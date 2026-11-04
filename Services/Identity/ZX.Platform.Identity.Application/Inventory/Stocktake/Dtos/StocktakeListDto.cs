namespace ZX.Platform.Identity.Application.Inventory.Stocktake.Dtos;

public sealed class StocktakeListDto
{
    public List<StocktakeRowDto> List { get; init; } = [];
    public long Total { get; init; }
}
