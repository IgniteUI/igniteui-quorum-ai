namespace MAKER.AI.Exceptions
{
    public class AIRedFlagException : Exception
    {
        public AIRedFlagException() { }
        public AIRedFlagException(string message) : base(message) { }
        public AIRedFlagException(string message, Exception inner) : base(message, inner) { }
    }
}
