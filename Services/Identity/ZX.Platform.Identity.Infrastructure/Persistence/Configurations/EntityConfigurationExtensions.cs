using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Auditing;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations
{
    public static class EntityConfigurationExtensions
    {
        /// <summary>
        /// 配置实体的创建和修改审计字段。
        /// </summary>
        public static void ConfigureAuditing<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : AuditableEntity
        {
            builder.Property(x => x.CreatedAtUtc)
                .HasColumnName("CREATED_AT_UTC")
                .IsRequired();

            builder.Property(x => x.CreatedByUserId)
                .HasColumnName("CREATED_BY_USER_ID");

            builder.Property(x => x.UpdatedAtUtc)
                .HasColumnName("UPDATED_AT_UTC");

            builder.Property(x => x.UpdatedByUserId)
                .HasColumnName("UPDATED_BY_USER_ID");
        }

        /// <summary>
        /// 配置逻辑删除字段和默认查询过滤器。
        /// </summary>
        public static void ConfigureSoftDelete<TEntity>(
            this EntityTypeBuilder<TEntity> builder)
            where TEntity : SoftDeletableEntity
        {
            builder.Property(x => x.IsDeleted)
                .HasColumnName("IS_DELETED")
                .IsRequired();

            // 普通查询默认排除已经逻辑删除的数据。
            builder.HasQueryFilter(x => !x.IsDeleted);
        }
    }
}
