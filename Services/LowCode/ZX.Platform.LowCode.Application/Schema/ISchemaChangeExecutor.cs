using ZX.Platform.LowCode.Application.Metadata;

namespace ZX.Platform.LowCode.Application.Schema;

/// <summary>
/// PlanJson 的反序列化目标。它是变更的唯一事实来源：
/// SQL 只是它的渲染结果，执行、导出、核对三处都读同一份，不允许从 SQL 文本反推计划。
/// </summary>
public sealed record SchemaChangePlan(
    string Kind,
    string AppCode,
    string Entity,
    string? Field,
    string Table,
    IReadOnlyList<string> Statements);

/// <summary>把 DDL 打到库上。测试环境用，生产环境走导出。</summary>
public interface ISchemaChangeExecutor
{
    /// <summary>
    /// 按顺序执行。注意 Oracle 的 DDL 每条隐式提交，**不能当原子操作**：
    /// 第 3 条失败时前 2 条已经生效，调用方必须把这一点如实告诉用户。
    /// </summary>
    Task ExecuteAsync(IReadOnlyList<string> statements, CancellationToken cancellationToken);
}

/// <summary>核对结果。不通过时 Message 说明差在哪。</summary>
public sealed record VerifyResult(bool Verified, string? Message)
{
    public static VerifyResult Ok() => new(true, null);

    public static VerifyResult Missing(string message) => new(false, message);
}
