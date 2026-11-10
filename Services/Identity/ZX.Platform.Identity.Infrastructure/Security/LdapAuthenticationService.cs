using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using ZX.Platform.Identity.Application.Authentication;

namespace ZX.Platform.Identity.Infrastructure.Security;

/// <summary>
/// 通过 System.DirectoryServices.Protocols 访问 Active Directory。
/// 员工先用 域名\工号 和 OA 密码 bind，再在同一连接读取自己的目录身份。
/// </summary>
public sealed class LdapAuthenticationService : ILdapAuthenticator
{
    private const int AccountDisabledFlag = 0x2;

    private readonly LdapOptions _options;
    private readonly ILogger<LdapAuthenticationService> _logger;
    private readonly LdapOperationRunner _runner;

    public LdapAuthenticationService(LdapOptions options, ILogger<LdapAuthenticationService> logger)
    {
        _options = options;
        _logger = logger;
        _runner = new LdapOperationRunner(options.MaxConcurrentOperations, options.TotalTimeoutSeconds);
    }

    public bool IsEnabled => _options.Enabled;

    public Task<LdapDirectoryUser> AuthenticateAsync(
        string employeeNo, string password, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            throw new LdapUnavailableException("LdapDisabled");

        employeeNo = employeeNo.Trim();
        if (string.IsNullOrEmpty(employeeNo) || string.IsNullOrEmpty(password) ||
            employeeNo.Any(character => char.IsControl(character) || character is '\\' or '/' or '@'))
        {
            // 空密码在发往目录前拒绝，防止误触发匿名或未认证 bind。
            return Task.FromException<LdapDirectoryUser>(
                new LdapInvalidCredentialsException("工号格式无效或密码为空。"));
        }

        return _runner.RunAsync(operation =>
        {
            LdapUnavailableException? lastFailure = null;
            foreach (var server in _options.Servers)
            {
                // 切换节点也消耗总预算：预算耗尽后不再尝试备用节点。
                operation.ThrowIfExpired();
                try
                {
                    return AuthenticateOnServer(server, employeeNo, password, operation);
                }
                catch (LdapInvalidCredentialsException)
                {
                    // 目录明确拒绝凭据后停止，不遍历其他节点重复尝试密码，避免触发 AD 锁定。
                    throw;
                }
                catch (LdapAmbiguousResultException)
                {
                    throw;
                }
                catch (LdapUnavailableException)
                {
                    // 总预算耗尽或身份属性缺失，换节点也无法修正，直接失败。
                    throw;
                }
                catch (Exception exception) when (IsDependencyFailure(exception))
                {
                    lastFailure = new LdapUnavailableException("LdapUnavailable", exception);
                    _logger.LogWarning(exception,
                        "LDAP 节点不可用，尝试下一个节点。Host={Host}, Port={Port}", server.Host, server.Port);
                }
            }

            throw lastFailure ?? new LdapUnavailableException("LdapUnavailable");
        }, cancellationToken);
    }

    private LdapDirectoryUser AuthenticateOnServer(
        LdapServerOptions server, string employeeNo, string password, LdapOperation operation)
    {
        using var connection = CreateConnection(server, operation);
        StartTlsIfNeeded(connection, server, operation);
        operation.ThrowIfExpired();
        try
        {
            // 固定使用 AD 的 NetBIOS 域名格式，不尝试多种账号格式或重试错误密码。
            connection.Bind(new NetworkCredential($"{_options.Domain}\\{employeeNo}", password));
        }
        catch (LdapException exception) when (exception.ErrorCode == LdapSearchFailure.InvalidCredentials)
        {
            throw new LdapInvalidCredentialsException("目录拒绝了该工号或密码。");
        }

        // 密码验证通过后，使用员工自身的连接读取身份；读不到时不能冒用输入工号建档。
        var entry = SearchUser(connection, employeeNo, operation)
            ?? throw new LdapUnavailableException("LdapAuthenticatedUserNotFound");
        var identity = ToIdentity(entry, employeeNo);

        _logger.LogInformation(
            "OA 用户目录认证通过。EmployeeNo={EmployeeNo}, Provider={Provider}",
            employeeNo, _options.Provider);
        return identity;
    }

    private LdapConnection CreateConnection(LdapServerOptions server, LdapOperation operation)
    {
        var connection = new LdapConnection(new LdapDirectoryIdentifier(server.Host, server.Port))
        {
            AuthType = AuthType.Basic,
            // 单次连接的超时同时受总预算约束，避免一个节点吃掉整段预算。
            Timeout = operation.Slice(_options.ConnectTimeoutSeconds)
        };
        connection.SessionOptions.ProtocolVersion = 3;

        // 不安装 VerifyServerCertificate 回调：始终使用平台默认的严格证书校验。
        // 证书问题应通过受信任 CA 与正确的服务器名称解决，不能放行任意证书。
        if (server.Encryption == LdapEncryption.Ldaps)
            connection.SessionOptions.SecureSocketLayer = true;

        return connection;
    }

    private static void StartTlsIfNeeded(
        LdapConnection connection, LdapServerOptions server, LdapOperation operation)
    {
        if (server.Encryption != LdapEncryption.StartTls)
            return;

        operation.ThrowIfExpired();
        connection.SessionOptions.StartTransportLayerSecurity([]);
    }

    private SearchResultEntry? SearchUser(
        LdapConnection connection, string employeeNo, LdapOperation operation)
    {
        // 输入工号按配置的查询属性匹配；默认跟随工号属性，避免改了工号属性却仍按登录名查询。
        var lookupAttribute = _options.ResolveEmployeeLookupAttribute();
        // 过滤器值按 RFC 4515 转义，不接受客户端提交任意过滤器。
        var filter = $"(&(objectCategory=Person)(objectClass=user)(sAMAccountName={EscapeFilterValue(employeeNo)})({lookupAttribute}={EscapeFilterValue(employeeNo)}))";
        var budget = operation.Slice(_options.OperationTimeoutSeconds);
        var request = new SearchRequest(
            _options.BaseDn,
            filter,
            SearchScope.Subtree,
            _options.UserNameAttribute,
            _options.EmployeeNumberAttribute,
            _options.SubjectAttribute,
            _options.DisplayNameAttribute,
            _options.DepartmentAttribute,
            _options.StatusAttribute)
        {
            TimeLimit = budget,
            // 只需要判断唯一性；超过 1 条时由目录返回 sizeLimitExceeded。
            SizeLimit = 2
        };

        SearchResponse response;
        try
        {
            response = (SearchResponse)connection.SendRequest(request, budget);
        }
        catch (LdapException exception)
            when (LdapSearchFailure.IsAmbiguous(exception.ErrorCode, null))
        {
            throw new LdapAmbiguousResultException($"工号 {employeeNo} 在目录中匹配到多条记录。");
        }
        catch (DirectoryOperationException exception)
            when (LdapSearchFailure.IsAmbiguous(null, (int?)exception.Response?.ResultCode))
        {
            // 搜索也可能以 DirectoryOperationException 携带 sizeLimitExceeded 结束，
            // 不能落到外层“依赖故障”里，否则会去试备用节点并丢失歧义分类。
            throw new LdapAmbiguousResultException($"工号 {employeeNo} 在目录中匹配到多条记录。");
        }

        // 此处已经完成密码认证，后续搜索失败按依赖故障处理，不计入密码失败次数。

        if (LdapSearchFailure.IsAmbiguous(null, (int)response.ResultCode))
            throw new LdapAmbiguousResultException($"工号 {employeeNo} 在目录中匹配到多条记录。");

        if (response.Entries.Count == 0)
            return null;
        if (response.Entries.Count > 1)
            throw new LdapAmbiguousResultException($"工号 {employeeNo} 在目录中匹配到多条记录。");
        return response.Entries[0];
    }

    private LdapDirectoryUser ToIdentity(SearchResultEntry entry, string queriedEmployeeNo)
    {
        var account = FirstString(entry, _options.UserNameAttribute);
        if (string.IsNullOrWhiteSpace(account))
            throw new LdapUnavailableException("LdapAccountAttributeMissing");
        if (!string.Equals(account.Trim(), queriedEmployeeNo.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new LdapUnavailableException("LdapAccountMismatch");

        // 不用请求参数冒充目录返回值：工号属性读不到说明配置与目录不一致，
        // 继续拿输入值顶上会让错配看起来像成功。
        var employeeNo = FirstString(entry, _options.EmployeeNumberAttribute);
        if (string.IsNullOrWhiteSpace(employeeNo))
            throw new LdapUnavailableException("LdapEmployeeNoAttributeMissing");
        employeeNo = employeeNo.Trim();

        // 查询属性与工号属性不是同一个时，核对返回的工号确实是这次查询的对象。
        if (!string.Equals(employeeNo, queriedEmployeeNo.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new LdapUnavailableException("LdapEmployeeNoMismatch");

        var displayName = FirstString(entry, _options.DisplayNameAttribute) ?? account.Trim();
        var department = FirstString(entry, _options.DepartmentAttribute);

        return new LdapDirectoryUser(
            employeeNo,
            account.Trim(),
            displayName.Trim(),
            department?.Trim(),
            _options.Provider,
            ReadSubject(entry),
            ReadStatus(entry));
    }

    /// <summary>
    /// 读取 objectGUID 并转换为规范 GUID 字符串；不能用 UTF-8 解码二进制值。
    /// </summary>
    private string? ReadSubject(SearchResultEntry entry)
    {
        if (string.IsNullOrEmpty(_options.SubjectAttribute) || !entry.Attributes.Contains(_options.SubjectAttribute))
            return null;

        var values = entry.Attributes[_options.SubjectAttribute].GetValues(typeof(byte[]));
        if (values.Length == 0 || values[0] is not byte[] bytes || bytes.Length != 16)
            return null;
        return new Guid(bytes).ToString("D");
    }

    /// <summary>
    /// 读取目录账号启用状态。没有读到可信状态时返回 Unknown，不能当作正常。
    /// </summary>
    private string ReadStatus(SearchResultEntry entry)
    {
        if (string.IsNullOrEmpty(_options.StatusAttribute) || !entry.Attributes.Contains(_options.StatusAttribute))
            return LdapDirectoryStatus.Unknown;

        var values = entry.Attributes[_options.StatusAttribute].GetValues(typeof(string));
        if (values.Length == 0 || values[0] is not string text || !int.TryParse(text, out var flags))
            return LdapDirectoryStatus.Unknown;

        return (flags & AccountDisabledFlag) != 0 ? LdapDirectoryStatus.Disabled : LdapDirectoryStatus.Enabled;
    }

    private static string? FirstString(SearchResultEntry entry, string attribute)
    {
        if (string.IsNullOrEmpty(attribute) || !entry.Attributes.Contains(attribute))
            return null;

        var values = entry.Attributes[attribute].GetValues(typeof(string));
        return values.Length == 0 ? null : values[0] as string;
    }

    private static string EscapeFilterValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append(@"\5c"); break;
                case '*': builder.Append(@"\2a"); break;
                case '(': builder.Append(@"\28"); break;
                case ')': builder.Append(@"\29"); break;
                case '\0': builder.Append(@"\00"); break;
                default: builder.Append(character); break;
            }
        }
        return builder.ToString();
    }

    private static bool IsDependencyFailure(Exception exception) =>
        exception is LdapException or IOException or TimeoutException or SocketException
            or DirectoryOperationException or ObjectDisposedException or System.Security.Authentication.AuthenticationException;
}
