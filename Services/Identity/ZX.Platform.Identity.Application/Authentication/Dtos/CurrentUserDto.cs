using ZX.Platform.Identity.Application.Menus.Dtos;

namespace ZX.Platform.Identity.Application.Authentication.Dtos;

public sealed class CurrentUserDto
{
    public Guid Id { get; init; }
    public required string Account { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>
    /// 认证类型，LOCAL 或 LDAP。
    /// </summary>
    public required string AuthType { get; init; }

    /// <summary>业务审批状态；Pending 可以登录，但不能访问业务功能。</summary>
    public required string ApprovalStatus { get; init; }

    /// <summary>是否具备业务授权资格；具体操作仍由角色权限和 SBU 独立决定。</summary>
    public bool HasBusinessAccess { get; init; }

    /// <summary>
    /// 是否允许在 ZX 修改密码；OA 账号为 false，需要前往 OA 修改。
    /// </summary>
    public bool CanChangePassword { get; init; }

    public bool IsAdministrator { get; init; }
    public string[] Roles { get; init; } = [];
    public string[] Permissions { get; init; } = [];
    public List<CurrentUserSbuDto> Sbus { get; init; } = [];
    public List<MenuDto> Menus { get; init; } = [];
}

public sealed class CurrentUserSbuDto
{
    public Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public bool IsDefault { get; init; }
}
