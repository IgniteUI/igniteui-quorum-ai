using MAKER.AI.Models;
using MAKER.AI.Validation;

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
        Task<AIResponse?> Request(string prompt, object? toolsObject = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a prompt with validation, automatically retrying if the response fails validation checks.
        /// </summary>
        /// <param name="prompt">The prompt text to send for processing.</param>
        /// <param name="validators">A list of validators to check the AI response against.</param>
        /// <param name="tools">An optional tools object whose methods are exposed to the model.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The validated AI response.</returns>
        Task<AIResponse> GuardedRequest(string prompt, List<IAIRedFlagValidator> validators, object? tools = null, CancellationToken cancellationToken = default);
    }
}
