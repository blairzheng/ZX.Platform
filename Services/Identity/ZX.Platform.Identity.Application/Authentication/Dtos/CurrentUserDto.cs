using ZX.Platform.Identity.Application.Menus.Dtos;

namespace ZX.Platform.Identity.Application.Authentication.Dtos;

public sealed class CurrentUserDto
{
    public Guid Id { get; init; }
    public required string Account { get; init; }
    public required string DisplayName { get; init; }
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
