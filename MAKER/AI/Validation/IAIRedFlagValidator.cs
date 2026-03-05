namespace MAKER.AI.Validation
{
    public interface IAIRedFlagValidator
    {
        /// <summary>
        /// Attempts to validate the AI output against a rule and throws an <see cref="Exceptions.AIRedFlagException"/> if it fails.
        /// </summary>
        /// <param name="aiOutput">The AI output to validate.</param>
        /// <exception cref="Exceptions.AIRedFlagException">Thrown when the AI output fails validation.</exception>
        void Validate(string aiOutput);
    }
}
