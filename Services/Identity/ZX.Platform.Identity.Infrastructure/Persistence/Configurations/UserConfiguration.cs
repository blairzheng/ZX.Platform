using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations
{
    public sealed class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("ZX_IDENTITY_USER");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .HasColumnName("ID")
                .ValueGeneratedNever();

            builder.Property(x => x.Account)
                .HasColumnName("ACCOUNT")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.NormalizedAccount)
                .HasColumnName("NORMALIZED_ACCOUNT")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.DisplayName)
                .HasColumnName("DISPLAY_NAME")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.PasswordHash)
                .HasColumnName("PASSWORD_HASH")
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(x => x.IsEnabled)
                .HasColumnName("IS_ENABLED")
                .IsRequired();

            builder.Property(x => x.AccessFailedCount)
                .HasColumnName("ACCESS_FAILED_COUNT")
                .IsRequired();

            builder.Property(x => x.LockoutEndUtc)
                .HasColumnName("LOCKOUT_END_UTC");

            builder.Property(x => x.CreatedAtUtc)
                .HasColumnName("CREATED_AT_UTC")
                .IsRequired();

            // 登录账号不区分大小写，标准化账号不能重复。
            builder.HasIndex(x => x.NormalizedAccount)
                .IsUnique();

            builder.ConfigureAuditing();

            builder.ConfigureSoftDelete();
        }
    }
}
