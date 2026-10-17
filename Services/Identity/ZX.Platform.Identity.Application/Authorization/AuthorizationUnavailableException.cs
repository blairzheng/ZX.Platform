namespace ZX.Platform.Identity.Application.Authorization;

public sealed class AuthorizationUnavailableException(Exception innerException)
    : Exception("共享权限缓存不可用。", innerException) { }
