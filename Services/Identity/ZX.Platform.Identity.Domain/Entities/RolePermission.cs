using ZX.Platform.Identity.Domain.Auditing;

namespace ZX.Platform.Identity.Domain.Entities;

public sealed class RolePermission : AuditableEntity
{
    private RolePermission() { }
    public Guid RoleId { get; private set; }
    public string PermissionCode { get; private set; } = string.Empty;
    public static RolePermission Create(Guid roleId, string permissionCode) => new() { RoleId = roleId, PermissionCode = permissionCode };
}
