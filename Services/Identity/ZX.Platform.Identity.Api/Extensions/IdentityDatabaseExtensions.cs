using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Infrastructure.Persistence;
using ZX.Platform.Identity.Infrastructure.Persistence.Interceptors;

namespace ZX.Platform.Identity.Api.Extensions;

public static class IdentityDatabaseExtensions
{
    public static IServiceCollection AddIdentityDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("IdentityDatabase")
            ?? throw new InvalidOperationException("未配置 IdentityDatabase 数据库连接字符串。");
        var schema = (configuration["IdentityDatabase:Schema"] ?? PlatformIdentityDbContext.DefaultSchema)
            .Trim().ToUpperInvariant();
        PlatformIdentityDbContext.ValidateSchema(schema);
        services.AddSingleton(new IdentityDatabaseSchema(schema));
        services.AddDbContext<PlatformIdentityDbContext>((serviceProvider, options) =>
        {
            PlatformIdentityDbContext.Configure(options, connection, schema);
            options.AddInterceptors(serviceProvider.GetRequiredService<AuditableEntitySaveChangesInterceptor>());
        });
        return services;
    }
}
