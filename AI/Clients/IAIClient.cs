namespace MAKER.AI.Clients
{
    /// <summary>
    /// Defines a contract for AI clients that can send prompts and receive generated responses.
    /// </summary>
    public interface IAIClient
    {
        /// <summary>
        /// Sends the specified prompt and asynchronously returns the generated response as a string.
        /// </summary>
        /// <param name="prompt">The prompt text to send for processing.</param>
        /// <returns>A string response from the AI model.</returns>
        Task<string?> Request(string prompt);
    }
}
