using System.Diagnostics;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 歌词服务：按优先级顺序尝试多个歌词提供者，返回第一个命中的结果。
/// Lyrics service: tries multiple providers in priority order and returns the first hit.
///
/// 职责 Responsibilities:
/// 1. 管理多个歌词源（网易云、LRCLIB、QQ 音乐、酷狗、汽水等）的调用顺序
///    Manage call order of multiple lyric sources (NetEase, LRCLIB, QQ Music, Kugou, Soda Music, and so on)
/// 2. 实现回退策略：第一个源未命中或超时后自动尝试下一个
///    Implement the fallback strategy: try the next source after a miss or a timeout
/// 3. 给整条兜底链加时间上限，单个来源卡住不会拖垮取词
///    Bound the whole fallback chain so one stuck source cannot stall lyric retrieval
///
/// 单源预算按"等待"计算而不是"取消"：已引用的歌词库内部请求不接受取消令牌，因此超时只能让我们不再等待它，
/// 转去下一个来源；被放弃的请求仍会在后台自行结束，其结果被丢弃。
/// The per-source budget measures waiting rather than cancellation: requests inside the referenced lyric library take no
/// cancellation token, so a timeout only stops us from waiting and moves on to the next source, while the abandoned request
/// finishes on its own in the background and its result is dropped.
///
/// ⚠️ 注意 Note:
/// Provider 顺序很重要：精确匹配源（如网易云 song id）应放在前面，模糊匹配源（如按曲名搜索）放在后面。
/// Provider order matters: exact-match sources (like a NetEase song id) should come first, fuzzy-match sources (like a
/// search by title) last.
/// </summary>
public sealed class LyricsService
{
    /// <summary>单个来源的默认等待上限：超过它就当作未命中并继续下一个来源。
    /// Default wait limit for one source: beyond it the source counts as a miss and the next one runs.</summary>
    public static readonly TimeSpan DefaultPerSourceBudget = TimeSpan.FromSeconds(6);

    /// <summary>整条兜底链的默认时间上限：用完就返回未命中，不再等待剩余来源。
    /// Default time limit for the whole fallback chain: once used up it returns a miss instead of waiting for the remaining sources.</summary>
    public static readonly TimeSpan DefaultTotalBudget = TimeSpan.FromSeconds(12);

    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly TimeSpan _perSourceBudget;
    private readonly TimeSpan _totalBudget;

    /// <summary>
    /// 用默认预算创建歌词服务。
    /// Creates the lyric service with the default budgets.
    /// </summary>
    /// <param name="providers">按优先级排列的提供器 / Providers in priority order.</param>
    public LyricsService(params ILyricsProvider[] providers)
        : this(DefaultPerSourceBudget, DefaultTotalBudget, providers)
    {
    }

    /// <summary>
    /// 用显式预算创建歌词服务；非正数预算回退到默认值。
    /// Creates the lyric service with explicit budgets; a non-positive budget falls back to the default.
    /// </summary>
    /// <param name="perSourceBudget">单个来源的等待上限 / Wait limit for one source.</param>
    /// <param name="totalBudget">整条兜底链的时间上限 / Time limit for the whole fallback chain.</param>
    /// <param name="providers">按优先级排列的提供器 / Providers in priority order.</param>
    public LyricsService(TimeSpan perSourceBudget, TimeSpan totalBudget, params ILyricsProvider[] providers)
    {
        _providers = providers;
        _perSourceBudget = perSourceBudget > TimeSpan.Zero ? perSourceBudget : DefaultPerSourceBudget;
        _totalBudget = totalBudget > TimeSpan.Zero ? totalBudget : DefaultTotalBudget;
    }

    /// <summary>
    /// 获取歌词：按顺序尝试所有提供者，返回第一个命中的结果。
    /// Get lyrics: try all providers in order, return the first hit.
    /// </summary>
    /// <param name="request">歌词查询请求 / Lyric query request.</param>
    /// <param name="cancellationToken">取消令牌；调用方取消时抛出 / Cancellation token; a caller cancellation is thrown.</param>
    /// <returns>命中的歌词；全部未命中或预算用尽时为 null / The matched lyrics, or null after every miss or once the budget runs out.</returns>
    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var budget = ResolveRemainingBudget(startedAt);
            if (budget <= TimeSpan.Zero)
            {
                return null;
            }

            var result = await TryProviderAsync(provider, request, budget, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private TimeSpan ResolveRemainingBudget(long startedAt)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var remaining = _totalBudget - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return remaining < _perSourceBudget ? remaining : _perSourceBudget;
    }

    private static async Task<LyricsResult?> TryProviderAsync(
        ILyricsProvider provider,
        LyricsRequest request,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        // 同步抛出的提供器同样按未命中处理：兜底链的健壮性不能依赖每个提供器都自己捕获异常。
        // A provider that throws synchronously counts as a miss too: the chain's robustness cannot depend on every provider
        // catching its own exceptions.
        Task<LyricsResult?> task;
        try
        {
            task = provider.GetLyricsAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }

        var finished = await Task.WhenAny(task, Task.Delay(budget));
        if (!ReferenceEquals(finished, task))
        {
            ObserveFault(task);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        try
        {
            return await task;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 提供器自身的取消按未命中处理，调用方的取消在上面已经抛出。
            // A provider's own cancellation counts as a miss; a caller cancellation was already thrown above.
            return null;
        }
        catch
        {
            // 单个来源的异常不得打断整个兜底链。 / One source's exception must not break the whole fallback chain.
            return null;
        }
    }

    /// <summary>
    /// 消费被放弃任务的异常，避免它变成未观察的任务异常。
    /// Consumes the exception of an abandoned task so it cannot become an unobserved task exception.
    /// </summary>
    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static abandoned => _ = abandoned.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
