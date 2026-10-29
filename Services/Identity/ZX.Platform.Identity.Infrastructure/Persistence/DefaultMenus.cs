using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence;

public static class DefaultMenus
{
    // 只补缺失节点，不覆盖管理员已维护的标题、图标与顺序。
    public static void AddMissing(IEnumerable<Menu> existing, Action<Menu> add)
    {
        var stored = existing.ToDictionary(menu => menu.Code, StringComparer.OrdinalIgnoreCase);
        Menu Ensure(string code, string kind, Menu? parent, string title, string english, int sort,
            string icon = "", string path = "", string component = "", string permission = "")
        {
            if (stored.TryGetValue(code, out var found)) return found;
            var menu = Menu.Create(code, kind, parent?.Id, title, english, icon, path, component, permission, sort, kind == Menu.Page, true);
            stored.Add(code, menu);
            add(menu);
            return menu;
        }
        var system = Ensure("system", Menu.Directory, null, "系统管理", "System", 10, "svg:lucide-settings");
        var access = Ensure("access", Menu.Directory, system, "权限管理", "Access control", 30, "svg:lucide-key-round");
        var users = Ensure("users", Menu.Page, system, "用户管理", "Users", 10, "svg:lucide-users", "/system/users", "SystemUser", IdentityPermissions.UserRead);
        var sbus = Ensure("sbus", Menu.Page, system, "SBU管理", "SBU management", 20, "svg:lucide-factory", "/system/sbus", "SystemSbu", IdentityPermissions.SbuRead);
        var roles = Ensure("roles", Menu.Page, access, "角色管理", "Roles", 10, "svg:lucide-shield-user", "/system/roles", "SystemRole", IdentityPermissions.RoleRead);
        var menus = Ensure("menus", Menu.Page, access, "菜单管理", "Menus", 20, "svg:lucide-list-tree", "/system/menus", "SystemMenu", IdentityPermissions.MenuRead);
        var actions = new (Menu Parent, string Permission, string Title, string English)[]
        {
            (users, IdentityPermissions.UserRead, "查看用户", "View users"),
            (users, IdentityPermissions.UserCreate, "新增用户", "Create users"),
            (users, IdentityPermissions.UserRename, "修改他人名称", "Rename other users"),
            (users, IdentityPermissions.UserChangeStatus, "启用或禁用用户", "Enable or disable users"),
            (users, IdentityPermissions.UserDelete, "删除用户", "Delete users"),
            (users, IdentityPermissions.UserRestore, "恢复用户", "Restore users"),
            (users, IdentityPermissions.UserAssignSbus, "分配用户SBU", "Assign user SBUs"),
            (users, IdentityPermissions.UserAssignRoles, "分配用户角色", "Assign user roles"),
            (users, IdentityPermissions.UserForceLogout, "强制用户下线", "Force users offline"),
            (sbus, IdentityPermissions.SbuRead, "查看SBU", "View SBUs"),
            (sbus, IdentityPermissions.SbuCreate, "新增SBU", "Create SBUs"),
            (sbus, IdentityPermissions.SbuRename, "修改SBU名称", "Rename SBUs"),
            (sbus, IdentityPermissions.SbuChangeStatus, "启用或禁用SBU", "Enable or disable SBUs"),
            (sbus, IdentityPermissions.SbuDelete, "删除SBU", "Delete SBUs"),
            (sbus, IdentityPermissions.SbuRestore, "恢复SBU", "Restore SBUs"),
            (roles, IdentityPermissions.RoleRead, "查看角色", "View roles"),
            (roles, IdentityPermissions.RoleCreate, "新增角色", "Create roles"),
            (roles, IdentityPermissions.RoleUpdate, "修改角色", "Edit roles"),
            (roles, IdentityPermissions.RoleChangeStatus, "启用或禁用角色", "Enable or disable roles"),
            (roles, IdentityPermissions.RoleDelete, "删除角色", "Delete roles"),
            (roles, IdentityPermissions.RoleAssignPermissions, "分配角色权限", "Assign role permissions"),
            (menus, IdentityPermissions.MenuRead, "查看菜单", "View menus"),
            (menus, IdentityPermissions.MenuCreate, "新增菜单", "Create menus"),
            (menus, IdentityPermissions.MenuUpdate, "修改菜单", "Edit menus"),
            (menus, IdentityPermissions.MenuChangeStatus, "启用或禁用菜单", "Enable or disable menus"),
            (menus, IdentityPermissions.MenuDelete, "删除菜单", "Delete menus")
        };
        var order = 0;
        foreach (var action in actions)
            Ensure("permission_" + action.Permission.Replace('.', '_'), Menu.Action, action.Parent, action.Title, action.English, ++order, permission: action.Permission);
    }
}
