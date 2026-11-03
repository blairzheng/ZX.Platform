using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("ZX_IDENTITY_PERMISSION");
        builder.HasKey(permission => permission.Code);
        builder.Property(permission => permission.Code).HasColumnName("CODE").HasMaxLength(100).ValueGeneratedNever();
        builder.Property(permission => permission.IsActive).HasColumnName("IS_ACTIVE");
    }
}
