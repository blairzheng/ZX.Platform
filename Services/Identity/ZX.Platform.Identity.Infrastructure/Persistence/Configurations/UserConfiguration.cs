using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations
{
    public sealed class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("ZX_IDENTITY_USER", table =>
            {
                // 认证类型与审批状态只允许约定的取值，避免绕过应用写入非法状态。
                table.HasCheckConstraint(
                    "CK_ZID_USER_AUTH_TYPE",
                    "AUTH_TYPE IN ('LOCAL', 'LDAP')");
                table.HasCheckConstraint(
                    "CK_ZID_USER_APPROVAL",
                    "APPROVAL_STATUS IN ('Pending', 'Approved', 'Rejected')");
                // 本地账号必须有密码哈希，OA 账号必须没有；不允许用空串或占位哈希冒充。
                table.HasCheckConstraint(
                    "CK_ZID_USER_PASSWORD",
                    "(AUTH_TYPE = 'LDAP' AND PASSWORD_HASH IS NULL) OR (AUTH_TYPE = 'LOCAL' AND PASSWORD_HASH IS NOT NULL)");
                // 未审批通过的 OA 账号不能处于启用状态。
                table.HasCheckConstraint(
                    "CK_ZID_USER_LDAP_ENABLED",
                    "AUTH_TYPE <> 'LDAP' OR APPROVAL_STATUS = 'Approved' OR IS_ENABLED = 0");
            });

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

            // OA 账号没有本地密码，列必须允许为空。
            builder.Property(x => x.PasswordHash)
                .HasColumnName("PASSWORD_HASH")
                .HasMaxLength(500);

            builder.Property(x => x.AuthType)
                .HasColumnName("AUTH_TYPE")
                .HasMaxLength(16)
                .HasDefaultValue(UserAuthType.Local)
                .IsRequired();

            builder.Property(x => x.EmployeeNo)
                .HasColumnName("EMPLOYEE_NO")
                .HasMaxLength(32);

            builder.Property(x => x.LdapProvider)
                .HasColumnName("LDAP_PROVIDER")
                .HasMaxLength(32);

            builder.Property(x => x.LdapSubject)
                .HasColumnName("LDAP_SUBJECT")
                .HasMaxLength(36);

            builder.Property(x => x.ApprovalStatus)
                .HasColumnName("APPROVAL_STATUS")
                .HasMaxLength(16)
                .HasDefaultValue(UserApprovalStatus.Approved)
                .IsRequired();

            // 并发令牌：审批、启用和禁用使用数据库条件更新，版本不一致时由 EF 抛出并发异常。
            builder.Property(x => x.SecurityVersion)
                .HasColumnName("SECURITY_VERSION")
                .HasDefaultValue(1L)
                .IsConcurrencyToken()
                .IsRequired();

            builder.Property(x => x.ReviewedByUserId)
                .HasColumnName("REVIEWED_BY_USER_ID");

            builder.Property(x => x.ReviewedAtUtc)
                .HasColumnName("REVIEWED_AT_UTC");

            builder.Property(x => x.ReviewReason)
                .HasColumnName("REVIEW_REASON")
                .HasMaxLength(500);

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
                .IsUnique()
                .HasDatabaseName("UX_ZID_USER_ACCOUNT");

            // 工号唯一；本地账号为空。Oracle 唯一索引允许多行同时为空。
            builder.HasIndex(x => x.EmployeeNo)
                .IsUnique()
                .HasDatabaseName("UX_ZID_USER_EMPLOYEE_NO");

            // 同一目录来源下的唯一标识只能对应一个用户记录。
            builder.HasIndex(x => new { x.LdapProvider, x.LdapSubject })
                .IsUnique()
                .HasDatabaseName("UX_ZID_USER_LDAP_IDENTITY");

            // 用户列表按认证类型和审批状态筛选。
            builder.HasIndex(x => new { x.AuthType, x.ApprovalStatus })
                .HasDatabaseName("IX_ZID_USER_TYPE_APPROVAL");

            builder.ConfigureAuditing();

            builder.ConfigureSoftDelete();
        }
    }
}
