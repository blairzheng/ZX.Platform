using Microsoft.EntityFrameworkCore;
using ZX.Platform.Identity.Api.LowCode;
using ZX.Platform.LowCode.Application.Metadata;
using ZX.Platform.LowCode.Application.Runtime;
using ZX.Platform.LowCode.Application.Schema;
using ZX.Platform.LowCode.Infrastructure.Persistence;
using ZX.Platform.LowCode.Infrastructure.Schema;

namespace ZX.Platform.Identity.Api.Extensions;

public static class LowCodeExtensions
{
    /// <summary>
    /// 低代码模块的依赖。总开关是 <c>LowCode:Enabled</c>：关闭时一行都不注册，
    /// 低代码的 Controller 会因为缺服务而无法激活——所以关闭时也不挂路由。
    /// </summary>
    public static IServiceCollection AddLowCodeModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        if (!IsEnabled(configuration)) return services;

        // 优先低代码专用账号；没配就复用 Identity 的连接（开发环境常见）。
        var connectionString =
            configuration.GetConnectionString("LowCodeDatabase")
            ?? configuration.GetConnectionString("IdentityDatabase")
            ?? throw new InvalidOperationException(
                "低代码已启用，但 ConnectionStrings:LowCodeDatabase 与 ConnectionStrings:IdentityDatabase 都为空。");

        services.AddDbContext<LowCodeDbContext>(options =>
            options.UseOracle(connectionString, oracle =>
                oracle.MigrationsHistoryTable(LowCodeDbContextFactory.MigrationsTable)));

        services.AddScoped<IMetadataStore, EfMetadataStore>();
        services.AddScoped<ISchemaChangePlanner>(_ => new SchemaChangePlanner());
        services.AddScoped<ISchemaInspector>(provider =>
            new OracleSchemaInspector(provider.GetRequiredService<LowCodeDbContext>()));
        services.AddScoped<ISchemaChangeExecutor, OracleSchemaChangeExecutor>();
        services.AddScoped<IRecordGateway, DapperRecordGateway>();

        services.AddScoped<MetadataService>();
        services.AddScoped<SchemaChangeService>();
        services.AddScoped<QueryEngine>();
        services.AddScoped<RecordEngine>();
        services.AddScoped<LowCodePermissionGuard>();

        return services;
    }

    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue("LowCode:Enabled", false);
}
