using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Infrastructure.Persistence.Configurations
{
    public sealed class UserApprovalAuditConfiguration : IEntityTypeConfiguration<UserApprovalAudit>
    {
        public void Configure(EntityTypeBuilder<UserApprovalAudit> builder)
        {
            builder.ToTable("ZX_IDENTITY_USER_APPROVAL_AUDIT");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .HasColumnName("ID")
                .ValueGeneratedNever();

            builder.Property(x => x.TargetUserId)
                .HasColumnName("TARGET_USER_ID")
                .IsRequired();

            // 账号与工号是事件发生时的快照，不跟随用户表后续改名。
            builder.Property(x => x.TargetAccount)
                .HasColumnName("TARGET_ACCOUNT")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.TargetEmployeeNo)
                .HasColumnName("TARGET_EMPLOYEE_NO")
                .HasMaxLength(32)
                .IsRequired();

            builder.Property(x => x.Action)
                .HasColumnName("ACTION")
                .HasMaxLength(32)
                .IsRequired();

            builder.Property(x => x.FromStatus)
                .HasColumnName("FROM_STATUS")
                .HasMaxLength(32)
                .IsRequired();

            builder.Property(x => x.ToStatus)
                .HasColumnName("TO_STATUS")
                .HasMaxLength(32)
                .IsRequired();

            builder.Property(x => x.ActorUserId)
                .HasColumnName("ACTOR_USER_ID")
                .IsRequired();

            builder.Property(x => x.Reason)
                .HasColumnName("REASON")
                .HasMaxLength(500);

            // 快照存逗号分隔的 32 位 GUID，避免审计表再依赖角色/SBU 表。
            // 必须用 CLOB：61 个 GUID 拼接后就有 2012 字符，VARCHAR2(2000) 会让
            // 一次合法的多角色/多 SBU 审批因审计字段超长而整笔失败；审计数据也不能截断。
            builder.Property(x => x.RoleIds)
                .HasColumnName("ROLE_IDS")
                .HasColumnType("CLOB");

            builder.Property(x => x.SbuIds)
                .HasColumnName("SBU_IDS")
                .HasColumnType("CLOB");

            builder.Property(x => x.TraceId)
                .HasColumnName("TRACE_ID")
                .HasMaxLength(64);

            // 按人追溯审批轨迹。
            builder.HasIndex(x => new { x.TargetUserId, x.CreatedAtUtc });
            // 按时间审计，例如“某天谁开通了哪些账号”。
            builder.HasIndex(x => x.CreatedAtUtc);

            builder.ConfigureAuditing();
        }
    }
}
