using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Persistence;

/// <summary>
/// OA 账号审批事件仓储。审计记录只追加，不提供修改或删除。
/// </summary>
public interface IUserApprovalAuditRepository
{
    void Add(UserApprovalAudit audit);

    /// <summary>按目标账号取审批轨迹，最新的在前。</summary>
    Task<List<UserApprovalAudit>> GetByTargetUserIdAsync(
        Guid targetUserId, CancellationToken cancellationToken);
}
