using ZX.Platform.Identity.Domain.Auditing;

namespace ZX.Platform.Identity.Domain.Entities;

/// <summary>
/// OA 账号审批事件。只追加不修改，保留预开通、通过、拒绝与重新审核的完整轨迹。
/// 用户表上的 ReviewedBy / ReviewedAt / ReviewReason 只反映最近一次审核，
/// 重新审核会覆盖旧值，不能当作审批历史。
/// </summary>
public sealed class UserApprovalAudit : AuditableEntity
{
    private UserApprovalAudit() { }

    public Guid Id { get; private set; }

    public Guid TargetUserId { get; private set; }

    /// <summary>目标账号快照：账号可能改名，审计要保留当时的值。</summary>
    public string TargetAccount { get; private set; } = string.Empty;

    public string TargetEmployeeNo { get; private set; } = string.Empty;

    /// <summary>取值见 <see cref="UserApprovalActions"/>。</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>审批前状态；首次创建时为 None。</summary>
    public string FromStatus { get; private set; } = string.Empty;

    public string ToStatus { get; private set; } = string.Empty;

    public Guid ActorUserId { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>
    /// 本次审批写入的角色与 SBU 快照，逗号分隔的 32 位 GUID。
    /// 按 CLOB 保存：数量由业务权限决定，不能因为审计字段长度而限制授权或截断证据。
    /// </summary>
    public string? RoleIds { get; private set; }

    public string? SbuIds { get; private set; }

    /// <summary>调用链标识，便于和日志、追踪系统对齐。</summary>
    public string? TraceId { get; private set; }

    public static UserApprovalAudit CreateForNoneStatus(
        User user, string action, Guid actorUserId, string? reason,
        IEnumerable<Guid> roleIds, IEnumerable<Guid> sbuIds, string? traceId) =>
        Create(user, action, "None", actorUserId, reason, roleIds, sbuIds, traceId);

    public static UserApprovalAudit Create(
        User user,
        string action,
        string fromStatus,
        Guid actorUserId,
        string? reason,
        IEnumerable<Guid> roleIds,
        IEnumerable<Guid> sbuIds,
        string? traceId)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        // 审批审计只针对带工号的 OA 账号。Oracle 把空串当 NULL，而 TARGET_EMPLOYEE_NO 是
        // NOT NULL，缺工号时会在写库阶段才报 ORA-01400；这里提前拒绝，错误位置更准确。
        ArgumentException.ThrowIfNullOrWhiteSpace(user.EmployeeNo);

        return new UserApprovalAudit
        {
            Id = Guid.NewGuid(),
            TargetUserId = user.Id,
            TargetAccount = user.Account,
            TargetEmployeeNo = user.EmployeeNo!,
            Action = action,
            FromStatus = fromStatus,
            // 取落库后的实际状态，避免事件与实体不一致。
            ToStatus = user.ApprovalStatus,
            ActorUserId = actorUserId,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            RoleIds = Join(roleIds),
            SbuIds = Join(sbuIds),
            TraceId = string.IsNullOrWhiteSpace(traceId) ? null : traceId.Trim()
        };
    }

    private static string? Join(IEnumerable<Guid> ids)
    {
        var values = ids.Distinct().Select(id => id.ToString("N")).ToList();
        return values.Count == 0 ? null : string.Join(',', values);
    }
}

/// <summary>
/// 审批事件类型。ReApproved 表示被拒绝后重新审核通过，与首次审批区分开。
/// </summary>
public static class UserApprovalActions
{
    /// <summary>预开通：管理员在用户不存在时按工号建档。</summary>
    public const string Provisioned = "Provisioned";

    public const string Approved = "Approved";

    public const string Rejected = "Rejected";

    public const string ReApproved = "ReApproved";
}
