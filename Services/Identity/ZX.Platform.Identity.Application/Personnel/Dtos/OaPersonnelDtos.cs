namespace ZX.Platform.Identity.Application.Personnel.Dtos;

public sealed class OaPersonnelSyncResultDto
{
    public int ReadCount { get; init; }
    public int AddedCount { get; init; }
    public int SkippedCount { get; init; }
    /// <summary>跳过已有账号中，本次首次补齐 OA 组织资料的数量。</summary>
    public int ProfileAddedCount { get; init; }
    public int InvalidCount { get; init; }
    public int TotalCount { get; init; }
    public DateTimeOffset SyncedAtUtc { get; init; }
}

/// <summary>返回全部已入库人员和解析后的父人员 ID，避免深层嵌套导致序列化深度溢出。</summary>
public sealed class OaPersonnelTreeDto
{
    public string BusinessUnit { get; init; } = string.Empty;
    public int Total { get; init; }
    public int DepartmentCount { get; init; }
    public DateTimeOffset? LastSyncedAtUtc { get; init; }
    public IReadOnlyList<OaPersonDto> People { get; init; } = [];
}

public sealed class OaPersonDto
{
    /// <summary>MOM 用户 ID。</summary>
    public Guid Id { get; init; }
    public string EmployeeNo { get; init; } = string.Empty;
    /// <summary>MOM 登录账号，OA 用户使用工号。</summary>
    public string Account { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Company { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public string? DepartmentCode { get; init; }
    public string? DepartmentRaw { get; init; }
    public Guid? ManagerId { get; set; }
    public string? ManagerName { get; init; }
    /// <summary>Resolved / Missing / None / Cycle；有环的边不会作为树的父子关系返回。</summary>
    public string ManagerRelation { get; set; } = "None";
    public string DirectoryStatus { get; init; } = string.Empty;
    public DateTimeOffset SyncedAtUtc { get; init; }
}
