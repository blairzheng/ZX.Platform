using ZX.Platform.Identity.Application.Authentication;

namespace ZX.Platform.Identity.Infrastructure.Security;

/// <summary>
/// 给同步 LDAP 调用套上并发名额与总预算。
/// 同步调用无法被取消令牌中断，因此“调用方停止等待”不等于“底层调用已经结束”：
/// 名额只能由真正执行调用的任务在结束时归还；否则反复取消请求会让实际并发超过配置上限。
/// </summary>
public sealed class LdapOperationRunner
{
    private readonly SemaphoreSlim _limiter;
    private readonly int _totalTimeoutSeconds;

    public LdapOperationRunner(int maxConcurrentOperations, int totalTimeoutSeconds)
    {
        _limiter = new SemaphoreSlim(Math.Max(1, maxConcurrentOperations));
        _totalTimeoutSeconds = Math.Max(1, totalTimeoutSeconds);
    }

    /// <summary>
    /// 在并发名额内执行一次目录操作。调用方取消或预算耗尽只结束等待，不终止底层调用、也不提前归还名额。
    /// </summary>
    public async Task<T> RunAsync<T>(Func<LdapOperation, T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_totalTimeoutSeconds);
        // 排队时间也计入总预算，否则高并发下实际耗时远大于配置值。
        budget.CancelAfter(deadline - DateTimeOffset.UtcNow);

        try
        {
            await _limiter.WaitAsync(budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 没拿到名额，无需归还。
            throw new LdapUnavailableException("LdapTimeout");
        }

        var operation = new LdapOperation(deadline, budget.Token);

        // 传 CancellationToken.None：即使等待已超时，也要让底层调用跑完再释放名额，
        // 不能用“停止等待”代替“操作已结束”。
        Task<T> running;
        try
        {
            running = Task.Run(
                () =>
                {
                    try
                    {
                        return work(operation);
                    }
                    finally
                    {
                        // 名额只在这里归还：work 真正结束才释放，正常返回、超时和取消都不例外。
                        _limiter.Release();
                    }
                },
                CancellationToken.None);
        }
        catch
        {
            // 任务没能排上队时 work 不会执行，名额必须在这里退回。
            _limiter.Release();
            throw;
        }

        try
        {
            return await running.WaitAsync(budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Observe(running);
            throw new LdapUnavailableException("LdapTimeout");
        }
        catch
        {
            // 客户端取消（浏览器断开等）或 work 失败：名额仍由后台任务归还，
            // 停止等待之后的异常必须继续被观察，不能变成未观察的任务异常。
            Observe(running);
            throw;
        }
    }

    /// <summary>观察已经不再等待的后台任务：只读取异常，不再影响调用方。</summary>
    private static void Observe(Task task) =>
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}

/// <summary>
/// 单次目录调用的总预算。同步 LDAP 调用无法被取消令牌中断，
/// 因此在每个阶段开始前用剩余预算收紧该阶段的超时。
/// </summary>
public sealed class LdapOperation(DateTimeOffset deadline, CancellationToken token)
{
    private static readonly TimeSpan MinimumSlice = TimeSpan.FromMilliseconds(250);

    public CancellationToken Token { get; } = token;

    public TimeSpan Remaining => deadline - DateTimeOffset.UtcNow;

    public void ThrowIfExpired()
    {
        if (Token.IsCancellationRequested || Remaining <= MinimumSlice)
            throw new LdapUnavailableException("LdapTimeout");
    }

    /// <summary>本次操作可用的超时：不超过配置值，也不超过剩余总预算。</summary>
    public TimeSpan Slice(int configuredSeconds)
    {
        ThrowIfExpired();
        var configured = TimeSpan.FromSeconds(configuredSeconds);
        var remaining = Remaining;
        return remaining < configured ? remaining : configured;
    }
}
