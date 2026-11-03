using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations
{
    public sealed class SbuConfiguration : IEntityTypeConfiguration<Sbu>
    {
        public void Configure(EntityTypeBuilder<Sbu> builder)
        {
            builder.ToTable("ZX_IDENTITY_SBU");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .HasColumnName("ID")
                .ValueGeneratedNever();

            builder.Property(x => x.Code)
                .HasColumnName("CODE")
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.Name)
                .HasColumnName("NAME")
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(x => x.IsEnabled)
                .HasColumnName("IS_ENABLED")
                .IsRequired();

            builder.Property(x => x.CreatedAtUtc)
                .HasColumnName("CREATED_AT_UTC")
                .IsRequired();

            // SBU编码不能重复。
            builder.HasIndex(x => x.Code)
                .IsUnique();

            builder.ConfigureAuditing();

            builder.ConfigureSoftDelete();
        }
    }
}
