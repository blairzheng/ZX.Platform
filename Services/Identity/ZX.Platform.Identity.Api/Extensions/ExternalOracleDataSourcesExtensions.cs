using ZX.Platform.Identity.Infrastructure.ExternalData;

namespace ZX.Platform.Identity.Api.Extensions;

public static class ExternalOracleDataSourcesExtensions
{
    public static IServiceCollection AddExternalOracleDataSources(
        this IServiceCollection services, IConfiguration configuration)
    {
        var sources = configuration.GetSection("ExternalOracle:Sources")
            .Get<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        services.AddSingleton<IExternalOracleConnectionFactory>(
            new ExternalOracleConnectionFactory(sources));
        return services;
    }
}
