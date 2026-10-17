using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations;

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("ZX_IDENTITY_USER_ROLE");
        builder.HasKey(relation => new { relation.UserId, relation.RoleId });
        builder.Property(relation => relation.UserId).HasColumnName("USER_ID").ValueGeneratedNever();
        builder.Property(relation => relation.RoleId).HasColumnName("ROLE_ID").ValueGeneratedNever();
        builder.HasIndex(relation => relation.RoleId);
        builder.ConfigureAuditing();
    }
}
