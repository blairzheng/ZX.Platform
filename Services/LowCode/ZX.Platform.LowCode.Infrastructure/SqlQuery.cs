namespace ZX.Platform.LowCode.Infrastructure;

/// <summary>绑定参数。Name 不带冒号，由构建器负责加前缀。</summary>
public sealed record SqlParameter(string Name, object? Value);

/// <summary>一条带参数的 SQL 与它的参数列表。</summary>
public sealed record SqlQuery(string Sql, IReadOnlyList<SqlParameter> Parameters)
{
    public static SqlQuery Empty { get; } = new(string.Empty, []);
}
