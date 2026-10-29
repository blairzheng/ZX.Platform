using Microsoft.AspNetCore.Authorization;
using ZX.Platform.Identity.Api.Authorization;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Application.Roles;
using ZX.Platform.Identity.Infrastructure.Persistence;
using ZX.Platform.Identity.Infrastructure.Persistence.Repositories;
using ZX.Platform.Identity.Infrastructure.Security;
using ZX.Platform.Identity.Application.Menus;

namespace ZX.Platform.Identity.Api.Extensions;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddIdentityPermissions(this IServiceCollection services)
    {
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddSingleton<IPermissionCache, RedisPermissionCache>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddScoped<AuthorizationInitializer>();
        services.AddScoped<IMenuRepository, MenuRepository>();
        services.AddScoped<IMenuService, MenuService>();
        services.AddAuthorization(options =>
        {
            foreach (var code in IdentityPermissions.All)
                options.AddPolicy(code, policy => policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(code)));
            options.AddPolicy(PermissionPolicies.SbuOptions, policy => policy.RequireAuthenticatedUser().AddRequirements(
                new PermissionRequirement(IdentityPermissions.SbuRead, IdentityPermissions.UserRead,
                    IdentityPermissions.UserCreate, IdentityPermissions.UserAssignSbus)));
            options.AddPolicy(PermissionPolicies.MenuOptions, policy => policy.RequireAuthenticatedUser().AddRequirements(
                new PermissionRequirement(IdentityPermissions.MenuCreate, IdentityPermissions.MenuUpdate)));
        });
        return services;
    }
}
