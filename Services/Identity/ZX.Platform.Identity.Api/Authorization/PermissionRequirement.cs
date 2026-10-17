using Microsoft.AspNetCore.Authorization;

namespace ZX.Platform.Identity.Api.Authorization;

public sealed record PermissionRequirement(params string[] Codes) : IAuthorizationRequirement;

public static class PermissionPolicies
{
    public const string FactoryOptions = "FactoryOptions";
    public const string MenuOptions = "MenuOptions";
}
