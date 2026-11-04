using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZX.Platform.Identity.Api.Contracts;
using ZX.Platform.Identity.Api.Localization;
using ZX.Platform.Identity.Application.Authorization;
using ZX.Platform.Identity.Application.Inventory.Stocktake;
using ZX.Platform.Identity.Application.Inventory.Stocktake.Dtos;

namespace ZX.Platform.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = IdentityPermissions.InventoryStocktakeRead)]
public sealed class InventoryStocktakeController(IStocktakeReportService reports, IApiMessageLocalizer messages) : ControllerBase
{
    // 全局报表，不标注 RequireSbu；功能权限仍由后端独立校验。
    [HttpGet("list")]
    public async Task<IActionResult> GetListAsync(CancellationToken cancellationToken)
    {
        var list = await reports.GetListAsync(cancellationToken);
        return Ok(new ApiResponse<StocktakeListDto>
        {
            Code = 0,
            Message = messages["Stocktake.ListLoaded"].Value,
            Data = list
        });
    }
}
