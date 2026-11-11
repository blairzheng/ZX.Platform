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
                // 待审批 OA 账号允许登录；已拒绝的 OA 账号必须禁用。
                table.HasCheckConstraint(
                    "CK_ZID_USER_LDAP_ENABLED",
                    "AUTH_TYPE <> 'LDAP' OR APPROVAL_STATUS <> 'Rejected' OR IS_ENABLED = 0");
                table.HasCheckConstraint(
                    "CK_ZID_USER_OA_STATUS",
                    "OA_DIRECTORY_STATUS IS NULL OR OA_DIRECTORY_STATUS IN ('Enabled', 'Disabled', 'Unknown')");
            });

            builder.HasKey(x => x.Id);
            builder.Ignore(x => x.CanUseSession);
            builder.Ignore(x => x.HasBusinessAccess);

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

            // 目录组织资料与认证、审批和授权分开维护，存量用户全部允许为空。
            builder.Property(x => x.OaAccount).HasColumnName("OA_ACCOUNT").HasMaxLength(100);
            builder.Property(x => x.OaName).HasColumnName("OA_NAME").HasMaxLength(200);
            builder.Property(x => x.OaDisplayName).HasColumnName("OA_DISPLAY_NAME").HasMaxLength(256);
            builder.Property(x => x.OaEmail).HasColumnName("OA_EMAIL").HasMaxLength(320);
            builder.Property(x => x.OaCompany).HasColumnName("OA_COMPANY").HasMaxLength(256);
            builder.Property(x => x.OaBusinessUnit).HasColumnName("OA_BUSINESS_UNIT").HasMaxLength(64);
            builder.Property(x => x.OaDepartmentName).HasColumnName("OA_DEPARTMENT_NAME").HasMaxLength(256);
            builder.Property(x => x.OaDepartmentCode).HasColumnName("OA_DEPARTMENT_CODE").HasMaxLength(100);
            builder.Property(x => x.OaDepartmentRaw).HasColumnName("OA_DEPARTMENT_RAW").HasMaxLength(1000);
            builder.Property(x => x.OaDistinguishedName).HasColumnName("OA_DISTINGUISHED_NAME").HasColumnType("NCLOB");
            builder.Property(x => x.OaManagerDn).HasColumnName("OA_MANAGER_DN").HasColumnType("NCLOB");
            builder.Property(x => x.OaDirectoryStatus).HasColumnName("OA_DIRECTORY_STATUS").HasMaxLength(16);
            // 首次补资料必须仍未同步；并发同步不能覆盖另一请求已补齐的快照。
            // 独立于会话安全版本，不因补资料改变登录或业务授权。
            builder.Property(x => x.OaSyncedAtUtc).HasColumnName("OA_SYNCED_AT_UTC")
                .HasColumnType("TIMESTAMP(7) WITH TIME ZONE").IsConcurrencyToken();

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

            builder.HasIndex(x => new { x.OaBusinessUnit, x.OaDepartmentCode })
                .HasDatabaseName("IX_ZID_USER_OA_BU_DEPT");

            builder.ConfigureAuditing();

            builder.ConfigureSoftDelete();
        }
    }
}
