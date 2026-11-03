using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ZX.Platform.Identity.Infrastructure.Persistence;

/// <summary>当前应用实例的表所属 schema；独立于数据库登录账号。</summary>
public sealed record IdentityDatabaseSchema(string Name);

/// <summary>按 schema 缓存模型，防止同一进程中的不同配置复用其他 schema 的表映射。</summary>
public sealed class IdentityDatabaseModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is PlatformIdentityDbContext identity
        ? (context.GetType(), identity.Schema, designTime)
        : (object)(context.GetType(), designTime);
}
