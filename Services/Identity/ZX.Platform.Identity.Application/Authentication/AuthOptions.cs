using System.ComponentModel.DataAnnotations;

namespace ZX.Platform.Identity.Application.Authentication;

public sealed class AuthOptions
{
    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 30)]
    public int RefreshTokenDays { get; set; } = 7;

    [Range(1, 20)]
    public int MaximumFailedAttempts { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;

    // 仅首次创建内置管理员角色时绑定已有账号；不再参与运行时授权。
    public string[] AdministratorAccounts { get; set; } = [];
}
