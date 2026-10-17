using System.Reflection;

namespace ZX.Platform.Identity.Application.Authorization;

// 新增功能时在这里声明，并在 Controller 使用同一个常量；启动时自动同步目录。
// 删除功能时删除相应声明；数据库中的旧权限将停用，已有角色也不再获得它。
public static class IdentityPermissions
{
    public const string UserRead = "identity.user.read";
    public const string UserCreate = "identity.user.create";
    public const string UserRename = "identity.user.rename";
    public const string UserChangeStatus = "identity.user.status";
    public const string UserDelete = "identity.user.delete";
    public const string UserRestore = "identity.user.restore";
    public const string UserAssignFactories = "identity.user.factories";
    public const string UserAssignRoles = "identity.user.roles";
    public const string UserForceLogout = "identity.user.forceLogout";
    public const string FactoryRead = "identity.factory.read";
    public const string FactoryCreate = "identity.factory.create";
    public const string FactoryRename = "identity.factory.rename";
    public const string FactoryChangeStatus = "identity.factory.status";
    public const string FactoryDelete = "identity.factory.delete";
    public const string FactoryRestore = "identity.factory.restore";
    public const string RoleRead = "identity.role.read";
    public const string RoleCreate = "identity.role.create";
    public const string RoleUpdate = "identity.role.update";
    public const string RoleChangeStatus = "identity.role.status";
    public const string RoleDelete = "identity.role.delete";
    public const string RoleAssignPermissions = "identity.role.permissions";
    public const string MenuRead = "identity.menu.read";
    public const string MenuCreate = "identity.menu.create";
    public const string MenuUpdate = "identity.menu.update";
    public const string MenuChangeStatus = "identity.menu.status";
    public const string MenuDelete = "identity.menu.delete";

    public static IReadOnlyList<string> All { get; } = typeof(IdentityPermissions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .Order(StringComparer.Ordinal).ToArray();
}
