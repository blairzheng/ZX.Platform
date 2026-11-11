namespace ZX.Platform.Identity.Application.Personnel;

/// <summary>仅供人员同步使用的服务端目录读取能力，不参与密码认证或业务授权。</summary>
public interface IOaPersonnelDirectory
{
    string BusinessUnit { get; }

    Task<IReadOnlyList<OaDirectoryPerson>> ReadAsync(CancellationToken cancellationToken);
}

public sealed record OaDirectoryPerson(
    string Provider,
    string Subject,
    string Account,
    string EmployeeNo,
    string Name,
    string DisplayName,
    string? Email,
    string? Company,
    string? DepartmentRaw,
    string DistinguishedName,
    string? ManagerDn,
    string DirectoryStatus);
