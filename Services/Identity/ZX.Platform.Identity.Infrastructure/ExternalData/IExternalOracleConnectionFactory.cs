using System.Data.Common;

namespace ZX.Platform.Identity.Infrastructure.ExternalData;

public interface IExternalOracleConnectionFactory
{
    Task<DbConnection> OpenAsync(string alias, CancellationToken cancellationToken);
}
