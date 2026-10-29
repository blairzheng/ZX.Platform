using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations
{
    public sealed class UserSbuConfiguration : IEntityTypeConfiguration<UserSbu>
    {
        public void Configure(EntityTypeBuilder<UserSbu> builder)
        {
            builder.ToTable("ZX_IDENTITY_USER_SBU");

            // 联合主键，防止同一个用户重复关联同一个SBU。
            builder.HasKey(x => new
            {
                x.UserId,
                x.SbuId
            });

            builder.Property(x => x.UserId)
                .HasColumnName("USER_ID")
                .ValueGeneratedNever();

            builder.Property(x => x.SbuId)
                .HasColumnName("SBU_ID")
                .ValueGeneratedNever();

            builder.Property(x => x.IsDefault)
                .HasColumnName("IS_DEFAULT")
                .IsRequired();

            builder.Property(x => x.CreatedAtUtc)
                .HasColumnName("CREATED_AT_UTC")
                .IsRequired();

            // 方便按SBU反查用户。
            builder.HasIndex(x => x.SbuId);

            builder.ConfigureAuditing();
        }
    }
}
