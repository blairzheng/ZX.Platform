namespace ZX.Platform.Identity.Application.Inventory.Stocktake.Dtos;

public sealed class StocktakeRowDto
{
    // 本次列表的行标识，来自 Oracle ROWID，不作为业务主键使用。
    public string RowKey { get; init; } = string.Empty;
    public string? Factory { get; init; }
    public string? StorageLocation { get; init; }
    public string? Material { get; init; }
    public string? PdlineName { get; init; }
    public string? VendorName { get; init; }
    public decimal? AverageReelQty { get; init; }

    // WMS 库存料卷数量、SN 数量。
    public decimal? WmsReelQty { get; init; }
    public decimal? WmsQty { get; init; }

    // MES 线边仓料卷数量、SN 数量。
    public decimal? MesReelQty { get; init; }
    public decimal? MesReelSnQty { get; init; }

    // 当前上线料卷数量；上线余量 = CURRENT_REEL_SN_QTY - CURRENT_KEEP_SN_QTY。
    public decimal? CurrentReelQty { get; init; }
    public decimal? OnlineRemainingQty { get; init; }
}
