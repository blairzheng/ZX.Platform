namespace ZX.Platform.Identity.Application.Authentication.Dtos;

public sealed class CurrentUserDto
{
    public Guid Id { get; init; }
    public required string Account { get; init; }
    public required string DisplayName { get; init; }
    public bool IsAdministrator { get; init; }
}
