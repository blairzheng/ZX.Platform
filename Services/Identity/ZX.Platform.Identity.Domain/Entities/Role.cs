using ZX.Platform.Identity.Domain.Auditing;
using ZX.Platform.Identity.Domain.Exceptions;

namespace ZX.Platform.Identity.Domain.Entities;

public sealed class Role : SoftDeletableEntity
{
    public const string AdministratorCode = "ADMINISTRATOR";

    private Role() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }
    public bool IsBuiltIn { get; private set; }

    public static Role Create(string code, string name, string? description = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = code.Trim().ToUpperInvariant(),
        Name = name.Trim(),
        Description = description?.Trim() ?? string.Empty,
        IsEnabled = true
    };

    public static Role CreateAdministrator()
    {
        var role = Create(AdministratorCode, "Administrator");
        role.IsBuiltIn = true;
        return role;
    }

    public void ChangeDetails(string name, string? description)
    {
        EnsureEditable();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
    }

    public void SetEnabled(bool enabled)
    {
        EnsureEditable();
        IsEnabled = enabled;
    }

    public void EnsureEditable()
    {
        if (IsBuiltIn) throw new DomainException("Access.BuiltInRoleReadOnly");
    }

    protected override void OnDeleted()
    {
        EnsureEditable();
        IsEnabled = false;
    }

    protected override void OnRestored()
    {
        EnsureEditable();
        IsEnabled = true;
    }
}
