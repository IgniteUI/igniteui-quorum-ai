using Google.GenAI;
using Google.GenAI.Types;
using MAKER.AI.Models;
using MAKER.Configuration;

using System.Text.Json;

namespace MAKER.AI.Clients
{
    internal sealed class GoogleAIClient(ExecutorConfig config, string model) : AIClientBase
    {
        private readonly Client _client = new(apiKey: config.AIProviderKeys.Google);

        protected override async Task<AIResponse?> RequestInternal(string prompt, List<AIFunctionInfo>? tools = null, object? toolsObject = null, CancellationToken cancellationToken = default)
        {
            var contents = new List<Content>
            {
                new() { Role = "user", Parts = [new Part { Text = prompt }] }
            };

            var requestConfig = tools != null
                ? new GenerateContentConfig
                {
                    Tools = [BuildTool(tools)],
                    ToolConfig = new ToolConfig
                    {
                        FunctionCallingConfig = new FunctionCallingConfig { Mode = FunctionCallingConfigMode.AUTO }
                    }
                }
                : null;

            int inputTokens = 0;
            int outputTokens = 0;

            GenerateContentResponse? lastResponse = null;
            bool requiresAction = false;
            do
            {
                requiresAction = false;
                var response = await _client.Models.GenerateContentAsync(
                    model: model,
                    contents: contents,
                    config: requestConfig
                );

                lastResponse = response;
                inputTokens += response.UsageMetadata?.PromptTokenCount ?? 0;
                outputTokens += response.UsageMetadata?.CandidatesTokenCount ?? 0;

                var candidate = response.Candidates?[0];
                if (candidate == null) break;

                switch (candidate.FinishReason)
                {
                    case FinishReason.STOP or null:
                        {
                            var functionCalls = candidate.Content?.Parts?
                                .Where(p => p.FunctionCall != null)
                                .Select(p => p.FunctionCall!)
                                .ToList();

                            if (functionCalls?.Count > 0)
                            {
                                contents.Add(candidate.Content!);

                                var responseParts = new List<Part>();
                                foreach (var call in functionCalls)
                                {
                                    var argsJson = JsonSerializer.Serialize(call.Args);
                                    string result;
                                    try
                                    {
                                        result = InvokeTool(call.Name!, argsJson, toolsObject!);
                                    }
                                    catch (Exception ex)
                                    {
                                        result = FormatToolError(ex);
                                    }

                                    responseParts.Add(new Part
                                    {
                                        FunctionResponse = new FunctionResponse
                                        {
                                            Name = call.Name,
                                            Response = new Dictionary<string, object> { ["result"] = result }
                                        }
                                    });

                                    // TODO: MCP, code exec, file search, etc
                                }

                                contents.Add(new Content { Role = "user", Parts = responseParts });
                                requiresAction = true;
                            }

                            break;
                        }

                    case FinishReason.MAX_TOKENS:
                        throw new InvalidOperationException("Incomplete model output due to MaxTokens parameter or token limit exceeded.");

                    default:
                        throw new InvalidOperationException(candidate.FinishReason?.ToString());
                }

            } while (requiresAction);

            if (lastResponse == null) return null;

            var text = lastResponse.Candidates?[0]?.Content?.Parts?
                .FirstOrDefault(p => p.Text != null)?.Text;

            return new AIResponse()
            {
                Content = text,
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }

        protected override bool IsTransientError(Exception ex, out int delayMs)
        {
            if (ex is ServerError)
            {
                delayMs = 2000;
                return true;
            }

            if (ex is ClientError clientError && clientError.Status == "RESOURCE_EXHAUSTED")
            {
                delayMs = 80000;
                return true;
            }

            return base.IsTransientError(ex, out delayMs);
        }

        private static Tool BuildTool(List<AIFunctionInfo> functions)
        {
            return new Tool
            {
                FunctionDeclarations = functions.Select(fn => new FunctionDeclaration
                {
                    Name = fn.Name,
                    Description = fn.Description,
                    Parameters = fn.Parameters.Count > 0
                        ? new Schema
                        {
                            Type = Google.GenAI.Types.Type.OBJECT,
                            Properties = fn.Parameters.ToDictionary(
                                p => p.Name,
                                p => new Schema
                                {
                                    Type = p.ParameterType == typeof(string)
                                        ? Google.GenAI.Types.Type.STRING
                                        : Google.GenAI.Types.Type.NUMBER,
                                    Description = p.Description
                                }
                            ),
                            Required = fn.Parameters.Where(p => p.IsRequired).Select(p => p.Name).ToList()
                        }
                        : new Schema { Type = Google.GenAI.Types.Type.OBJECT }
                }).ToList()
            };
        }
    }
}
