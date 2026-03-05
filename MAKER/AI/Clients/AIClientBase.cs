using MAKER.AI.Attributes;
using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Validation;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MAKER.AI.Clients
{
    public abstract partial class AIClientBase : IAIClient
    {
        public int MaxRequestRetries { get; set; } = 3;

        public int MaxGuardedRetries { get; set; } = 3;

        public async Task<AIResponse?> Request(string prompt, object? toolsObject = null, CancellationToken cancellationToken = default)
        {
            List<AIFunctionInfo>? functions = null;
            if (toolsObject != null)
            {
                functions = GenerateFunctionInfo(toolsObject);
            }

            var tools = functions?.Count > 0 ? functions : null;

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return await RequestInternal(prompt, tools, toolsObject, cancellationToken);
                }
                catch (Exception ex) when (attempt < MaxRequestRetries && IsTransientError(ex, out int delayMs))
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
        }

        protected virtual bool IsTransientError(Exception ex, out int delayMs)
        {
            delayMs = 0;

            if (ex is HttpRequestException)
            {
                delayMs = 2000;
                return true;
            }

            return false;
        }

        public Task<AIResponse> GuardedRequest(string prompt, List<IAIRedFlagValidator> validators, object? tools = null, CancellationToken cancellationToken = default)
        {
            return GuardedRequestInternal(prompt, validators, tools, MaxGuardedRetries, cancellationToken);
        }

        private async Task<AIResponse> GuardedRequestInternal(string prompt, List<IAIRedFlagValidator> validators, object? tools, int remainingRetries, CancellationToken cancellationToken)
        {
            try
            {
                var responseObj = await Request(prompt, tools, cancellationToken) ?? throw new AIRedFlagException("Received null response from the model.");
                var response = responseObj.Content ?? throw new AIRedFlagException("Received response with null content from the model.");

                var jsonMatch = JsonCodeBlockRegex().Match(response);
                response = jsonMatch.Success ? jsonMatch.Groups[1].Value.Trim() : response.Trim();

                validators.ForEach(validator => validator.Validate(response));

                return new AIResponse()
                {
                    Content = response,
                    InputTokens = responseObj.InputTokens,
                    OutputTokens = responseObj.OutputTokens,
                };
            }
            catch (AIRedFlagException ex)
            {
                if (remainingRetries <= 0) throw;
                return await GuardedRequestInternal($"{prompt}\n\nLast response was rejected:\n{ex.Message}", validators, tools, remainingRetries - 1, cancellationToken);
            }
        }

        protected abstract Task<AIResponse?> RequestInternal(string prompt, List<AIFunctionInfo>? tools = null, object? toolsObject = null, CancellationToken cancellationToken = default);

        protected static string FormatToolError(Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            return $"[ERROR] [{inner.GetType().Name}]: {inner.Message}";
        }

        protected static string InvokeTool(string functionName, string argumentsJson, object toolsObject)
        {
            ArgumentNullException.ThrowIfNull(toolsObject);

            var method = toolsObject.GetType().GetMethod(functionName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                ?? throw new MissingMethodException($"Tool call for {functionName} failed: no such public method found on {toolsObject.GetType().Name}.");

            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];

            using var argumentsDoc = JsonDocument.Parse(argumentsJson);

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];

                if (argumentsDoc.RootElement.TryGetProperty(param.Name!, out var argValue))
                {
                    var targetType = Nullable.GetUnderlyingType(param.ParameterType) ?? param.ParameterType;

                    try
                    {
                        args[i] = argValue.ValueKind switch
                        {
                            JsonValueKind.Null => param.ParameterType.IsValueType && Nullable.GetUnderlyingType(param.ParameterType) == null
                                ? throw new InvalidCastException($"Cannot assign null to non-nullable parameter '{param.Name}' of type {param.ParameterType.Name}.")
                                : null,
                            JsonValueKind.String => Convert.ChangeType(argValue.GetString(), targetType),
                            JsonValueKind.Number when targetType == typeof(int) => argValue.GetInt32(),
                            JsonValueKind.Number when targetType == typeof(long) => argValue.GetInt64(),
                            JsonValueKind.Number when targetType == typeof(double) => argValue.GetDouble(),
                            JsonValueKind.Number when targetType == typeof(float) => argValue.GetSingle(),
                            JsonValueKind.Number when targetType == typeof(decimal) => argValue.GetDecimal(),
                            JsonValueKind.Number => Convert.ChangeType(argValue.GetDouble(), targetType),
                            JsonValueKind.True or JsonValueKind.False when targetType == typeof(bool) => argValue.GetBoolean(),
                            _ => JsonSerializer.Deserialize(argValue.GetRawText(), param.ParameterType)
                        };
                    }
                    catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or JsonException)
                    {
                        throw new ArgumentException(
                            $"Tool call for {functionName} failed: cannot convert argument '{param.Name}' to {param.ParameterType.Name}.", ex);
                    }
                }
                else if (param.HasDefaultValue)
                {
                    args[i] = param.DefaultValue;
                }
                else
                {
                    throw new ArgumentException($"Tool call for {functionName} failed: missing required argument '{param.Name}'.");
                }
            }

            var result = method.Invoke(toolsObject, args);
            return result?.ToString() ?? string.Empty;
        }

        protected List<AIFunctionInfo> GenerateFunctionInfo(object toolsObject)
        {
            var functionInfos = new List<AIFunctionInfo>();
            Type objectType = toolsObject.GetType();

            var methods = objectType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                var description = method.GetCustomAttributes<AIDescription>().FirstOrDefault()?.Description ?? string.Empty;

                var functionInfo = new AIFunctionInfo
                {
                    Name = method.Name,
                    Description = description,
                    Parameters = [.. parameters.Select(p => new AIFunctionParameterInfo
                    {
                        Name = p.Name!,
                        ParameterType = p.ParameterType,
                        Description = p.GetCustomAttributes<AIDescription>().FirstOrDefault()?.Description ?? string.Empty,
                        IsRequired = !p.IsOptional
                    })]
                };

                functionInfos.Add(functionInfo);
            }

            return functionInfos;
        }

        [GeneratedRegex(@"```json\s*(.*?)\s*```", RegexOptions.Singleline)]
        private static partial Regex JsonCodeBlockRegex();
    }
}
