namespace ZX.Platform.Identity.Domain.Exceptions
{
    public sealed class DomainException : Exception
    {
        public DomainException(string messageKey) : base(messageKey)
        {
            MessageKey = messageKey;
        }

        public string MessageKey { get; }
    }
}
