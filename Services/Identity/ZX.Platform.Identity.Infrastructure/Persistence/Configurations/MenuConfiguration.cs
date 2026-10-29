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
        // Oracle 将空字符串存为 NULL；这些业务可空字段读取时由实体还原为空字符串。
        builder.Property(menu => menu.TitleEn).HasColumnName("TITLE_EN").HasMaxLength(200).IsRequired(false);
        builder.Property(menu => menu.Icon).HasColumnName("ICON").HasMaxLength(100).IsRequired(false);
        builder.Property(menu => menu.Path).HasColumnName("PATH").HasMaxLength(200).IsRequired(false);
        builder.Property(menu => menu.Component).HasColumnName("COMPONENT").HasMaxLength(100).IsRequired(false);
        builder.Property(menu => menu.PermissionCode).HasColumnName("PERMISSION_CODE").HasMaxLength(100).IsRequired(false);
        builder.Property(menu => menu.Sort).HasColumnName("SORT");
        builder.Property(menu => menu.KeepAlive).HasColumnName("KEEP_ALIVE");
        builder.Property(menu => menu.ShowInMenu).HasColumnName("SHOW_IN_MENU");
        builder.Property(menu => menu.ShowInTags).HasColumnName("SHOW_IN_TAGS");
        builder.Property(menu => menu.Affix).HasColumnName("AFFIX");
        builder.Property(menu => menu.IsEnabled).HasColumnName("IS_ENABLED");
        builder.Property(menu => menu.IsBuiltIn).HasColumnName("IS_BUILT_IN");
        builder.HasIndex(menu => menu.Code).IsUnique();
        builder.HasIndex(menu => menu.ParentId);
        // Oracle 无筛选索引；虚拟列只为未删除的对应类型提供索引值，其他行返回 NULL。
        builder.Property<string>("ActivePagePath").HasColumnName("ACTIVE_PAGE_PATH").HasMaxLength(200)
            .HasComputedColumnSql("CASE WHEN \"KIND\" = N'page' AND \"IS_DELETED\" = 0 THEN \"PATH\" END", stored: false);
        builder.Property<string>("ActiveActionPermission").HasColumnName("ACTIVE_ACTION_PERMISSION").HasMaxLength(100)
            .HasComputedColumnSql("CASE WHEN \"KIND\" = N'action' AND \"IS_DELETED\" = 0 THEN \"PERMISSION_CODE\" END", stored: false);
        builder.HasIndex("ActivePagePath").IsUnique().HasFilter(null);
        builder.HasIndex("ActiveActionPermission").IsUnique().HasFilter(null);
        builder.ConfigureAuditing();
        builder.ConfigureSoftDelete();
    }
}
