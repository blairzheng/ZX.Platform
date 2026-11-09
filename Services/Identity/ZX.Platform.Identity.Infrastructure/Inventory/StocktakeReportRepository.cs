using System.Data.Common;
using Oracle.ManagedDataAccess.Client;
using ZX.Platform.Identity.Application.Inventory.Stocktake;
using ZX.Platform.Identity.Application.Inventory.Stocktake.Dtos;
using ZX.Platform.Identity.Infrastructure.ExternalData;

namespace ZX.Platform.Identity.Infrastructure.Inventory;

public sealed class StocktakeReportRepository(IExternalOracleConnectionFactory connections) : IStocktakeReportRepository
{
    public async Task<StocktakeListDto> GetListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync("MesEDA", cancellationToken);
        await using var command = connection.CreateCommand();
        ((OracleCommand)command).BindByName = true;
        command.CommandTimeout = 10;
        // 固定读取 BYTIME 明细，前端本地筛选；上线余量按源表两列直接相减。
        command.CommandText = """
            SELECT ROWIDTOCHAR(t.ROWID), t.WERKS, t.LGORT, t.MATNR,
                   t.PDLINE_NAME, t.VENDOR_NAME, t.AVERAGE_REEL_QTY,
                   t.WMS_REEL_QTY, t.WMS_QTY,
                   t.MES_REEL_QTY, t.MES_REEL_SN_QTY,
                   t.CURRENT_REEL_QTY,
                   t.CURRENT_REEL_SN_QTY - t.CURRENT_KEEP_SN_QTY AS ONLINE_REMAINING_QTY
            FROM EDA.G_WMS_KEYBOARD t
            WHERE t.DATATYPE = 'BYTIME'
            ORDER BY t.WERKS, t.LGORT, t.MATNR, t.ROWID
            """;

        var rows = new List<StocktakeRowDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new StocktakeRowDto
            {
                RowKey = reader.GetString(0),
                Factory = ReadString(reader, 1),
                StorageLocation = ReadString(reader, 2),
                Material = ReadString(reader, 3),
                PdlineName = ReadString(reader, 4),
                VendorName = ReadString(reader, 5),
                AverageReelQty = ReadDecimal(reader, 6),
                WmsReelQty = ReadDecimal(reader, 7),
                WmsQty = ReadDecimal(reader, 8),
                MesReelQty = ReadDecimal(reader, 9),
                MesReelSnQty = ReadDecimal(reader, 10),
                CurrentReelQty = ReadDecimal(reader, 11),
                OnlineRemainingQty = ReadDecimal(reader, 12)
            });
        }

        return new StocktakeListDto { List = rows, Total = rows.Count };
    }

    private static string? ReadString(DbDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static decimal? ReadDecimal(DbDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetDecimal(index);
}
