using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Personnel;

namespace ZX.Platform.Identity.Infrastructure.Security;

/// <summary>
/// 固定远端目录适配器。仅传递目录认证及只读结果；不接收远端 MOM Token，不切换本机数据库。
/// </summary>
public sealed class DirectoryRelayClient(
    HttpClient http,
    DirectoryRelayOptions options,
    LdapOptions ldap,
    OaPersonnelSyncOptions sync)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int AuthenticationResponseLimit = 64 * 1024;
    private const int PersonnelResponseLimit = 64 * 1024 * 1024;

    public async Task<LdapDirectoryUser> AuthenticateAsync(
        string employeeNo, string password, CancellationToken cancellationToken)
    {
        if (!options.Enabled || !ldap.Enabled)
            throw Unavailable();
        employeeNo = employeeNo.Trim();
        if (!Fits(employeeNo, 32, true) || employeeNo.IndexOfAny(['\\', '/', '@']) >= 0 ||
            string.IsNullOrEmpty(password) || password.Length > 1024)
            throw new LdapInvalidCredentialsException("工号格式无效或密码为空。");

        var response = await SendAsync(
            DirectoryRelayProtocol.AuthenticatePath,
            new DirectoryRelayLoginDto { EmployeeNo = employeeNo, Password = password },
            AuthenticationResponseLimit, true,
            Math.Min(options.TimeoutSeconds, ldap.TotalTimeoutSeconds + 3), cancellationToken);
        LdapDirectoryUser? identity;
        try
        {
            identity = response.Deserialize<LdapDirectoryUser>(JsonOptions);
        }
        catch (JsonException)
        {
            throw Unavailable();
        }

        if (identity is null || !string.Equals(identity.EmployeeNo, employeeNo, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(identity.Account, employeeNo, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(identity.Provider, ldap.Provider, StringComparison.Ordinal) ||
            !Guid.TryParse(identity.Subject, out var subject) || subject == Guid.Empty ||
            !Fits(identity.Account, 100, true) || !Fits(identity.DisplayName, 256, true) ||
            !Fits(identity.Department, 1000) ||
            identity.Status is not (LdapDirectoryStatus.Enabled or LdapDirectoryStatus.Disabled or LdapDirectoryStatus.Unknown))
            throw Unavailable();

        return identity;
    }

    public async Task<IReadOnlyList<OaDirectoryPerson>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled || !sync.Enabled || !Fits(sync.BusinessUnit, 32, true) ||
            sync.BusinessUnit.Trim().Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_') ||
            sync.MaxPeople is < 1 or > 50000 || sync.TotalTimeoutSeconds is < 5 or > 180)
            throw PersonnelFailure("OaPersonnel.NotConfigured");

        try
        {
            var response = await SendAsync(
                DirectoryRelayProtocol.PersonnelPath, null,
                PersonnelResponseLimit, false,
                Math.Min(options.TimeoutSeconds, sync.TotalTimeoutSeconds + 5), cancellationToken);
            var result = response.Deserialize<DirectoryRelayPersonnelResult>(JsonOptions);
            if (result is null || !string.Equals(result.Provider, ldap.Provider, StringComparison.Ordinal) ||
                !string.Equals(result.BusinessUnit, sync.BusinessUnit.Trim(), StringComparison.Ordinal) ||
                result.People is null || result.People.Count > sync.MaxPeople ||
                result.People.Any(person => !IsSameSource(person)))
                throw PersonnelFailure("OaPersonnel.IncompleteRead");
            return result.People;
        }
        catch (JsonException)
        {
            throw PersonnelFailure("OaPersonnel.IncompleteRead");
        }
        catch (LdapUnavailableException)
        {
            throw PersonnelFailure("OaPersonnel.DirectoryUnavailable");
        }
    }

    private async Task<JsonElement> SendAsync(
        string path, DirectoryRelayLoginDto? login, int responseLimit, bool authentication, int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // 登录与批量读取分别沿用各自目录预算，并只留少量网络余量；客户端上限继续兜底。
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute), path));
        request.Headers.Add(DirectoryRelayProtocol.KeyHeader, options.AccessKey);
        if (login is not null)
            request.Content = JsonContent.Create(login, options: JsonOptions);
        try
        {
            // 不做自动重试；HttpClient 的 handler 必须在注册时禁用重定向，避免转发密钥和员工密码。
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new BusinessException(ErrorCodes.TooManyRequests,
                    authentication ? "Auth.LdapTooManyAttempts" : "Error.TooManyRequests",
                    new { Reason = "TooManyRequests" });

            using var body = await ReadBoundedAsync(response.Content, responseLimit, timeout.Token);
            var envelope = JsonSerializer.Deserialize<RelayEnvelope>(body.ToArray(), JsonOptions);
            if (envelope is null)
                throw Unavailable();
            if (!response.IsSuccessStatusCode || envelope.Code != 0)
            {
                var reason = ReadReason(envelope.Data);
                if (authentication && response.StatusCode == HttpStatusCode.Unauthorized &&
                    envelope.Code == ErrorCodes.Unauthorized && reason == DirectoryRelayProtocol.InvalidCredentialsReason)
                    throw new LdapInvalidCredentialsException("目录拒绝了该工号或密码。");
                if (authentication && response.StatusCode == HttpStatusCode.ServiceUnavailable &&
                    envelope.Code == ErrorCodes.ServiceUnavailable && reason == DirectoryRelayProtocol.AmbiguousReason)
                    throw new LdapAmbiguousResultException("目录身份不唯一。");
                throw Unavailable();
            }
            if (envelope.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                throw Unavailable();
            return envelope.Data;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            // 不包含响应体、请求凭据、URL 或原始传输异常消息。
            throw Unavailable();
        }
    }

    private static async Task<MemoryStream> ReadBoundedAsync(
        HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limit)
            throw Unavailable();
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        var output = new MemoryStream();
        try
        {
            var buffer = new byte[8192];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (output.Length + read > limit)
                    throw Unavailable();
                output.Write(buffer, 0, read);
            }
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    // 传输层只阻止混入其他来源。缺少 GUID、字段超长等源记录问题仍由原同步用例逐条计入 invalidCount。
    private bool IsSameSource(OaDirectoryPerson? person) =>
        person is not null && (string.IsNullOrWhiteSpace(person.Provider) ||
            string.Equals(person.Provider, ldap.Provider, StringComparison.Ordinal));

    private static bool Fits(string? value, int maxLength, bool required = false) =>
        (!required || !string.IsNullOrWhiteSpace(value)) && (value?.Trim().Length ?? 0) <= maxLength &&
        (value is null || !value.Any(char.IsControl));

    private static string? ReadReason(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty("reason", out var reason) &&
        reason.ValueKind == JsonValueKind.String ? reason.GetString() : null;

    private static LdapUnavailableException Unavailable() =>
        new(DirectoryRelayProtocol.UnavailableReason);

    private static BusinessException PersonnelFailure(string key) => new(ErrorCodes.ServiceUnavailable, key);

    private sealed class RelayEnvelope
    {
        public RelayEnvelope() { }

        public int? Code { get; init; }
        public JsonElement Data { get; init; }
    }
}

public sealed class RemoteLdapAuthenticator(DirectoryRelayClient client, LdapOptions options) : ILdapAuthenticator
{
    public bool IsEnabled => options.Enabled;

    public Task<LdapDirectoryUser> AuthenticateAsync(
        string employeeNo, string password, CancellationToken cancellationToken) =>
        client.AuthenticateAsync(employeeNo, password, cancellationToken);
}

public sealed class RemoteOaPersonnelDirectory(DirectoryRelayClient client, OaPersonnelSyncOptions options) : IOaPersonnelDirectory
{
    public string BusinessUnit => options.BusinessUnit?.Trim() ?? string.Empty;

    public Task<IReadOnlyList<OaDirectoryPerson>> ReadAsync(CancellationToken cancellationToken) =>
        client.ReadAsync(cancellationToken);
}
