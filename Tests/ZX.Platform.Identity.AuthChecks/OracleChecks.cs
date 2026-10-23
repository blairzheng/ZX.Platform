using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Users.Dtos;
using ZX.Platform.Identity.Domain.Entities;
using ZX.Platform.Identity.Infrastructure.Persistence;
using ZX.Platform.Identity.Infrastructure.Persistence.Interceptors;
using ZX.Platform.Identity.Infrastructure.Persistence.Repositories;

namespace ZX.Platform.Identity.AuthChecks;

internal static class OracleChecks
{
    public static async Task RunAsync()
    {
        // 显式提供测试库连接才执行；不建表、不提交测试数据、不创建管理员。
        var connection = Environment.GetEnvironmentVariable("IDENTITY_TEST_ORACLE");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var options = new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseOracle(connection)
            .AddInterceptors(new AuditableEntitySaveChangesInterceptor(TimeProvider.System)).Options;
        await using var db = new PlatformIdentityDbContext(options);
        Check.That(!(await db.Database.GetPendingMigrationsAsync()).Any(), "Apply Oracle migrations before integration checks.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var account = suffix[..8];
        var user = User.Create(account, "Oracle 中文校验", "integration-check-never-used-for-login");
        var factory = Factory.Create("check-" + suffix, "Oracle 测试厂区");
        var role = Role.Create("CHECK_" + suffix, "Oracle 测试角色");
        db.AddRange(user, factory, role);
        var directory = Menu.Create("check-dir-" + suffix, Menu.Directory, null, "测试目录", "", "", "", "", "", 0, false);
        var secondDirectory = Menu.Create("check-dir2-" + suffix, Menu.Directory, null, "第二目录", "", "", "", "", "", 0, false);
        var path = "/check/" + suffix;
        var page = Page("page", path);
        var action = Action("action");
        db.AddRange(directory, secondDirectory, page, action);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loadedUser = await db.Users.SingleAsync(item => item.Id == user.Id);
        Check.That(loadedUser.DisplayName == "Oracle 中文校验" && loadedUser.IsEnabled &&
            loadedUser.CreatedAtUtc.Offset == TimeSpan.Zero && loadedUser.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1),
            "Oracle must round-trip Guid, Unicode, Boolean and UTC audit timestamps.");
        var loadedDirectory = await db.Menus.SingleAsync(item => item.Id == directory.Id);
        Check.That(loadedDirectory.TitleEn == "" && loadedDirectory.Icon == "" && loadedDirectory.Path == "" &&
            loadedDirectory.Component == "" && loadedDirectory.PermissionCode == "",
            "Oracle NULL optional menu text must read as empty strings.");
        Check.That((await db.Roles.SingleAsync(item => item.Id == role.Id)).Description == "",
            "Oracle NULL role description must read as an empty string.");

        var repository = new UserRepository(db);
        var (items, count) = await repository.GetPageListAsync(new UserQueryDto
        {
            Keyword = account, IsEnabled = true, Page = 1, PageSize = 10,
            CreatedFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1), CreatedToUtc = DateTimeOffset.UtcNow.AddMinutes(1)
        }, default);
        Check.That(count == 1 && items.Single().Id == user.Id, "Oracle must translate text, Boolean, UTC range and pagination queries.");
        Check.That((await repository.GetUsersAsync([user.Id, Guid.NewGuid()], default)).Single().Id == user.Id,
            "Oracle must translate Guid collection membership.");

        await DuplicateAsync(User.Create(account.ToUpperInvariant(), "重复账号", "test-only"));
        await DuplicateAsync(Page("duplicate-page", path));
        await DuplicateAsync(Action("duplicate-action"));

        var loadedPage = await db.Menus.SingleAsync(item => item.Id == page.Id);
        loadedPage.MarkDeleted();
        var loadedAction = await db.Menus.SingleAsync(item => item.Id == action.Id);
        loadedAction.MarkDeleted();
        loadedUser.MarkDeleted();
        await db.SaveChangesAsync();
        Check.That(!await db.Menus.AnyAsync(item => item.Id == page.Id) &&
            !await db.Users.AnyAsync(item => item.Id == user.Id), "Oracle must apply soft-delete filters.");
        await DuplicateAsync(User.Create(account, "删除后仍不可复用账号", "test-only"));
        db.AddRange(Page("reused-page", path), Action("reused-action"));
        await db.SaveChangesAsync();
        loadedPage.Restore();
        try
        {
            await db.SaveChangesAsync();
            throw new InvalidOperationException("Restoring a duplicate active page path must fail.");
        }
        catch (UniqueConstraintException) { }

        await transaction.RollbackAsync();
        Console.WriteLine("Oracle integration checks passed; all test writes rolled back.");

        Menu Page(string code, string route) => Menu.Create("check-" + code + "-" + suffix, Menu.Page,
            directory.Id, "测试页面", "", "", route, "SystemUser", "check.read." + suffix, 0, false);
        Menu Action(string code) => Menu.Create("check-" + code + "-" + suffix, Menu.Action,
            page.Id, "测试操作", "", "", "", "", "check.action." + suffix, 0, false);

        async Task DuplicateAsync(object entity)
        {
            db.Add(entity);
            try
            {
                await db.SaveChangesAsync();
                throw new InvalidOperationException("Oracle duplicate data must raise UniqueConstraintException.");
            }
            catch (UniqueConstraintException) { }
            finally { db.Entry(entity).State = EntityState.Detached; }
        }
    }
}
