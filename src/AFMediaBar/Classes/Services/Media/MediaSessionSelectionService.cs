using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 维护当前媒体来源选择、自动跟随和浏览器会话重建缓冲。
/// Maintains the selected source, auto-follow behavior, and the browser session recreation grace period.
/// </summary>
public sealed class MediaSessionSelectionService : IDisposable, IMemoryPrunable
{
    private static readonly TimeSpan AutoSwitchGracePeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MissingSessionGracePeriod = TimeSpan.FromSeconds(3);

    /// <summary>常规刷新周期。计时器只在自动切换或会话重建的宽限期内运行，因此这是"正在挣扎着切源"时的节奏。
    /// The ordinary refresh period. The timer only runs inside the auto-switch or session-recreation grace period, so this is the cadence while a
    /// switch is being resolved.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>剪枝后的刷新周期：宽限期是按时间戳判定的，放慢只是让恢复得晚一点，不会漏掉切换。
    /// The refresh period while pruned: the grace periods are decided from timestamps, so slowing down only delays the resolution instead of losing it.</summary>
    private static readonly TimeSpan PrunedRefreshInterval = TimeSpan.FromSeconds(1);
    private readonly MediaSessionCatalog _catalog;
    private readonly DispatcherTimer _timer;
    private string? _pendingAutoSwitchKey;
    private DateTime _pendingAutoSwitchSinceUtc;
    private DateTime _missingSessionSinceUtc;
    private bool _isDisposed;

    public string? SelectedKey { get; private set; }
    public string? SelectedSourceId { get; private set; }

    public event Action? RefreshRequested;

    /// <summary>
    /// 创建会话选择状态机，并使用共享目录提供焦点会话与刷新信号。
    /// Creates the session-selection state machine using the shared catalog for focused sessions and refresh signals.
    /// </summary>
    public MediaSessionSelectionService(MediaSessionCatalog catalog)
    {
        _catalog = catalog;
        _timer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher)
        {
            Interval = RefreshInterval
        };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>
    /// 在给定稳定会话集合中建立手动选择；键不存在时不改变当前状态。
    /// Establishes a manual selection within the supplied stable session set without changing state for an unknown key.
    /// </summary>
    public bool Select(string key, IReadOnlyList<MediaSession> sessions)
    {
        var selected = sessions.FirstOrDefault(session =>
            MediaSessionGuard.IsUsable(session) &&
            string.Equals(session.Id, key, StringComparison.Ordinal));
        if (selected is null)
        {
            return false;
        }

        ClearPendingAutoSwitch();
        ClearMissingSession();
        SelectedKey = selected.Id;
        SelectedSourceId = MediaSessionGuard.GetSourceId(selected);
        return true;
    }

    /// <summary>
    /// 按手动选择、宽限期和系统焦点优先级解析当前会话，并且只返回仍可读取的会话；全部会话已关闭时返回空值。
    /// Resolves the current session using manual selection, grace-period, and system-focus precedence, returning only a
    /// still-readable session and null when every session has been closed.
    /// </summary>
    public MediaSession? Resolve(IReadOnlyList<MediaSession> sessions)
    {
        var selected = sessions.FirstOrDefault(session =>
            MediaSessionGuard.IsUsable(session) &&
            string.Equals(session.Id, SelectedKey, StringComparison.Ordinal));
        if (selected is not null)
        {
            if (_missingSessionSinceUtc != default)
            {
                Debug.WriteLine($"[MediaSessionSelection] Browser source recovered: {SelectedSourceId}");
            }

            SelectedSourceId = MediaSessionGuard.GetSourceId(selected);
            ClearMissingSession();
            return selected;
        }

        var restored = FindRestoredSession(sessions);
        if (restored is not null)
        {
            SelectedKey = restored.Id;
            SelectedSourceId = MediaSessionGuard.GetSourceId(restored);
            Debug.WriteLine($"[MediaSessionSelection] Browser source recreated: {SelectedSourceId}");
            ClearMissingSession();
            return restored;
        }

        if (TryHoldMissingSession())
        {
            return null;
        }

        ClearPendingAutoSwitch();
        ClearMissingSession();
        var focused = _catalog.GetFocusedSession();
        selected = focused is not null
            ? sessions.FirstOrDefault(session =>
                MediaSessionGuard.IsUsable(session) &&
                string.Equals(session.Id, focused.Id, StringComparison.Ordinal))
            : null;
        selected ??= sessions.FirstOrDefault(session =>
            MediaSessionGuard.IsUsable(session) && IsPlaying(session));
        selected ??= sessions.FirstOrDefault(MediaSessionGuard.IsUsable);
        if (selected is null)
        {
            SelectedKey = null;
            SelectedSourceId = null;
            return null;
        }

        SelectedKey = selected.Id;
        SelectedSourceId = MediaSessionGuard.GetSourceId(selected);
        return selected;
    }

    /// <summary>
    /// 在自动跟随模式下尝试切换到正在播放的会话，并报告选择是否变化。
    /// Attempts to follow a playing session in automatic mode and reports whether the selection changed.
    /// </summary>
    public bool TryAutoSwitchToPlaying(IReadOnlyList<MediaSession> sessions)
    {
        var current = sessions.FirstOrDefault(session =>
            MediaSessionGuard.IsUsable(session) &&
            string.Equals(session.Id, SelectedKey, StringComparison.Ordinal));
        if (current is null)
        {
            if (IsMissingSessionGraceActive)
            {
                _timer.Start();
            }
            else
            {
                ClearPendingAutoSwitch();
            }

            return false;
        }

        if (IsPlaying(current))
        {
            ClearPendingAutoSwitch();
            return false;
        }

        var currentSourceId = MediaSessionGuard.GetSourceId(current);
        if (IsBrowserSource(currentSourceId))
        {
            ClearPendingAutoSwitch();
            return false;
        }

        var replacement = sessions.FirstOrDefault(candidate =>
            MediaSessionGuard.IsUsable(candidate) &&
            !ReferenceEquals(candidate, current) && IsPlaying(candidate));
        if (replacement is null)
        {
            ClearPendingAutoSwitch();
            return false;
        }

        if (!string.Equals(_pendingAutoSwitchKey, replacement.Id, StringComparison.Ordinal))
        {
            _pendingAutoSwitchKey = replacement.Id;
            _pendingAutoSwitchSinceUtc = DateTime.UtcNow;
            _timer.Start();
            return false;
        }

        if (DateTime.UtcNow - _pendingAutoSwitchSinceUtc < AutoSwitchGracePeriod)
        {
            _timer.Start();
            return false;
        }

        ClearPendingAutoSwitch();
        SelectedKey = replacement.Id;
        SelectedSourceId = MediaSessionGuard.GetSourceId(replacement);
        return true;
    }

    public bool IsMissingSessionGraceActive =>
        _missingSessionSinceUtc != default &&
        DateTime.UtcNow - _missingSessionSinceUtc < MissingSessionGracePeriod &&
        IsBrowserSource(SelectedSourceId ?? string.Empty);

    /// <summary>
    /// 在浏览器会话暂时消失时启动或维持恢复缓冲。
    /// Starts or maintains the recovery grace period while a browser session is temporarily missing.
    /// </summary>
    public bool TryHoldMissingSession()
    {
        if (!IsBrowserSource(SelectedSourceId ?? string.Empty))
        {
            return false;
        }

        if (_missingSessionSinceUtc == default)
        {
            _missingSessionSinceUtc = DateTime.UtcNow;
            Debug.WriteLine($"[MediaSessionSelection] Holding missing browser source: {SelectedSourceId}");
        }

        if (DateTime.UtcNow - _missingSessionSinceUtc >= MissingSessionGracePeriod)
        {
            Debug.WriteLine($"[MediaSessionSelection] Missing browser source grace expired: {SelectedSourceId}");
            return false;
        }

        _timer.Start();
        return true;
    }

    /// <summary>
    /// 清除手动选择和相关宽限状态，使后续解析恢复自动跟随。
    /// Clears manual selection and its grace state so subsequent resolution returns to automatic following.
    /// </summary>
    public void ClearSelection()
    {
        SelectedKey = null;
        SelectedSourceId = null;
        ClearPendingAutoSwitch();
        ClearMissingSession();
    }

    /// <summary>参与者名称，只用于诊断。/ Participant name, used for diagnostics only.</summary>
    public string PruneParticipantName => "session-selection";

    /// <summary>
    /// 放慢或恢复刷新周期。这个计时器本身只在自己的宽限期内运行（平时是停的），因此剪枝对它做的只是"每秒看一次"而不是"每秒看五次"：
    /// 它的状态机完全按时间戳判定，放慢只影响恰好卡在宽限期里的那一次切换早几百毫秒还是晚几百毫秒被发现。
    /// Slows down or restores the refresh period. This timer only runs inside its own grace period — it is stopped the rest of the time — so pruning
    /// only changes five looks per second into one, and since the state machine decides everything from timestamps, the only effect is that a switch
    /// caught mid-grace-period is noticed a few hundred milliseconds later.
    /// </summary>
    /// <param name="level">目标档位。/ The target level.</param>
    public void Prune(MemoryPruneLevel level)
    {
        if (_isDisposed)
        {
            return;
        }

        var interval = level == MemoryPruneLevel.None ? RefreshInterval : PrunedRefreshInterval;
        if (_timer.Interval != interval)
        {
            _timer.Interval = interval;
        }
    }

    /// <summary>
    /// 停止选择计时器并阻止后续刷新请求。
    /// Stops selection timers and prevents subsequent refresh requests.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private MediaSession? FindRestoredSession(IReadOnlyList<MediaSession> sessions) =>
        IsMissingSessionGraceActive
            ? sessions.FirstOrDefault(session => IsSameBrowserSource(
                MediaSessionGuard.GetSourceId(session),
                SelectedSourceId ?? string.Empty))
            : null;

    private void ClearPendingAutoSwitch()
    {
        _pendingAutoSwitchKey = null;
        _pendingAutoSwitchSinceUtc = default;
        if (!IsMissingSessionGraceActive)
        {
            _timer.Stop();
        }
    }

    private void ClearMissingSession()
    {
        _missingSessionSinceUtc = default;
        if (string.IsNullOrEmpty(_pendingAutoSwitchKey))
        {
            _timer.Stop();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            _timer.Stop();
            return;
        }

        RefreshRequested?.Invoke();
    }

    private static bool IsPlaying(MediaSession session)
    {
        // 会话可能在本方法执行期间被第三方库关闭，读取失败一律按“未播放”处理。
        // The third-party library may close the session while this method runs, so every read failure means "not playing".
        if (!MediaSessionGuard.IsUsable(session))
        {
            return false;
        }

        try
        {
            return session.ControlSession.GetPlaybackInfo().PlaybackStatus ==
                Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBrowserSource(string sourceId) =>
        sourceId.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("microsoftedge", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("firefox", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameBrowserSource(string leftSourceId, string rightSourceId)
    {
        var leftFamily = GetBrowserFamily(leftSourceId);
        var rightFamily = GetBrowserFamily(rightSourceId);
        return leftFamily is not null && string.Equals(leftFamily, rightFamily, StringComparison.Ordinal);
    }

    private static string? GetBrowserFamily(string sourceId)
    {
        if (sourceId.Contains("chrome", StringComparison.OrdinalIgnoreCase))
        {
            return "chrome";
        }

        if (sourceId.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
            sourceId.Contains("microsoftedge", StringComparison.OrdinalIgnoreCase))
        {
            return "edge";
        }

        return sourceId.Contains("firefox", StringComparison.OrdinalIgnoreCase)
            ? "firefox"
            : null;
    }
}
