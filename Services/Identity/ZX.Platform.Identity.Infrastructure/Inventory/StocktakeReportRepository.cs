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
        // 当前报表完整读取现有记录，前端本地筛选；数据源、表名和排序均固定。
        command.CommandText = """
            SELECT ROWIDTOCHAR(t.ROWID), t.WERKS, t.LGORT, t.MATNR,
                   t.WMS_REEL_QTY, t.REEL_QTY, t.KEEP_QTY,
                   t.WMS_QTY, t.REEL_INFO_QTY, t.KEEP_INFO_QTY,
                   TO_CHAR(t.CREATE_DATE, 'YYYY-MM-DD HH24:MI:SS'),
                   TO_CHAR(t.UPDATE_DATE, 'YYYY-MM-DD HH24:MI:SS'),
                   t.WORK_DATE, t.DATATYPE
            FROM EDA.G_WMS_KEYBOARD t
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
                WmsReelQty = ReadDecimal(reader, 4),
                ReelQty = ReadDecimal(reader, 5),
                KeepQty = ReadDecimal(reader, 6),
                WmsQty = ReadDecimal(reader, 7),
                ReelInfoQty = ReadDecimal(reader, 8),
                KeepInfoQty = ReadDecimal(reader, 9),
                CreateDate = ReadString(reader, 10),
                UpdateDate = ReadString(reader, 11),
                WorkDate = ReadString(reader, 12),
                DataType = ReadString(reader, 13)
            });
        }

        return new StocktakeListDto { List = rows, Total = rows.Count };
    }

    private static string? ReadString(DbDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static decimal? ReadDecimal(DbDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetDecimal(index);
}
