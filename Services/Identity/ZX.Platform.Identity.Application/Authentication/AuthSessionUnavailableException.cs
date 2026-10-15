namespace ZX.Platform.Identity.Application.Authentication;

public sealed class AuthSessionUnavailableException(Exception innerException)
    : Exception("Authentication session storage is unavailable.", innerException);
