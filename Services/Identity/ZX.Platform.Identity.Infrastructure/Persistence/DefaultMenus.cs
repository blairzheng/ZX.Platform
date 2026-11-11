using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence;

public static class DefaultMenus
{
    // 只补缺失节点，不覆盖管理员已维护的标题、图标与顺序。
    public static void AddMissing(IEnumerable<Menu> existing, Action<Menu> add)
    {
        var stored = existing.ToDictionary(menu => menu.Code, StringComparer.OrdinalIgnoreCase);
        Menu Ensure(string code, string kind, Menu? parent, string title, string english, string vietnamese, int sort,
            string icon = "", string path = "", string component = "", string permission = "")
        {
            if (stored.TryGetValue(code, out var found)) return found;
            var menu = Menu.Create(code, kind, parent?.Id, title, english, icon, path, component, permission, sort, kind == Menu.Page,
                builtIn: true, titleVi: vietnamese);
            stored.Add(code, menu);
            add(menu);
            return menu;
        }
        var system = Ensure("system", Menu.Directory, null, "系统管理", "System", "Quản trị hệ thống", 10, "svg:lucide-settings");
        var access = Ensure("access", Menu.Directory, system, "权限管理", "Access control", "Quản lý quyền", 30, "svg:lucide-key-round");
        var users = Ensure("users", Menu.Page, system, "用户管理", "Users", "Quản lý người dùng", 10, "svg:lucide-users", "/system/users", "SystemUser", IdentityPermissions.UserRead);
        var sbus = Ensure("sbus", Menu.Page, system, "SBU管理", "SBU management", "Quản lý SBU", 20, "svg:lucide-factory", "/system/sbus", "SystemSbu", IdentityPermissions.SbuRead);
        var roles = Ensure("roles", Menu.Page, access, "角色管理", "Roles", "Quản lý vai trò", 10, "svg:lucide-shield-user", "/system/roles", "SystemRole", IdentityPermissions.RoleRead);
        var menus = Ensure("menus", Menu.Page, access, "菜单管理", "Menus", "Quản lý menu", 20, "svg:lucide-list-tree", "/system/menus", "SystemMenu", IdentityPermissions.MenuRead);
        // 越南语标题与 AccessMessages.vi-VN.resx 的权限显示名保持一致，避免同一权限出现两套说法。
        var actions = new (Menu Parent, string Permission, string Title, string English, string Vietnamese)[]
        {
            (users, IdentityPermissions.UserRead, "查看用户", "View users", "Xem người dùng"),
            (users, IdentityPermissions.UserCreate, "新增用户", "Create users", "Thêm người dùng"),
            (users, IdentityPermissions.UserRename, "修改他人名称", "Rename other users", "Sửa tên người khác"),
            (users, IdentityPermissions.UserChangeStatus, "启用或禁用用户", "Enable or disable users", "Kích hoạt hoặc vô hiệu hóa người dùng"),
            (users, IdentityPermissions.UserDelete, "删除用户", "Delete users", "Xóa người dùng"),
            (users, IdentityPermissions.UserRestore, "恢复用户", "Restore users", "Khôi phục người dùng"),
            (users, IdentityPermissions.UserAssignSbus, "分配用户SBU", "Assign user SBUs", "Gán SBU cho người dùng"),
            (users, IdentityPermissions.UserAssignRoles, "分配用户角色", "Assign user roles", "Gán vai trò cho người dùng"),
            (users, IdentityPermissions.UserForceLogout, "强制用户下线", "Force users offline", "Buộc người dùng đăng xuất"),
            (users, IdentityPermissions.UserApprove, "审批OA账号", "Approve OA accounts", "Phê duyệt kích hoạt tài khoản OA"),
            (sbus, IdentityPermissions.SbuRead, "查看SBU", "View SBUs", "Xem SBU"),
            (sbus, IdentityPermissions.SbuCreate, "新增SBU", "Create SBUs", "Thêm SBU"),
            (sbus, IdentityPermissions.SbuRename, "修改SBU名称", "Rename SBUs", "Sửa tên SBU"),
            (sbus, IdentityPermissions.SbuChangeStatus, "启用或禁用SBU", "Enable or disable SBUs", "Kích hoạt hoặc vô hiệu hóa SBU"),
            (sbus, IdentityPermissions.SbuDelete, "删除SBU", "Delete SBUs", "Xóa SBU"),
            (sbus, IdentityPermissions.SbuRestore, "恢复SBU", "Restore SBUs", "Khôi phục SBU"),
            (roles, IdentityPermissions.RoleRead, "查看角色", "View roles", "Xem vai trò"),
            (roles, IdentityPermissions.RoleCreate, "新增角色", "Create roles", "Thêm vai trò"),
            (roles, IdentityPermissions.RoleUpdate, "修改角色", "Edit roles", "Sửa thông tin vai trò"),
            (roles, IdentityPermissions.RoleChangeStatus, "启用或禁用角色", "Enable or disable roles", "Kích hoạt hoặc vô hiệu hóa vai trò"),
            (roles, IdentityPermissions.RoleDelete, "删除角色", "Delete roles", "Xóa vai trò"),
            (roles, IdentityPermissions.RoleAssignPermissions, "分配角色权限", "Assign role permissions", "Gán quyền cho vai trò"),
            (menus, IdentityPermissions.MenuRead, "查看菜单", "View menus", "Xem menu"),
            (menus, IdentityPermissions.MenuCreate, "新增菜单", "Create menus", "Thêm menu"),
            (menus, IdentityPermissions.MenuUpdate, "修改菜单", "Edit menus", "Sửa menu"),
            (menus, IdentityPermissions.MenuChangeStatus, "启用或禁用菜单", "Enable or disable menus", "Kích hoạt hoặc vô hiệu hóa menu"),
            (menus, IdentityPermissions.MenuDelete, "删除菜单", "Delete menus", "Xóa menu")
        };
        var order = 0;
        foreach (var action in actions)
            Ensure("permission_" + action.Permission.Replace('.', '_'), Menu.Action, action.Parent, action.Title, action.English,
                action.Vietnamese, ++order, permission: action.Permission);
    }
}
