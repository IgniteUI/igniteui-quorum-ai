using Anthropic;
using Anthropic.Models.Messages;
using MAKER.AI.Models;
using MAKER.Configuration;

using System.Text.Json;

namespace MAKER.AI.Clients
{
    internal class AnthropicAIClient(ExecutorConfig config, string model) : AIClientBase
    {
        private readonly AnthropicClient _client = new() { APIKey = config.AIProviderKeys.Anthropic };
        private readonly Model _model = Enum.Parse<Model>(model);

        protected override async Task<AIResponse?> RequestInternal(string prompt, List<AIFunctionInfo>? tools = null)
        {
            List<MessageParam> messages = [new() { Role = Role.User, Content = prompt }];

            List<ToolUnion>? toolUnions = tools != null ? BuildToolUnions(tools) : null;

            int inputTokens = 0;
            int outputTokens = 0;
            Message? lastResponse = null;
            bool requiresAction = false;
            do
            {
                requiresAction = false;
                var request = await _client.Messages.Create(toolUnions != null
                    ? new MessageCreateParams
                    {
                        MaxTokens = 8192,
                        Messages = messages,
                        Model = _model,
                        Tools = toolUnions,
                        ToolChoice = new ToolChoice(new ToolChoiceAuto()),
                    }
                    : new MessageCreateParams
                    {
                        MaxTokens = 8192,
                        Messages = messages,
                        Model = _model,
                    });

                if (request == null)
                {
                    break;
                }

                lastResponse = request;
                inputTokens += (int)request.Usage.InputTokens;
                outputTokens += (int)request.Usage.OutputTokens;

                // NOTE: ApiEnum == is broken in SDK v11; cast to the enum to compare
                switch ((StopReason?)request.StopReason)
                {
                    case StopReason.ToolUse:
                        {
                            messages.Add(new MessageParam
                            {
                                Role = Role.Assistant,
                                Content = new MessageParamContent(BuildAssistantContent(request)),
                            });

                            foreach (var block in request.Content)
                            {
                                if (!block.TryPickToolUse(out var toolUse))
                                    continue;

                                var inputJson = JsonSerializer.Serialize(toolUse.Input);

                                try
                                {
                                    var result = InvokeTool(toolUse.Name, inputJson);

                                    messages.Add(new MessageParam
                                    {
                                        Role = Role.User,
                                        Content = new MessageParamContent([new ContentBlockParam(new ToolResultBlockParam
                                        {
                                            ToolUseID = toolUse.ID,
                                            Content = new ToolResultBlockParamContent(result),
                                        })]),
                                    });
                                }
                                catch (Exception ex)
                                {
                                    var inner = ex.InnerException ?? ex;
                                    messages.Add(new MessageParam
                                    {
                                        Role = Role.User,
                                        Content = new MessageParamContent([new ContentBlockParam(new ToolResultBlockParam
                                        {
                                            ToolUseID = toolUse.ID,
                                            Content = new ToolResultBlockParamContent(
                                                $"[ERROR] [{inner.GetType().Name}]: {inner.Message}"),
                                        })]),
                                    });
                                }

                                // TODO: MCP, code exec, file search, etc
                                requiresAction = true;
                            }

                            break;
                        }

                    case StopReason.EndTurn:
                        break;

                    case StopReason.MaxTokens:
                        throw new Exception("Incomplete model output due to MaxTokens parameter or token limit exceeded.");

                    default:
                        throw new InvalidOperationException(request.StopReason?.ToString());
                }

            } while (requiresAction);

            if (lastResponse == null || lastResponse.Content.Count == 0)
            {
                return null;
            }

            lastResponse.Content[lastResponse.Content.Count - 1].TryPickText(out var finalText);

            return new AIResponse()
            {
                Content = finalText?.Text,
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }

        private static List<ContentBlockParam> BuildAssistantContent(Message response)
        {
            List<ContentBlockParam> content = [];

            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var text))
                {
                    content.Add(new ContentBlockParam(new TextBlockParam { Text = text.Text }));
                }
                else if (block.TryPickToolUse(out var toolUse))
                {
                    content.Add(new ContentBlockParam(new ToolUseBlockParam
                    {
                        ID = toolUse.ID,
                        Name = toolUse.Name,
                        Input = toolUse.Input,
                    }));
                }
            }

            return content;
        }

        private static List<ToolUnion> BuildToolUnions(List<AIFunctionInfo> functions)
        {
            List<ToolUnion> tools = [];

            foreach (var fn in functions)
            {
                InputSchema inputSchema;

                if (fn.Parameters.Count > 0)
                {
                    inputSchema = new InputSchema
                    {
                        Type = JsonSerializer.Deserialize<JsonElement>(@"""object"""),
                        Properties = fn.Parameters.ToDictionary(
                            p => p.Name,
                            p => JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new
                            {
                                type = p.ParameterType == typeof(string) ? "string" : "number",
                                description = p.Description
                            }))
                        ),
                        Required = fn.Parameters.Where(p => p.IsRequired).Select(p => p.Name).ToList(),
                    };
                }
                else
                {
                    inputSchema = new InputSchema
                    {
                        Type = JsonSerializer.Deserialize<JsonElement>(@"""object"""),
                    };
                }

                tools.Add(new ToolUnion(new Tool
                {
                    Name = fn.Name,
                    Description = string.IsNullOrEmpty(fn.Description) ? null : fn.Description,
                    InputSchema = inputSchema,
                }));
            }

            return tools;
        }
    }
}
