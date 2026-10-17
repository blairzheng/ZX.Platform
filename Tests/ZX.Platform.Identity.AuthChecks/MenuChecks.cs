using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Menus;
using ZX.Platform.Identity.Application.Menus.Dtos;
using ZX.Platform.Identity.Domain.Entities;
using ZX.Platform.Identity.Domain.Exceptions;
using ZX.Platform.Identity.Infrastructure.Persistence;

namespace ZX.Platform.Identity.AuthChecks;

internal static class MenuChecks
{
    public static async Task RunAsync()
    {
        var options = new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseSqlServer("Server=unused;Database=ModelCheck;Integrated Security=true;TrustServerCertificate=true").Options;
        await using (var db = new PlatformIdentityDbContext(options))
        {
            Check.That(!db.Database.HasPendingModelChanges(), "Menu mappings must match the migration snapshot.");
            var sql = db.GetService<IMigrator>().GenerateScript("20261016080000_AddRolesAndPermissions", "20261016090000_AddMenus");
            Check.That(sql.Contains("CREATE TABLE [ZX_IDENTITY_MENU]") && sql.Contains("[KIND] = 'page' AND [IS_DELETED] = 0"),
                "Menu migration must create the table and filtered unique indexes.");
        }
        var repository = new TestMenus();
        DefaultMenus.AddMissing(repository.Items, repository.Add);
        var initialCount = repository.Items.Count;
        DefaultMenus.AddMissing(repository.Items, repository.Add);
        Check.That(repository.Items.Count == initialCount, "Menu bootstrap must be idempotent.");
        var service = new MenuService(repository, new TestUnitOfWork());
        var read = new AuthorizationSnapshot(false, [], [IdentityPermissions.UserRead]);
        var visible = await service.GetCurrentAsync(read, default);
        Check.That(visible.Single().Children.Single().Code == "users", "Only pages granted by read permission must be returned.");
        Check.That((await service.GetCurrentAsync(new(false, [], []), default)).Count == 0, "Empty directories must be pruned.");
        var tree = await service.GetPermissionTreeAsync(default);
        var declared = Flatten(tree).Where(node => node.Kind == Menu.Action).Select(node => node.PermissionCode).ToHashSet();
        Check.That(IdentityPermissions.All.All(declared.Contains), "All declared permissions must remain assignable.");
        var builtIn = repository.Items.Single(menu => menu.Code == "menus");
        try { await service.DeleteAsync(builtIn.Id, default); throw new InvalidOperationException("Built-in menu was deleted."); }
        catch (DomainException) { }
        var directoryId = await service.CreateAsync(new() { Code = "custom", Kind = Menu.Directory, Title = "Custom" }, default);
        var pageId = await service.CreateAsync(new() { Code = "customPage", ParentId = directoryId, Kind = Menu.Page,
            Title = "Custom page", Path = "/custom/users", Component = "SystemUser", PermissionCode = IdentityPermissions.UserRead }, default);
        Check.That((await service.GetCurrentAsync(read, default)).Count == 2, "Custom pages must follow their read permission.");
        await service.UpdateAsync(new() { Id = pageId, ParentId = directoryId, Kind = Menu.Page,
            Title = "Hidden page", Path = "/custom/users", Component = "SystemUser",
            PermissionCode = IdentityPermissions.UserRead, ShowInMenu = false, ShowInTags = false }, default);
        var hiddenPage = Flatten(await service.GetCurrentAsync(read, default)).Single(node => node.Id == pageId);
        Check.That(!hiddenPage.ShowInMenu && !hiddenPage.ShowInTags && hiddenPage.KeepAlive && !hiddenPage.Affix,
            "Hiding navigation or tags must retain the authorized page route and its display settings.");
        await Check.ErrorAsync(() => service.UpdateAsync(new() { Id = pageId, ParentId = directoryId, Kind = Menu.Page,
            Title = "Invalid pin", Path = "/custom/users", Component = "SystemUser",
            PermissionCode = IdentityPermissions.UserRead, ShowInTags = false, Affix = true }, default), ErrorCodes.ValidationFailed);
        await service.SetEnabledAsync(directoryId, false, default);
        Check.That((await service.GetCurrentAsync(read, default)).Count == 1, "A disabled ancestor must hide descendants.");
        await Check.ErrorAsync(() => service.DeleteAsync(directoryId, default), ErrorCodes.ValidationFailed);
        await Check.ErrorAsync(() => service.UpdateAsync(new() { Id = directoryId, ParentId = directoryId, Kind = Menu.Directory, Title = "Cycle" }, default), ErrorCodes.ValidationFailed);
        await Check.ErrorAsync(() => service.CreateAsync(new() { Code = "badRoute", Kind = Menu.Page, Title = "Reserved",
            Path = "/overview", Component = "SystemUser", PermissionCode = IdentityPermissions.UserRead }, default), ErrorCodes.ValidationFailed);
        await Check.ErrorAsync(() => service.CreateAsync(new() { Code = "caseRoute", Kind = Menu.Page, Title = "Duplicate",
            Path = "/SYSTEM/USERS", Component = "SystemUser", PermissionCode = IdentityPermissions.UserRead }, default), ErrorCodes.Conflict);
        await service.DeleteAsync(pageId, default);
        await service.DeleteAsync(directoryId, default);
        var stale = Menu.Create("oldAction", Menu.Action, builtIn.Id, "Old", "", "", "", "", "identity.removed.action", 999, false);
        repository.Add(stale);
        Check.That(Flatten(await service.GetTreeAsync(default)).Any(node => node.Code == stale.Code), "Management must retain stale nodes for cleanup.");
        Check.That(!Flatten(await service.GetPermissionTreeAsync(default)).Any(node => node.Code == stale.Code), "Removed permissions must not remain assignable.");
        repository.Items.RemoveAll(menu => menu.Kind == Menu.Action && menu.PermissionCode == IdentityPermissions.UserRestore);
        var stalePage = Menu.Create("oldPage", Menu.Page, null, "Old page", "", "", "/old", "SystemUser", "identity.removed.read", 999, true);
        repository.Add(stalePage);
        repository.Add(Menu.Create("stillValid", Menu.Action, stalePage.Id, "Restore", "", "", "", "", IdentityPermissions.UserRestore, 1, false));
        var cleaned = await service.GetPermissionTreeAsync(default);
        Check.That(!Flatten(cleaned).Any(node => node.Code == stalePage.Code) &&
            cleaned.Single(node => node.Code == "__unassigned").Children.Any(node => node.PermissionCode == IdentityPermissions.UserRestore),
            "A removed page must not strand its still-valid action permissions.");
        Console.WriteLine("Menu migration, bootstrap, visibility, hierarchy and built-in protection passed.");
    }

    private static IEnumerable<MenuDto> Flatten(IEnumerable<MenuDto> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));
}\n
