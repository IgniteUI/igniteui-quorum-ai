using Google.GenAI;
using MAKER.Configuration;

namespace MAKER.AI.Clients
{
    internal class GoogleAIClient(ExecutorConfig config, string model) : IAIClient
    {
        private readonly Client _client = new(apiKey: config.AIProviderKeys.Google);

        public async Task<string?> Request(string prompt)
        {
            var responseString = string.Empty;
            try
            {
                var response = await _client.Models.GenerateContentAsync(
                    model: model,
                    contents: prompt
                );

                responseString = response.Candidates?[0]?.Content?.Parts?[0]?.Text;
            }
            catch (ServerError)
            {
                await Task.Delay(2000);
                return await Request(prompt);
            }
            catch (ClientError ex)
            {
                if (ex.Status == "RESOURCE_EXHAUSTED")
                {
                    await Task.Delay(80000);
                    return await Request(prompt);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error during model request: {ex.Message}");
            }

            return responseString;
        }
    }
}
