using ZX.Platform.Identity.Application.Inventory.Stocktake.Dtos;

namespace ZX.Platform.Identity.Application.Inventory.Stocktake;

public interface IStocktakeReportService
{
    Task<StocktakeListDto> GetListAsync(CancellationToken cancellationToken);
}
