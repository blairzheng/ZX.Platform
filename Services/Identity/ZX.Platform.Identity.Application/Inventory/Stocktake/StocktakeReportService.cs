using ZX.Platform.Identity.Application.Inventory.Stocktake.Dtos;

namespace ZX.Platform.Identity.Application.Inventory.Stocktake;

public sealed class StocktakeReportService(IStocktakeReportRepository repository) : IStocktakeReportService
{
    public Task<StocktakeListDto> GetListAsync(CancellationToken cancellationToken)
        => repository.GetListAsync(cancellationToken);
}
