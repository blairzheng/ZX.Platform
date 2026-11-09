using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Authentication.Dtos;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Application.Security;
using ZX.Platform.Identity.Application.Users;
using ZX.Platform.Identity.Application.Users.Dtos;
using ZX.Platform.Identity.Domain.Entities;
using ZX.Platform.Identity.Infrastructure.Security;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using ZX.Platform.Identity.Application.Authorization;
using Role = ZX.Platform.Identity.Domain.Entities.Role;
using ZX.Platform.Identity.Application.Menus;
using ZX.Platform.Identity.Infrastructure.Persistence;

namespace ZX.Platform.Identity.AuthChecks;

internal static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task ErrorAsync(Func<Task> action, int expectedCode)
    {
        try { await action(); }
        catch (BusinessException exception) when (exception.Code == expectedCode) { return; }
        throw new InvalidOperationException($"Expected business error {expectedCode}.");
    }
}

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan duration) => Now += duration;
}

internal sealed class TestPasswords : IPasswordHasher
{
    public string Hash(string password) => "test-hash:" + password;
    public bool Verify(string password, string passwordHash) => Hash(password) == passwordHash;
}

internal sealed class TestUsers : IUserRepository
{
    public List<User> Items { get; } = [];
    public Task<User?> GetByIdAsync(Guid id, CancellationToken token) =>
        Task.FromResult(Items.FirstOrDefault(user => user.Id == id && !user.IsDeleted));
    public Task<User?> GetByIdIncludeDeletedAsync(Guid id, CancellationToken token) =>
        Task.FromResult(Items.FirstOrDefault(user => user.Id == id));
    public Task<User?> GetByNormalizedAccountAsync(string account, CancellationToken token) =>
        Task.FromResult(Items.FirstOrDefault(user => user.NormalizedAccount == account));
    public void Add(User user)
    {
        // 模拟数据库唯一约束：账号、工号和目录身份都不能重复。
        if (Items.Any(item => item.NormalizedAccount == user.NormalizedAccount ||
                              (user.EmployeeNo is not null && item.EmployeeNo == user.EmployeeNo) ||
                              (user.LdapSubject is not null && item.LdapProvider == user.LdapProvider &&
                               item.LdapSubject == user.LdapSubject)))
            throw new UniqueConstraintException("duplicate", new InvalidOperationException("duplicate"));
        Items.Add(user);
    }
    public void Detach(User user) => Items.Remove(user);
    public Task<User?> GetByEmployeeNoAsync(string employeeNo, CancellationToken token) =>
        Task.FromResult(Items.FirstOrDefault(user => user.EmployeeNo == employeeNo));
    public Task<User?> GetByLdapIdentityAsync(string provider, string subject, CancellationToken token) =>
        Task.FromResult(Items.FirstOrDefault(user => user.LdapProvider == provider && user.LdapSubject == subject));
    public Task<List<User>> GetUsersAsync(List<Guid> ids, CancellationToken token) =>
        Task.FromResult(Items.Where(user => ids.Contains(user.Id) && !user.IsDeleted).ToList());
    public Task<(List<User>, int count)> GetPageListAsync(UserQueryDto query, CancellationToken token) =>
        Task.FromResult((Items.Where(user => !user.IsDeleted).ToList(), Items.Count(user => !user.IsDeleted)));
}

internal sealed class TestUnitOfWork : IUnitOfWork
{
    public Func<Task>? OnSave { get; set; }
    public async Task<int> SaveChangesAsync(CancellationToken token)
    {
        if (OnSave is not null) await OnSave();
        token.ThrowIfCancellationRequested();
        return 1;
    }
}

internal sealed class TestRoles(TestUsers users) : IRoleRepository
{
    public List<Role> Items { get; } = [];
    public List<UserRole> UserRoles { get; } = [];
    public List<RolePermission> RolePermissions { get; } = [];
    public void Add(Role role) => Items.Add(role);
    public Task<Role?> GetByIdAsync(Guid id, CancellationToken token) => Task.FromResult(Items.FirstOrDefault(role => role.Id == id && !role.IsDeleted));
    public Task<Role?> GetByCodeAsync(string code, CancellationToken token) => Task.FromResult(Items.FirstOrDefault(role => role.Code == code));
    public Task<List<Role>> GetAllAsync(CancellationToken token) => Task.FromResult(Items.Where(role => !role.IsDeleted).ToList());
    public Task<List<Role>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken token) => Task.FromResult(Items.Where(role => ids.Contains(role.Id) && !role.IsDeleted).ToList());
    public Task<List<RolePermission>> GetPermissionsAsync(IEnumerable<Guid> roleIds, CancellationToken token) => Task.FromResult(RolePermissions.Where(item => roleIds.Contains(item.RoleId)).ToList());
    public void AddPermission(RolePermission permission) => RolePermissions.Add(permission);
    public void RemovePermission(RolePermission permission) => RolePermissions.Remove(permission);
    public Task<List<UserRole>> GetUserRolesAsync(Guid userId, CancellationToken token) => Task.FromResult(UserRoles.Where(item => item.UserId == userId).ToList());
    public void AddUserRole(UserRole relation) => UserRoles.Add(relation);
    public void RemoveUserRole(UserRole relation) => UserRoles.Remove(relation);
    public Task<List<Guid>> GetAdministratorUserIdsAsync(IEnumerable<Guid> ids, CancellationToken token) => Task.FromResult(
        UserRoles.Where(item => ids.Contains(item.UserId) && Items.Any(role => role.Id == item.RoleId && role.IsBuiltIn && role.IsEnabled && !role.IsDeleted)).Select(item => item.UserId).ToList());
    public Task<AuthorizationSnapshot> GetAuthorizationAsync(Guid userId, CancellationToken token)
    {
        if (!users.Items.Any(user => user.Id == userId && user.HasBusinessAccess))
            return Task.FromResult(new AuthorizationSnapshot(false, [], []));
        var roles = Items.Where(role => role.IsEnabled && !role.IsDeleted && UserRoles.Any(item => item.UserId == userId && item.RoleId == role.Id)).ToList();
        var administrator = roles.Any(role => role.IsBuiltIn && role.Code == Role.AdministratorCode);
        return Task.FromResult(new AuthorizationSnapshot(administrator, roles.Select(role => role.Code).ToArray(),
            administrator ? IdentityPermissions.All.ToArray() : RolePermissions.Where(item => roles.Any(role => role.Id == item.RoleId)).Select(item => item.PermissionCode).Distinct().ToArray()));
    }
}

internal sealed class TestMenus : IMenuRepository
{
    public List<Menu> Items { get; } = [];
    public void Add(Menu menu) => Items.Add(menu);
    public Task<List<Menu>> GetAllAsync(CancellationToken token) => Task.FromResult(Items.Where(menu => !menu.IsDeleted).ToList());
    public Task<Menu?> GetByIdAsync(Guid id, CancellationToken token) => Task.FromResult(Items.FirstOrDefault(menu => menu.Id == id && !menu.IsDeleted));
    public Task<bool> CodeExistsAsync(string code, CancellationToken token) => Task.FromResult(Items.Any(menu => menu.Code == code));
}

internal sealed class TestSbus : ISbuRepository
{
    public List<Sbu> Items { get; } = [];
    public Task<Sbu?> GetByIdAsync(Guid id, CancellationToken token) =>
        Task.FromResult(Items.FirstOrDefault(item => item.Id == id && !item.IsDeleted));
    public Task<Sbu?> GetByIdIncludeDeletedAsync(Guid id, CancellationToken token) =>
        Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
    public Task<Sbu?> GetByCodeAsync(string code, CancellationToken token) =>
        Task.FromResult(Items.FirstOrDefault(item => item.Code == code));
    public Task<List<Sbu>> GetAllAsync(CancellationToken token) =>
        Task.FromResult(Items.Where(item => !item.IsDeleted).ToList());
    public Task<List<Sbu>> GetAllEnabledSbusAsync(CancellationToken token) =>
        Task.FromResult(Items.Where(item => !item.IsDeleted && item.IsEnabled).ToList());
    public Task<List<Sbu>> GetEnabledSbusByIdsAsync(List<Guid> ids, CancellationToken token) =>
        Task.FromResult(Items.Where(item => ids.Contains(item.Id) && !item.IsDeleted && item.IsEnabled).ToList());
    public void Add(Sbu sbu) => Items.Add(sbu);
}

/// <summary>
/// 替代目录适配器：支持错误密码、禁用账号、依赖故障和多条结果。
/// </summary>
internal sealed class TestLdap : ILdapAuthenticator
{
    public sealed record Entry(string EmployeeNo, string Password, string Account, string DisplayName,
        string Department, string Provider, string? Subject, string Status);

    public List<Entry> Entries { get; } = [];
    public bool IsEnabled { get; set; } = true;
    public bool Unavailable { get; set; }
    public bool Ambiguous { get; set; }
    public int AuthenticateCalls { get; private set; }

    public Task<LdapDirectoryUser> AuthenticateAsync(
        string employeeNo, string password, CancellationToken cancellationToken)
    {
        AuthenticateCalls++;
        if (!IsEnabled) throw new LdapUnavailableException("LdapDisabled");
        if (Unavailable) throw new LdapUnavailableException("LdapUnavailable");
        if (Ambiguous) throw new LdapAmbiguousResultException("多条目录记录。");
        var entry = Entries.FirstOrDefault(item => item.EmployeeNo == employeeNo);
        if (entry is null || entry.Password != password)
            throw new LdapInvalidCredentialsException("凭据无效。");
        return Task.FromResult(ToUser(entry));
    }

    private static LdapDirectoryUser ToUser(Entry entry) => new(
        entry.EmployeeNo, entry.Account, entry.DisplayName, entry.Department,
        entry.Provider, entry.Subject, entry.Status);
}

/// <summary>
/// 替代审批审计仓储，行为与数据库版本一致：只追加，按目标账号倒序读取。
/// </summary>
internal sealed class TestApprovalAudits : IUserApprovalAuditRepository
{
    public List<UserApprovalAudit> Items { get; } = [];

    public void Add(UserApprovalAudit audit) => Items.Add(audit);

    public Task<List<UserApprovalAudit>> GetByTargetUserIdAsync(
        Guid targetUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Items
            .Where(audit => audit.TargetUserId == targetUserId)
            .OrderByDescending(audit => audit.CreatedAtUtc)
            .ToList());
}

/// <summary>
/// 替代工号限频，行为与 Redis 版本一致：超限抛 429。
/// </summary>
internal sealed class TestLdapGuard : ILdapLoginGuard
{
    public Dictionary<string, int> Failures { get; } = [];
    public int Limit { get; set; } = 3;

    public Task EnsureAllowedAsync(string employeeNo, CancellationToken cancellationToken)
    {
        if (Failures.TryGetValue(employeeNo, out var count) && count >= Limit)
            throw new BusinessException(ErrorCodes.TooManyRequests, "Auth.LdapTooManyAttempts");
        return Task.CompletedTask;
    }

    public Task RecordFailureAsync(string employeeNo, CancellationToken cancellationToken)
    {
        Failures[employeeNo] = Failures.TryGetValue(employeeNo, out var count) ? count + 1 : 1;
        return Task.CompletedTask;
    }

    public Task ResetAsync(string employeeNo, CancellationToken cancellationToken)
    {
        Failures.Remove(employeeNo);
        return Task.CompletedTask;
    }
}

internal sealed class TestUserSbus(TestUsers users) : IUserSbuRepository{
    public List<UserSbu> Items { get; } = [];
    public List<Sbu> Sbus { get; } = [];
    public Task<List<UserSbu>> GetByUserIdAsync(Guid id, CancellationToken token) =>
        Task.FromResult(Items.Where(item => item.UserId == id).ToList());
    public Task<Sbu?> GetEnabledSbuForUserAsync(Guid userId, Guid sbuId, CancellationToken token) =>
        Task.FromResult(Sbus.FirstOrDefault(sbu => sbu.Id == sbuId && !sbu.IsDeleted && sbu.IsEnabled &&
            users.Items.Any(user => user.Id == userId && user.HasBusinessAccess) &&
            Items.Any(item => item.UserId == userId && item.SbuId == sbu.Id)));
    public Task<List<UserSbu>> GetByUserIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken token) =>
        Task.FromResult(Items.Where(item => ids.Contains(item.UserId)).ToList());
    public void Add(UserSbu relation) => Items.Add(relation);
    public void Remove(UserSbu relation) => Items.Remove(relation);
}

internal sealed class Fixture : IDisposable
{
    public TestClock Clock { get; } = new();
    public AuthOptions Options { get; } = new();
    public JwtOptions Jwt { get; } = new() { SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) };
    public JwtAccessTokenIssuer AccessTokens { get; }
    public TestPasswords Passwords { get; } = new();
    public TestUsers Users { get; } = new();
    public TestRoles Roles { get; }
    public TestMenus Menus { get; } = new();
    public TestSbus Sbus { get; } = new();
    public TestUserSbus UserSbus { get; }
    public TestLdap Ldap { get; } = new();
    public TestLdapGuard LdapGuard { get; } = new();
    public RedisPermissionCache PermissionCache { get; }
    public TestUnitOfWork Unit { get; } = new();
    public RedisSessionOptions RedisOptions { get; } = new() { KeyPrefix = $"zx:identity:checks:{Guid.NewGuid():N}:" };
    public TestApprovalAudits ApprovalAudits { get; } = new();
    public RedisAuthSessionStore Sessions { get; }
    public AuthService Auth { get; }
    public UserService UserService { get; }
    public User User { get; }

    public Fixture()
    {
        AccessTokens = new(Jwt);
        Sessions = new(TestRedis.Connection, Options, RedisOptions, AccessTokens, Clock);
        Roles = new(Users);
        UserSbus = new(Users);
        PermissionCache = new(TestRedis.Connection, RedisOptions);
        DefaultMenus.AddMissing(Menus.Items, Menus.Add);
        Auth = new(Users, Unit, Passwords, Sessions, Options, Clock, new PermissionService(Roles, PermissionCache, Users),
            new MenuService(Menus, Unit), UserSbus, Sbus, Ldap, LdapGuard);
        UserService = new(Users, Unit, Passwords, Sbus, UserSbus, Sessions, PermissionCache, Roles,
            new PermissionService(Roles, PermissionCache, Users), ApprovalAudits, Clock);
        User = User.CreateLocal("ADMIN", "Administrator", Passwords.Hash("Example123!"));
        Users.Add(User);
        var administrator = Role.CreateAdministrator();
        Roles.Add(administrator);
        Roles.AddUserRole(UserRole.Create(User.Id, administrator.Id));
    }

    public Task<AuthTokens> LoginAsync(string password = "Example123!", bool replaceExisting = false) =>
        Auth.LoginAsync(new LoginDto { Account = " admin ", Password = password, ReplaceExistingSession = replaceExisting }, default);

    public async Task<ClaimsPrincipal?> FindAccessAsync(string token, CancellationToken cancellationToken, IAuthSessionStore? store = null)
    {
        // 使用标准 JWT 验证，仅替换测试时钟，避免等待真实 15 分钟。
        var validation = Jwt.CreateValidationParameters();
        validation.LifetimeValidator = (notBefore, expires, _, _) =>
            notBefore <= Clock.Now.UtcDateTime && expires > Clock.Now.UtcDateTime;
        ClaimsPrincipal user;
        try
        {
            user = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, validation, out _);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException) { return null; }
        if (!Guid.TryParse(user.FindFirstValue("sub"), out var userId) ||
            !Guid.TryParse(user.FindFirstValue("sid"), out var sessionId) ||
            user.FindFirstValue("jti") is not string tokenId) return null;
        return await (store ?? Sessions).IsAccessTokenActiveAsync(userId, sessionId, tokenId, cancellationToken) ? user : null;
    }

    public void Dispose()
    {
        // 只清理本次检查的随机前缀，不清库、不接触业务会话。
        var database = TestRedis.Connection.GetDatabase();
        foreach (var endpoint in TestRedis.Connection.GetEndPoints())
            foreach (var key in TestRedis.Connection.GetServer(endpoint).Keys(database.Database, RedisOptions.KeyPrefix + "*"))
                database.KeyDelete(key);
    }
}

internal static class TestRedis
{
    public static string ConnectionString { get; } = Environment.GetEnvironmentVariable("IDENTITY_TEST_REDIS") ?? "127.0.0.1:6379";
    public static ConnectionMultiplexer Connection { get; } = Connect();

    public static ConnectionMultiplexer Connect()
    {
        var configuration = ConfigurationOptions.Parse(ConnectionString);
        configuration.ConnectTimeout = 1000;
        configuration.AsyncTimeout = 1000;
        configuration.ConnectRetry = 0;
        configuration.BacklogPolicy = BacklogPolicy.FailFast;
        return ConnectionMultiplexer.Connect(configuration);
    }
}
