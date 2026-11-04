using ZX.Platform.Identity.Application.Inventory.Stocktake.Dtos;

namespace ZX.Platform.Identity.Application.Inventory.Stocktake;

public interface IStocktakeReportRepository
{
    Task<StocktakeListDto> GetListAsync(CancellationToken cancellationToken);
}
