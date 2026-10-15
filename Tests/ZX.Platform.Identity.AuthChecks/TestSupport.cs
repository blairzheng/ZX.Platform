using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Authentication.Dtos;
using ZX.Platform.Identity.Application.Exceptions;
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
    public void Add(User user) => Items.Add(user);
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

internal sealed class Fixture : IDisposable
{
    public TestClock Clock { get; } = new();
    public AuthOptions Options { get; } = new() { AdministratorAccounts = ["ADMIN"] };
    public JwtOptions Jwt { get; } = new() { SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) };
    public JwtAccessTokenIssuer AccessTokens { get; }
    public TestPasswords Passwords { get; } = new();
    public TestUsers Users { get; } = new();
    public TestUnitOfWork Unit { get; } = new();
    public RedisSessionOptions RedisOptions { get; } = new() { KeyPrefix = $"zx:identity:checks:{Guid.NewGuid():N}:" };
    public RedisAuthSessionStore Sessions { get; }
    public AuthService Auth { get; }
    public UserService UserService { get; }
    public User User { get; }

    public Fixture()
    {
        AccessTokens = new(Jwt);
        Sessions = new(TestRedis.Connection, Options, RedisOptions, AccessTokens, Clock);
        Auth = new(Users, Unit, Passwords, Sessions, Options, Clock);
        // 这些检查仅调用用户状态/密码用例，不使用厂区仓储。
        UserService = new(Users, Unit, Passwords, null!, null!, Sessions);
        User = User.Create("ADMIN", "Administrator", Passwords.Hash("Example123!"));
        Users.Add(User);
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
