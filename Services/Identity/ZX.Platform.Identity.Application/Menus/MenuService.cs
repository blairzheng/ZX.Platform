using System.Text.RegularExpressions;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Menus.Dtos;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Menus;

public sealed class MenuService(IMenuRepository menus, IUnitOfWork unitOfWork) : IMenuService
{
    public async Task<List<MenuDto>> GetTreeAsync(CancellationToken cancellationToken) =>
        Tree(await menus.GetAllAsync(cancellationToken), null, null);

    public async Task<List<MenuDto>> GetCurrentAsync(AuthorizationSnapshot authorization, CancellationToken cancellationToken) =>
        Tree(await menus.GetAllAsync(cancellationToken), null, authorization);

    // 菜单是展示与分组，授权仍只保存 PermissionCode；未挂菜单的新权限也可见，不会静默丢失已有授权。
    public async Task<List<MenuDto>> GetPermissionTreeAsync(CancellationToken cancellationToken)
    {
        var tree = Tree(await menus.GetAllAsync(cancellationToken), null, null);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        void AppendReadPermissions(List<MenuDto> nodes)
        {
            nodes.RemoveAll(node => node.Kind != Menu.Directory && !IdentityPermissions.All.Contains(node.PermissionCode, StringComparer.Ordinal));
            foreach (var node in nodes)
            {
                if (node.Kind == Menu.Page && IdentityPermissions.All.Contains(node.PermissionCode, StringComparer.Ordinal) &&
                    !node.Children.Any(child => child.PermissionCode == node.PermissionCode))
                    node.Children.Insert(0, new MenuDto { Code = "__view_" + node.Code, Kind = Menu.Action,
                        Title = "查看页面", TitleEn = "View page", PermissionCode = node.PermissionCode });
                if (node.Kind == Menu.Action) placed.Add(node.PermissionCode);
                AppendReadPermissions(node.Children);
            }
            nodes.RemoveAll(node => node.Kind != Menu.Action && node.Children.Count == 0);
        }
        AppendReadPermissions(tree);
        var remaining = IdentityPermissions.All.Where(code => !placed.Contains(code)).ToArray();
        if (remaining.Length > 0) tree.Add(new MenuDto { Code = "__unassigned", Kind = Menu.Directory,
            Title = "未归类权限", TitleEn = "Unassigned permissions", Children = remaining.Select(code => new MenuDto
            { Code = "__permission_" + code, Kind = Menu.Action, Title = code, TitleEn = code, PermissionCode = code }).ToList() });
        return tree;
    }

    private static List<MenuDto> Tree(List<Menu> all, Guid? parentId, AuthorizationSnapshot? authorization, int depth = 0)
    {
        if (depth > 32) return [];
        var result = new List<MenuDto>();
        foreach (var menu in all.Where(item => item.ParentId == parentId).OrderBy(item => item.Sort).ThenBy(item => item.Code))
        {
            if (authorization is not null && (!menu.IsEnabled || menu.Kind == Menu.Action)) continue;
            var children = Tree(all, menu.Id, authorization, depth + 1);
            if (authorization is not null &&
                ((menu.Kind == Menu.Directory && menu.IsBuiltIn && children.Count == 0) ||
                 menu.Kind != Menu.Directory &&
                 (!IdentityPermissions.All.Contains(menu.PermissionCode, StringComparer.Ordinal) ||
                  !authorization.IsAdministrator && !authorization.Permissions.Contains(menu.PermissionCode, StringComparer.Ordinal))))
                continue;
            result.Add(new MenuDto { Id = menu.Id, ParentId = menu.ParentId, Code = menu.Code, Kind = menu.Kind,
                Title = menu.Title, TitleEn = menu.TitleEn, Icon = menu.Icon, Path = menu.Path, Component = menu.Component,
                PermissionCode = menu.PermissionCode,
                PermissionActive = string.IsNullOrWhiteSpace(menu.PermissionCode) ||
                    IdentityPermissions.All.Contains(menu.PermissionCode, StringComparer.Ordinal),
                LauncherTone = menu.LauncherTone,
                LauncherDescription = menu.LauncherDescription, LauncherDescriptionEn = menu.LauncherDescriptionEn,
                Sort = menu.Sort, KeepAlive = menu.KeepAlive,
                ShowInMenu = menu.ShowInMenu, ShowInTags = menu.ShowInTags, Affix = menu.Affix,
                IsEnabled = menu.IsEnabled, IsBuiltIn = menu.IsBuiltIn, Children = children });
        }
        return result;
    }

    public async Task<Guid> CreateAsync(CreateMenuDto input, CancellationToken cancellationToken)
    {
        var all = await menus.GetAllAsync(cancellationToken);
        Validate(input, null, all);
        var code = await GenerateCodeAsync(cancellationToken);
        var menu = Menu.Create(code, input.Kind, input.ParentId, input.Title, input.TitleEn,
            input.Icon, input.Path, input.Component, input.PermissionCode, input.Sort, input.KeepAlive,
            showInMenu: input.ShowInMenu, showInTags: input.ShowInTags, affix: input.Affix,
            launcherTone: input.LauncherTone, launcherDescription: input.LauncherDescription,
            launcherDescriptionEn: input.LauncherDescriptionEn);
        menus.Add(menu);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return menu.Id;
    }

    private async Task<string> GenerateCodeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var code = $"menu_{Guid.NewGuid():N}";
            if (!await menus.CodeExistsAsync(code, cancellationToken)) return code;
        }
    }

    public async Task UpdateAsync(UpdateMenuDto input, CancellationToken cancellationToken)
    {
        var menu = await FindAsync(input.Id, cancellationToken);
        Validate(input, menu.Id, await menus.GetAllAsync(cancellationToken));
        menu.Update(input.Kind, input.ParentId, input.Title, input.TitleEn, input.Icon,
            input.Path, input.Component, input.PermissionCode, input.Sort, input.KeepAlive,
            input.ShowInMenu, input.ShowInTags, input.Affix, input.LauncherTone,
            input.LauncherDescription, input.LauncherDescriptionEn);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static void Validate(MenuInputDto input, Guid? id, List<Menu> all)
    {
        if (input.Kind is not (Menu.Directory or Menu.Page or Menu.Action)) throw Error("Access.InvalidMenu");
        var parent = input.ParentId is Guid parentId ? all.FirstOrDefault(menu => menu.Id == parentId) : null;
        if (input.ParentId is not null && parent is null) throw Error("Access.InvalidMenuParent");
        if (input.Kind == Menu.Action ? parent?.Kind != Menu.Page : parent is not null && parent.Kind != Menu.Directory)
            throw Error("Access.InvalidMenuParent");
        var visited = new HashSet<Guid>();
        while (parent is not null)
        {
            if (parent.Id == id || !visited.Add(parent.Id) || visited.Count > 30) throw Error("Access.InvalidMenuParent");
            parent = all.FirstOrDefault(menu => menu.Id == parent.ParentId);
        }
        if (id is not null && all.Any(menu => menu.ParentId == id && (input.Kind == Menu.Action ||
            (input.Kind == Menu.Page ? menu.Kind != Menu.Action : menu.Kind == Menu.Action)))) throw Error("Access.MenuHasChildren");
        if (input.Kind != Menu.Directory && !IdentityPermissions.All.Contains(input.PermissionCode, StringComparer.Ordinal))
            throw Error("Access.UnknownPermission");
        if (input.Kind == Menu.Directory && (input.PermissionCode.Length > 0 || input.Path.Length > 0 || input.Component.Length > 0))
            throw Error("Access.InvalidMenu");
        if (input.Kind == Menu.Action && (input.Path.Length > 0 || input.Component.Length > 0)) throw Error("Access.InvalidMenu");
        if (input.Affix && (input.Kind != Menu.Page || !input.ShowInTags)) throw Error("Access.InvalidMenu");
        if (input.Kind == Menu.Page)
        {
            if (!Regex.IsMatch(input.Path, @"^/[a-zA-Z0-9_-]+(/[a-zA-Z0-9_-]+)*$", RegexOptions.CultureInvariant) ||
                new[] { "/login", "/overview", "/applications", "/examples/users" }.Contains(input.Path, StringComparer.OrdinalIgnoreCase) ||
                input.Component is not ("SystemUser" or "SystemSbu" or "SystemRole" or "SystemMenu")) throw Error("Access.InvalidMenuRoute");
            if (all.Any(menu => menu.Id != id && menu.Kind == Menu.Page && string.Equals(menu.Path, input.Path, StringComparison.OrdinalIgnoreCase)))
                throw Error("Access.MenuPathExists", ErrorCodes.Conflict);
        }
        if (input.Kind == Menu.Action && all.Any(menu => menu.Id != id && menu.Kind == Menu.Action && menu.PermissionCode == input.PermissionCode))
            throw Error("Access.MenuPermissionExists", ErrorCodes.Conflict);
        if (input.Icon.Length > 0 && !Regex.IsMatch(input.Icon, @"^(svg|ep):[a-zA-Z][a-zA-Z0-9_-]*$", RegexOptions.CultureInvariant))
            throw Error("Access.InvalidMenuIcon");
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        (await FindAsync(id, cancellationToken)).SetEnabled(enabled);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var menu = await FindAsync(id, cancellationToken);
        menu.EnsureRemovable();
        if ((await menus.GetAllAsync(cancellationToken)).Any(item => item.ParentId == id)) throw Error("Access.MenuHasChildren");
        menu.MarkDeleted();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
    private async Task<Menu> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await menus.GetByIdAsync(id, cancellationToken) ?? throw Error("Access.MenuNotFound", ErrorCodes.NotFound);
    private static BusinessException Error(string key, int code = ErrorCodes.ValidationFailed) => new(code, key);
}
