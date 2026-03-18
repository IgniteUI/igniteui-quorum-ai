using MAKER.AI.Constants;
using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using System.Text.RegularExpressions;

namespace MAKER.AI.Validation
{
    public static partial class StepAtomicityValidator
    {
        public const int MaxStepLength = 128;

        /// <summary>
        /// Validates that a step is atomic — modifies exactly one thing.
        /// Throws <see cref="AIRedFlagException"/> if the step appears to modify multiple things.
        /// </summary>
        public static void Validate(Step step)
        {
            if (string.IsNullOrEmpty(step.Task) || step.Task == AIResponses.End)
                return;

            if (step.Task.Length > MaxStepLength)
            {
                throw new AIRedFlagException(
                    $"Step exceeds maximum length of {MaxStepLength} characters. " +
                    "Atomic steps must be concise and modify exactly one thing.");
            }

            if (MultiActionPattern().IsMatch(step.Task))
            {
                throw new AIRedFlagException(
                    $"Step appears to modify multiple things. " +
                    "Each step must modify exactly ONE field, value, or concept.");
            }
        }

        /// <summary>
        /// Validates all steps in a collection.
        /// </summary>
        public static void ValidateAll(IEnumerable<Step> steps)
        {
            foreach (var step in steps)
            {
                Validate(step);
            }
        }

        // Matches two action verbs connected by " and " or ", " — a strong signal of multi-modification.
        // Excludes "and set/assign" since the rules allow "determine + set" as a single step.
        [GeneratedRegex(
            @"\b(determine|calculate|compute|generate|select|create|update|find|choose|derive|extract|identify|resolve|evaluate|transform|convert|define|establish|produce|build|compose|format)\b" +
            @".{1,80}" +
            @"\b(and|,)\s+(also\s+|then\s+)?" +
            @"\b(determine|calculate|compute|generate|select|create|update|find|choose|derive|extract|identify|resolve|evaluate|transform|convert|define|establish|produce|build|compose|format)\b",
            RegexOptions.IgnoreCase)]
        private static partial Regex MultiActionPattern();
    }
}
