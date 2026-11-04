namespace ZX.Platform.Identity.Application.Inventory.Stocktake.Dtos;

public sealed class StocktakeRowDto
{
    // 本次列表的行标识，来自 Oracle ROWID，不作为业务主键使用。
    public string RowKey { get; init; } = string.Empty;
    public string? Factory { get; init; }
    public string? StorageLocation { get; init; }
    public string? Material { get; init; }

    // 卷数：WMS、线边仓、使用中的卷数。
    public decimal? WmsReelQty { get; init; }
    public decimal? ReelQty { get; init; }
    public decimal? KeepQty { get; init; }

    // 数量：WMS、线边仓、使用中的物料数量。
    public decimal? WmsQty { get; init; }
    public decimal? ReelInfoQty { get; init; }
    public decimal? KeepInfoQty { get; init; }

    // MES DATE 无时区，按源库墙上时间返回，不冒充 UTC。
    public string? CreateDate { get; init; }
    public string? UpdateDate { get; init; }
    public string? WorkDate { get; init; }
    public string? DataType { get; init; }
}
