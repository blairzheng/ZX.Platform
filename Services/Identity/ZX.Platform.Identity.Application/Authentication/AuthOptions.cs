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

    // 过渡权限：显式指定已有账号；空名单不会赋予任何人管理权限。
    public string[] AdministratorAccounts { get; set; } = [];

    public bool IsAdministrator(string account) => AdministratorAccounts.Any(
        item => string.Equals(item.Trim(), account, StringComparison.OrdinalIgnoreCase));
}
