namespace ZX.Platform.Identity.Application.Authorization;

public sealed record AuthorizationSnapshot(bool IsAdministrator, string[] Roles, string[] Permissions);

public sealed record CachedAuthorization(string Version, AuthorizationSnapshot? Snapshot);
