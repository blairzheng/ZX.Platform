using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("ZX_IDENTITY_ROLE");
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Id).HasColumnName("ID").ValueGeneratedNever();
        builder.Property(role => role.Code).HasColumnName("CODE").HasMaxLength(50).IsRequired();
        builder.Property(role => role.Name).HasColumnName("NAME").HasMaxLength(200).IsRequired();
        builder.Property(role => role.Description).HasColumnName("DESCRIPTION").HasMaxLength(500).IsRequired();
        builder.Property(role => role.IsEnabled).HasColumnName("IS_ENABLED");
        builder.Property(role => role.IsBuiltIn).HasColumnName("IS_BUILT_IN");
        builder.HasIndex(role => role.Code).IsUnique();
        builder.ConfigureAuditing();
        builder.ConfigureSoftDelete();
    }
}
