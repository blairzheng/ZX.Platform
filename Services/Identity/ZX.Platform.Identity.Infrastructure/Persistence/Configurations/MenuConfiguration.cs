using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations;

public sealed class MenuConfiguration : IEntityTypeConfiguration<Menu>
{
    public void Configure(EntityTypeBuilder<Menu> builder)
    {
        builder.ToTable("ZX_IDENTITY_MENU");
        builder.HasKey(menu => menu.Id);
        builder.Property(menu => menu.Id).HasColumnName("ID").ValueGeneratedNever();
        builder.Property(menu => menu.ParentId).HasColumnName("PARENT_ID");
        builder.Property(menu => menu.Code).HasColumnName("CODE").HasMaxLength(100).IsRequired();
        builder.Property(menu => menu.Kind).HasColumnName("KIND").HasMaxLength(16).IsRequired();
        builder.Property(menu => menu.Title).HasColumnName("TITLE").HasMaxLength(200).IsRequired();
        builder.Property(menu => menu.TitleEn).HasColumnName("TITLE_EN").HasMaxLength(200).IsRequired();
        builder.Property(menu => menu.Icon).HasColumnName("ICON").HasMaxLength(100).IsRequired();
        builder.Property(menu => menu.Path).HasColumnName("PATH").HasMaxLength(200).IsRequired();
        builder.Property(menu => menu.Component).HasColumnName("COMPONENT").HasMaxLength(100).IsRequired();
        builder.Property(menu => menu.PermissionCode).HasColumnName("PERMISSION_CODE").HasMaxLength(100).IsRequired();
        builder.Property(menu => menu.Sort).HasColumnName("SORT");
        builder.Property(menu => menu.KeepAlive).HasColumnName("KEEP_ALIVE");
        builder.Property(menu => menu.ShowInMenu).HasColumnName("SHOW_IN_MENU");
        builder.Property(menu => menu.ShowInTags).HasColumnName("SHOW_IN_TAGS");
        builder.Property(menu => menu.Affix).HasColumnName("AFFIX");
        builder.Property(menu => menu.IsEnabled).HasColumnName("IS_ENABLED");
        builder.Property(menu => menu.IsBuiltIn).HasColumnName("IS_BUILT_IN");
        builder.HasIndex(menu => menu.Code).IsUnique();
        builder.HasIndex(menu => menu.ParentId);
        builder.HasIndex(menu => menu.Path).IsUnique().HasFilter("[KIND] = 'page' AND [IS_DELETED] = 0");
        builder.HasIndex(menu => menu.PermissionCode).IsUnique().HasFilter("[KIND] = 'action' AND [IS_DELETED] = 0");
        builder.ConfigureAuditing();
        builder.ConfigureSoftDelete();
    }
}
