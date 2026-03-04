using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Redflag;
using System.Text.RegularExpressions;

namespace MAKER.AI.Clients
{
    public static class IAIClientExtensions
    {
        public static async Task<AIResponse> GuardedRequest(this IAIClient client, string prompt, List<IAIRedFlagValidator> validators, object? tools = null)
        {
            try
            {
                var responseObj = await client.Request(prompt, tools) ?? throw new AIRedFlagException("Received null response from the model.");
                var response = responseObj.Content ?? throw new AIRedFlagException("Received response with null content from the model.");

                var jsonMatch = Regex.Match(response, @"```json\s*(.*?)\s*```", RegexOptions.Singleline);
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
                return await client.GuardedRequest($"{prompt}\n\nLast response was rejected:\n{ex.Message}", validators, tools);
            }
        }
    }
}
