using MAKER.AI.Models;
using MAKER.Configuration;
using OpenAI.Chat;

using System.Text.Json;

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
namespace MAKER.AI.Clients
{
    internal sealed class OpenAIClient(ExecutorConfig config, string model, bool priority = false) : AIClientBase
    {
        private readonly ChatClient _client = new(model: model, apiKey: config.AIProviderKeys.OpenAI);

        protected override async Task<AIResponse?> RequestInternal(string prompt, List<AIFunctionInfo>? tools = null, object? toolsObject = null, CancellationToken cancellationToken = default)
        {
            var opts = new ChatCompletionOptions();

            if (priority)
            {
                opts.ServiceTier = new ChatServiceTier("priority");
            }

            List<ChatMessage> messages = [new UserChatMessage(prompt)];

            if (tools != null)
            {
                var chatTools = GenerateTools(tools);
                chatTools.ForEach(t => opts.Tools.Add(t));
                opts.AllowParallelToolCalls = true;
            }

            opts.ReasoningEffortLevel = ChatReasoningEffortLevel.Low;

            //#pragma warning disable SCME0001
            //  opts.Patch.Set("$.prompt_cache_retention"u8, "24h");
            //#pragma warning restore SCME0001

            int inputTokens = 0;
            int outputTokens = 0;
            bool requiresAction = false;
            do
            {
                requiresAction = false;
                var request = await _client.CompleteChatAsync(messages, opts, cancellationToken);

                if (request.Value == null)
                {
                    break;
                }

                inputTokens += request.Value.Usage.InputTokenCount;
                outputTokens += request.Value.Usage.OutputTokenCount;

                switch (request.Value.FinishReason)
                {
                    case ChatFinishReason.ToolCalls:
                        {
                            messages.Add(new AssistantChatMessage(request.Value));

                            foreach (ChatToolCall toolCall in request.Value.ToolCalls)
                            {
                                try
                                {
                                    var result = InvokeTool(toolCall.FunctionName, toolCall.FunctionArguments.ToString(), toolsObject!);

                                    messages.Add(new ToolChatMessage(
                                        toolCall.Id,
                                        result
                                    ));
                                }
                                catch (Exception ex)
                                {
                                    messages.Add(new ToolChatMessage(
                                        toolCall.Id,
                                        FormatToolError(ex)
                                    ));
                                }

                                // TODO: MCP, code exec, file search, etc
                                requiresAction = true;
                            }

                            break;
                        }

                    case ChatFinishReason.Stop:
                        {
                            messages.Add(new AssistantChatMessage(request.Value));
                            break;
                        }

                    case ChatFinishReason.Length:
                        throw new InvalidOperationException("Incomplete model output due to MaxTokens parameter or token limit exceeded.");

                    case ChatFinishReason.ContentFilter:
                        throw new InvalidOperationException("Omitted content due to a content filter flag.");

                    case ChatFinishReason.FunctionCall:
                        throw new InvalidOperationException("Deprecated in favor of tool calls.");

                    default:
                        throw new InvalidOperationException(request.Value.FinishReason.ToString());
                }

            } while (requiresAction);

            if (messages.Count <= 1)
            {
                return null;
            }

            return new AIResponse()
            {
                Content = messages.Last().Content[0].Text,
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }

        private List<ChatTool> GenerateTools(List<AIFunctionInfo> functions)
        {
            List<ChatTool> tools = [];

            foreach (var function in functions)
            {
                BinaryData? paramData = null;

                if (function.Parameters.Count > 0)
                {
                    var paramObj = new
                    {
                        type = "object",
                        properties = function.Parameters.ToDictionary(
                            p => p.Name,
                            p => new
                            {
                                type = p.ParameterType == typeof(string) ? "string" : "number",
                                description = p.Description
                            }
                        ),
                        required = function.Parameters.Where(p => p.IsRequired).Select(p => p.Name).ToArray(),
                        additionalProperties = false
                    };

                    var json = JsonSerializer.Serialize(paramObj);
                    paramData = BinaryData.FromString(json);
                }

                var tool = ChatTool.CreateFunctionTool(
                    functionName: function.Name,
                    functionDescription: function.Description,
                    functionParameters: paramData
                );

                tools.Add(tool);
            }

            return tools;
        }
    }
}
#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
