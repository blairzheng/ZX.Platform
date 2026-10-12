using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations
{
    public sealed class UserFactoryConfiguration : IEntityTypeConfiguration<UserFactory>
    {
        public void Configure(EntityTypeBuilder<UserFactory> builder)
        {
            builder.ToTable("ZX_IDENTITY_USER_FACTORY");

            // 联合主键，防止同一个用户重复关联同一个厂区。
            builder.HasKey(x => new
            {
                x.UserId,
                x.FactoryId
            });

            builder.Property(x => x.UserId)
                .HasColumnName("USER_ID")
                .ValueGeneratedNever();

            builder.Property(x => x.FactoryId)
                .HasColumnName("FACTORY_ID")
                .ValueGeneratedNever();

            builder.Property(x => x.IsDefault)
                .HasColumnName("IS_DEFAULT")
                .IsRequired();

            builder.Property(x => x.CreatedAtUtc)
                .HasColumnName("CREATED_AT_UTC")
                .IsRequired();

            // 方便按厂区反查用户。
            builder.HasIndex(x => x.FactoryId);

            builder.ConfigureAuditing();
        }
    }
}
