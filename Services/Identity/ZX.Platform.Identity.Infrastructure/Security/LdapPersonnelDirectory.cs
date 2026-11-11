using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Personnel;

namespace ZX.Platform.Identity.Infrastructure.Security;

/// <summary>
/// 使用独立只读服务账号分页读取指定 BU 的 OA 人员。
/// 只有一个节点的全部分页完整返回后才向应用层交付；不暴露任意目录查询能力。
/// </summary>
public sealed class LdapPersonnelDirectory : IOaPersonnelDirectory
{
    private const int AccountDisabledFlag = 0x2;

    private readonly LdapOptions _directory;
    private readonly OaPersonnelSyncOptions _sync;
    private readonly ILogger<LdapPersonnelDirectory> _logger;
    private readonly LdapOperationRunner _runner;

    public LdapPersonnelDirectory(
        LdapOptions directory,
        OaPersonnelSyncOptions sync,
        ILogger<LdapPersonnelDirectory> logger)
    {
        _directory = directory;
        _sync = sync;
        _logger = logger;
        _runner = new LdapOperationRunner(1, sync.TotalTimeoutSeconds);
    }

    public string BusinessUnit => _sync.BusinessUnit?.Trim() ?? string.Empty;

    public async Task<IReadOnlyList<OaDirectoryPerson>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConfigured())
            throw Failure("OaPersonnel.NotConfigured");

        try
        {
            return await _runner.RunAsync(ReadFromAvailableServer, cancellationToken).ConfigureAwait(false);
        }
        catch (LdapUnavailableException)
        {
            throw Failure("OaPersonnel.DirectoryUnavailable");
        }
        catch (BerConversionException)
        {
            throw Failure("OaPersonnel.IncompleteRead");
        }
    }

    private IReadOnlyList<OaDirectoryPerson> ReadFromAvailableServer(LdapOperation operation)
    {
        foreach (var server in _directory.Servers)
        {
            operation.ThrowIfExpired();
            try
            {
                // 每个节点从空 cookie 开始；故障节点的部分结果绝不与备用节点拼接。
                return ReadServer(server, operation);
            }
            catch (Exception exception) when (IsDependencyFailure(exception))
            {
                // 不记录原始异常消息，避免目录诊断中夹带绑定账号或人员 DN。
                _logger.LogWarning(
                    "OA 人员目录节点读取失败。Host={Host}, Port={Port}, FailureType={FailureType}",
                    server.Host, server.Port, exception.GetType().Name);
            }
        }

        throw Failure("OaPersonnel.DirectoryUnavailable");
    }

    private IReadOnlyList<OaDirectoryPerson> ReadServer(LdapServerOptions server, LdapOperation operation)
    {
        using var connection = CreateConnection(server, operation);
        if (server.Encryption == LdapEncryption.StartTls)
        {
            operation.ThrowIfExpired();
            connection.SessionOptions.StartTransportLayerSecurity([]);
        }

        operation.ThrowIfExpired();
        connection.Timeout = operation.Slice(_directory.ConnectTimeoutSeconds);
        try
        {
            // 配置已排除空账号、空密码，不能意外执行匿名 bind。
            connection.Bind(new NetworkCredential(_sync.BindUser.Trim(), _sync.BindPassword));
        }
        catch (LdapException exception) when (exception.ErrorCode == LdapSearchFailure.InvalidCredentials)
        {
            // 错误服务凭据不切换节点重试，避免加速锁定服务账号。
            throw Failure("OaPersonnel.DirectoryUnavailable");
        }
        catch (DirectoryOperationException exception)
            when ((int?)exception.Response?.ResultCode == LdapSearchFailure.InvalidCredentials)
        {
            throw Failure("OaPersonnel.DirectoryUnavailable");
        }

        var suffix = $",{BusinessUnit}";
        var filter = $"(&(objectCategory=person)(objectClass=user)({_directory.DepartmentAttribute.Trim()}=*{EscapeFilterValue(suffix)}))";
        var attributes = new[]
        {
            _directory.UserNameAttribute.Trim(), _directory.EmployeeNumberAttribute.Trim(),
            _directory.SubjectAttribute.Trim(), _directory.DisplayNameAttribute.Trim(),
            _directory.DepartmentAttribute.Trim(), _directory.StatusAttribute.Trim(),
            "cn", "displayName", "sn", "givenName", "mail", "company", "manager"
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var request = new SearchRequest(_directory.BaseDn.Trim(), filter, SearchScope.Subtree, attributes)
        {
            SizeLimit = 0,
            Aliases = DereferenceAlias.Never
        };
        var paging = new PageResultRequestControl(_sync.PageSize) { IsCritical = true };
        request.Controls.Add(paging);
        var results = new List<OaDirectoryPerson>();

        while (true)
        {
            operation.ThrowIfExpired();
            var timeout = operation.Slice(_directory.OperationTimeoutSeconds);
            request.TimeLimit = timeout;
            SearchResponse response;
            try
            {
                response = (SearchResponse)connection.SendRequest(request, timeout);
            }
            catch (DirectoryOperationException exception)
                when (IsIncompleteResult((int?)exception.Response?.ResultCode))
            {
                throw Failure("OaPersonnel.IncompleteRead");
            }
            catch (LdapException exception) when (IsIncompleteResult(exception.ErrorCode))
            {
                throw Failure("OaPersonnel.IncompleteRead");
            }

            if (response.ResultCode != ResultCode.Success || response.References.Count > 0 ||
                response.Referral.Length > 0)
                throw Failure("OaPersonnel.IncompleteRead");

            var pageControls = response.Controls.OfType<PageResultResponseControl>().ToArray();
            if (pageControls.Length != 1)
                throw Failure("OaPersonnel.IncompleteRead");
            if (response.Entries.Count > _sync.MaxPeople - results.Count)
                throw Failure("OaPersonnel.ScopeLimit");

            foreach (SearchResultEntry entry in response.Entries)
            {
                operation.ThrowIfExpired();
                var department = FirstString(entry, _directory.DepartmentAttribute)?.Trim();
                // 防止错误服务器或属性配置返回范围外对象；不能悄悄扩大同步范围。
                if (department is null || !department.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    throw Failure("OaPersonnel.IncompleteRead");
                results.Add(ToPerson(entry, department));
            }

            var nextCookie = pageControls[0].Cookie;
            if (nextCookie.Length == 0)
            {
                operation.ThrowIfExpired();
                return results;
            }

            // cookie 是服务器的不透明游标，不要求每页值变化；数量上限和总预算限制异常分页。
            if (response.Entries.Count == 0)
                throw Failure("OaPersonnel.IncompleteRead");
            paging.Cookie = nextCookie;
        }
    }

    private LdapConnection CreateConnection(LdapServerOptions server, LdapOperation operation)
    {
        var connection = new LdapConnection(new LdapDirectoryIdentifier(server.Host.Trim(), server.Port))
        {
            AuthType = AuthType.Basic,
            AutoBind = false,
            Timeout = operation.Slice(_directory.ConnectTimeoutSeconds)
        };
        try
        {
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            connection.SessionOptions.SecureSocketLayer = server.Encryption == LdapEncryption.Ldaps;
            // 始终采用平台证书验证，不安装跳过验证的回调。
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private OaDirectoryPerson ToPerson(SearchResultEntry entry, string department)
    {
        var account = FirstString(entry, _directory.UserNameAttribute)?.Trim() ?? string.Empty;
        var employeeNo = FirstString(entry, _directory.EmployeeNumberAttribute)?.Trim() ?? string.Empty;
        var commonName = FirstString(entry, "cn")?.Trim();
        var name = RemoveIdentifierSuffix(commonName, employeeNo, account);
        if (string.IsNullOrWhiteSpace(name))
            name = FirstString(entry, "sn")?.Trim() ?? FirstString(entry, "givenName")?.Trim() ?? string.Empty;
        var displayName = FirstString(entry, "displayName")?.Trim()
            ?? FirstString(entry, _directory.DisplayNameAttribute)?.Trim() ?? name;

        return new OaDirectoryPerson(
            _directory.Provider.Trim(), ReadSubject(entry), account, employeeNo, name, displayName,
            FirstString(entry, "mail")?.Trim(), FirstString(entry, "company")?.Trim(), department,
            entry.DistinguishedName, FirstString(entry, "manager")?.Trim(), ReadStatus(entry));
    }

    private string ReadSubject(SearchResultEntry entry)
    {
        var attribute = _directory.SubjectAttribute.Trim();
        if (!entry.Attributes.Contains(attribute))
            return string.Empty;
        var values = entry.Attributes[attribute].GetValues(typeof(byte[]));
        if (values.Length != 1 || values[0] is not byte[] bytes || bytes.Length != 16)
            return string.Empty;
        var subject = new Guid(bytes);
        // 不伪造缺失身份；应用层将缺失或无效身份计入 invalidCount。
        return subject == Guid.Empty ? string.Empty : subject.ToString("D");
    }

    private string ReadStatus(SearchResultEntry entry)
    {
        var value = FirstString(entry, _directory.StatusAttribute);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var flags))
            return LdapDirectoryStatus.Unknown;
        return (flags & AccountDisabledFlag) != 0 ? LdapDirectoryStatus.Disabled : LdapDirectoryStatus.Enabled;
    }

    private bool IsConfigured()
    {
        if (!_sync.Enabled || string.IsNullOrWhiteSpace(_sync.BindUser) || string.IsNullOrEmpty(_sync.BindPassword) ||
            _sync.BindUser.Length > 512 || _sync.BindUser.Any(char.IsControl) ||
            _sync.PageSize is < 1 or > 1000 || _sync.MaxPeople is < 1 or > 50000 ||
            _sync.TotalTimeoutSeconds is < 5 or > 180 ||
            string.IsNullOrWhiteSpace(_sync.BusinessUnit) || _sync.BusinessUnit.Length > 32 ||
            _sync.BusinessUnit.Trim().Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_') ||
            string.IsNullOrWhiteSpace(_directory.Provider) || _directory.Provider.Length > 32 ||
            _directory.Provider.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(_directory.BaseDn) || _directory.BaseDn.Length > 2048 ||
            _directory.BaseDn.Any(char.IsControl) || _directory.Servers.Count == 0 ||
            _directory.ConnectTimeoutSeconds is < 1 or > 30 || _directory.OperationTimeoutSeconds is < 1 or > 60 ||
            !_directory.VerifyServerCertificate)
            return false;

        var attributes = new[]
        {
            _directory.UserNameAttribute, _directory.EmployeeNumberAttribute, _directory.SubjectAttribute,
            _directory.DisplayNameAttribute, _directory.DepartmentAttribute, _directory.StatusAttribute
        };
        if (attributes.Any(attribute => !IsAttributeName(attribute)) ||
            !string.Equals(_directory.SubjectAttribute.Trim(), "objectGUID", StringComparison.OrdinalIgnoreCase))
            return false;

        return _directory.Servers.All(server => !string.IsNullOrWhiteSpace(server.Host) &&
            server.Host.Length <= 255 && !server.Host.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)) &&
            server.Port is >= 1 and <= 65535 && LdapEncryption.IsValid(server.Encryption) &&
            (server.Encryption != LdapEncryption.Plain || _directory.AllowPlainText));
    }

    private static bool IsAttributeName(string? attribute) =>
        !string.IsNullOrWhiteSpace(attribute) && attribute.Length <= 128 &&
        char.IsAsciiLetter(attribute.Trim()[0]) &&
        attribute.Trim().All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static string? FirstString(SearchResultEntry entry, string attribute)
    {
        attribute = attribute.Trim();
        if (!entry.Attributes.Contains(attribute))
            return null;
        var values = entry.Attributes[attribute].GetValues(typeof(string));
        return values.Length == 0 ? null : values[0] as string;
    }

    private static string RemoveIdentifierSuffix(string? commonName, string employeeNo, string account)
    {
        if (string.IsNullOrWhiteSpace(commonName))
            return string.Empty;
        foreach (var identifier in new[] { employeeNo, account })
        {
            if (identifier.Length == 0)
                continue;
            foreach (var suffix in new[] { $"({identifier})", $"（{identifier}）" })
                if (commonName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return commonName[..^suffix.Length].Trim();
        }
        return commonName;
    }

    private static string EscapeFilterValue(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            escaped.Append(character switch
            {
                '\\' => @"\5c",
                '*' => @"\2a",
                '(' => @"\28",
                ')' => @"\29",
                '\0' => @"\00",
                _ => character.ToString()
            });
        }
        return escaped.ToString();
    }

    private static bool IsIncompleteResult(int? code) => code is 3 or 4 or 10 or 11 or 12;

    private static bool IsDependencyFailure(Exception exception) =>
        exception is LdapException or IOException or TimeoutException or SocketException or DirectoryOperationException
            or ObjectDisposedException or System.Security.Authentication.AuthenticationException
            or DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException;

    private static BusinessException Failure(string key) => new(ErrorCodes.ServiceUnavailable, key);
}
