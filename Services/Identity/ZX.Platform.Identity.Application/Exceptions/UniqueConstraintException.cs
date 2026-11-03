namespace ZX.Platform.Identity.Application.Exceptions
{
    public class UniqueConstraintException : Exception
    {
        public UniqueConstraintException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
