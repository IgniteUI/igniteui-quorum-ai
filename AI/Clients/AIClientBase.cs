using MAKER.AI.Models;
using System.Reflection;
using System.Text.Json;

namespace MAKER.AI.Clients
{
    public class AIClientBase : IAIClient
    {
        protected object? _toolsObject;

        public Task<AIResponse?> Request(string prompt, object? toolsObject = null)
        {
            _toolsObject = toolsObject;

            List<AIFunctionInfo>? functions = null;
            if (toolsObject != null)
            {
                functions = GenerateFunctionInfo(toolsObject);
            }

            return RequestInternal(prompt, functions?.Count > 0 ? functions : null);
        }

        protected virtual Task<AIResponse?> RequestInternal(string prompt, List<AIFunctionInfo>? tools = null)
        {
            throw new NotImplementedException();
        }

        protected string InvokeTool(string functionName, string argumentsJson)
        {
            if (_toolsObject == null)
            {
                throw new InvalidOperationException($"Tool call for {functionName} failed: no tools object has been set.");
            }

            var method = _toolsObject.GetType().GetMethod(functionName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                ?? throw new MissingMethodException($"Tool call for {functionName} failed: no such public method found on {_toolsObject.GetType().Name}.");

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

            var result = method.Invoke(_toolsObject, args);
            return result?.ToString() ?? string.Empty;
        }

        protected virtual List<AIFunctionInfo> GenerateFunctionInfo(object toolsObject)
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
    }
}
