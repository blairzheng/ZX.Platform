namespace ZX.Platform.Identity.Application.Roles.Dtos;

public sealed class RoleDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsBuiltIn { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string[] PermissionCodes { get; set; } = [];
}
