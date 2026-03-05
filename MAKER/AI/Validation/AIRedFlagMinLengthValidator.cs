using MAKER.AI.Exceptions;

namespace MAKER.AI.Validation
{
    public class AIRedFlagMinLengthValidator(int minLength) : IAIRedFlagValidator
    {
        public void Validate(string aiOutput)
        {
            if (string.IsNullOrEmpty(aiOutput) || aiOutput.Length < minLength)
            {
                throw new AIRedFlagException($"AI output is too short. Minimum length is {minLength}.");
            }
        }
    }
}
