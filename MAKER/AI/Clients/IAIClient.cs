using MAKER.AI.Models;

namespace MAKER.AI.Clients
{
    /// <summary>
    /// Defines a contract for AI clients that can send prompts and receive generated responses.
    /// </summary>
    public interface IAIClient
    {
        /// <summary>
        /// Sends the specified prompt and asynchronously returns the AI-generated response.
        /// </summary>
        /// <param name="prompt">The prompt text to send for processing.</param>
        /// <param name="toolsObject">
        /// An optional object whose public methods are exposed to the model as callable tools.
        /// Methods decorated with <see cref="AIDescription"/> provide descriptions to the model.
        /// </param>
        /// <returns>The AI response, or <c>null</c> if no response was produced.</returns>
        Task<AIResponse?> Request(string prompt, object? toolsObject = null);
    }
}
