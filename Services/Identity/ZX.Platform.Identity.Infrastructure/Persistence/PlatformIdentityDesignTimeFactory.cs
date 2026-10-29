using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ZX.Platform.Identity.Infrastructure.Persistence;

/// <summary>离线生成统一的 Oracle 19c 迁移和 SQL，目标 schema 由导出工具替换。</summary>
public sealed class PlatformIdentityDesignTimeFactory : IDesignTimeDbContextFactory<PlatformIdentityDbContext>
{
    public PlatformIdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlatformIdentityDbContext>();
        PlatformIdentityDbContext.Configure(options,
            "User Id=SQL_GENERATION_ONLY;Password=unused;Data Source=127.0.0.1:1/SQL_GENERATION_ONLY;Connection Timeout=1",
            PlatformIdentityDbContext.DefaultSchema);
        return new PlatformIdentityDbContext(options.Options,
            new IdentityDatabaseSchema(PlatformIdentityDbContext.DefaultSchema));
    }
}
