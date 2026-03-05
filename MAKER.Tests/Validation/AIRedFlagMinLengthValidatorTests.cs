using MAKER.AI.Exceptions;
using MAKER.AI.Validation;

namespace MAKER.Tests.Validation
{
    public class AIRedFlagMinLengthValidatorTests
    {
        [Fact]
        public void Validate_InputMeetsMinLength_DoesNotThrow()
        {
            var validator = new AIRedFlagMinLengthValidator(5);

            validator.Validate("Hello");
        }

        [Fact]
        public void Validate_InputExceedsMinLength_DoesNotThrow()
        {
            var validator = new AIRedFlagMinLengthValidator(3);

            validator.Validate("Hello World");
        }

        [Fact]
        public void Validate_InputBelowMinLength_ThrowsAIRedFlagException()
        {
            var validator = new AIRedFlagMinLengthValidator(10);

            Assert.Throws<AIRedFlagException>(() => validator.Validate("Hi"));
        }

        [Fact]
        public void Validate_EmptyString_ThrowsAIRedFlagException()
        {
            var validator = new AIRedFlagMinLengthValidator(1);

            Assert.Throws<AIRedFlagException>(() => validator.Validate(string.Empty));
        }

        [Fact]
        public void Validate_NullString_ThrowsAIRedFlagException()
        {
            var validator = new AIRedFlagMinLengthValidator(1);

            Assert.Throws<AIRedFlagException>(() => validator.Validate(null!));
        }
    }
}
