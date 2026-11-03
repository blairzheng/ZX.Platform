using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("ZX_IDENTITY_ROLE_PERMISSION");
        builder.HasKey(relation => new { relation.RoleId, relation.PermissionCode });
        builder.Property(relation => relation.RoleId).HasColumnName("ROLE_ID").ValueGeneratedNever();
        builder.Property(relation => relation.PermissionCode).HasColumnName("PERMISSION_CODE").HasMaxLength(100).ValueGeneratedNever();
        builder.HasIndex(relation => relation.PermissionCode);
        builder.ConfigureAuditing();
    }
}
