using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Authentication;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Infrastructure.Security;

namespace ZX.Platform.Identity.Api.Controllers;

/// <summary>只执行真实目录认证和只读查询；建档、授权与会话仍由调用方的原有用例处理。</summary>
[ApiController]
[Route("api/DirectoryRelay")]
[AllowAnonymous]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class DirectoryRelayController(
    DirectoryRelayOptions relay,
    LdapAuthenticationService ldap,
    LdapPersonnelDirectory personnel,
    LdapOptions ldapOptions,
    ILdapLoginGuard guard,
    IApiMessageLocalizer messages) : ControllerBase
{
    [HttpPost("authenticate")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> AuthenticateAsync(
        [FromBody] DirectoryRelayLoginDto input, CancellationToken cancellationToken)
    {
        EnsureAccessKey();
        if (!ldap.IsEnabled)
            throw Unavailable();

        var employeeNo = input.EmployeeNo.Trim();
        if (employeeNo.Length == 0 || employeeNo.Any(character =>
                char.IsControl(character) || character is '\\' or '/' or '@'))
            throw new BusinessException(ErrorCodes.ValidationFailed, "DirectoryRelay.RequestInvalid");

        // 各开发机的个人 Redis 前缀互不影响；这里沿用服务器守卫聚合所有来源的失败次数。
        await guard.EnsureAllowedAsync(employeeNo, cancellationToken);
        LdapDirectoryUser identity;
        try
        {
            identity = await ldap.AuthenticateAsync(employeeNo, input.Password, cancellationToken);
        }
        catch (LdapInvalidCredentialsException)
        {
            await guard.RecordFailureAsync(employeeNo, cancellationToken);
            throw new BusinessException(ErrorCodes.Unauthorized, "Auth.InvalidCredentials",
                new { Reason = DirectoryRelayProtocol.InvalidCredentialsReason });
        }
        catch (LdapAmbiguousResultException)
        {
            throw new BusinessException(ErrorCodes.ServiceUnavailable, "Auth.LdapUnavailable",
                new { Reason = DirectoryRelayProtocol.AmbiguousReason });
        }
        catch (LdapUnavailableException)
        {
            throw Unavailable();
        }

        if (!Guid.TryParse(identity.Subject, out var subject) || subject == Guid.Empty ||
            !string.Equals(identity.EmployeeNo, employeeNo, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(identity.Provider, ldapOptions.Provider, StringComparison.Ordinal))
            throw Unavailable();

        if (identity.Status == LdapDirectoryStatus.Enabled)
            await guard.ResetAsync(employeeNo, cancellationToken);

        return Ok(new ApiResponse<LdapDirectoryUser>
        {
            Code = 0,
            Message = messages["DirectoryRelay.Authenticated"].Value,
            Data = identity
        });
    }

    [HttpPost("personnel")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> ReadPersonnelAsync(CancellationToken cancellationToken)
    {
        EnsureAccessKey();
        // 直接使用 LDAP 实现，避免调用业务同步而写入中转服务器的用户表。
        // 范围完全来自服务器配置，不接收调用方的 BU、节点或 LDAP 查询参数。
        var people = await personnel.ReadAsync(cancellationToken);
        return Ok(new ApiResponse<DirectoryRelayPersonnelResult>
        {
            Code = 0,
            Message = messages["DirectoryRelay.PersonnelRead"].Value,
            Data = new DirectoryRelayPersonnelResult
            {
                Provider = ldapOptions.Provider,
                BusinessUnit = personnel.BusinessUnit,
                People = people
            }
        });
    }

    private void EnsureAccessKey()
    {
        // 未填写有效密钥时仅关闭这两个接口，不影响原有登录和同步。
        if (!DirectoryRelayOptions.IsValidAccessKey(relay.AccessKey))
            throw Unavailable();

        var header = Request.Headers[DirectoryRelayProtocol.KeyHeader];
        var supplied = header.ToString();
        if (header.Count != 1 || !DirectoryRelayOptions.IsValidAccessKey(supplied) ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
                SHA256.HashData(Encoding.UTF8.GetBytes(relay.AccessKey))))
            throw new BusinessException(ErrorCodes.Unauthorized, "DirectoryRelay.ClientInvalid",
                new { Reason = "DirectoryRelayKeyRejected" });
    }

    private static BusinessException Unavailable() =>
        new(ErrorCodes.ServiceUnavailable, "DirectoryRelay.Unavailable",
            new { Reason = DirectoryRelayProtocol.UnavailableReason });
}
