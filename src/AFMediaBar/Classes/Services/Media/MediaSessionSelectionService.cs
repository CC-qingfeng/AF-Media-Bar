using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 维护当前媒体来源选择、自动跟随和浏览器会话重建缓冲。
/// Maintains the selected source, auto-follow behavior, and the browser session recreation grace period.
/// </summary>
public sealed class MediaSessionSelectionService : IDisposable
{
    private static readonly TimeSpan AutoSwitchGracePeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MissingSessionGracePeriod = TimeSpan.FromSeconds(3);
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
            Interval = TimeSpan.FromMilliseconds(200)
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
            string.Equals(session.Id, key, StringComparison.Ordinal));
        if (selected is null)
        {
            return false;
        }

        ClearPendingAutoSwitch();
        ClearMissingSession();
        SelectedKey = selected.Id;
        SelectedSourceId = selected.ControlSession.SourceAppUserModelId ?? string.Empty;
        return true;
    }

    /// <summary>
    /// 按手动选择、宽限期和系统焦点优先级解析当前会话。
    /// Resolves the current session using manual selection, grace-period, and system-focus precedence.
    /// </summary>
    public MediaSession? Resolve(IReadOnlyList<MediaSession> sessions)
    {
        var selected = sessions.FirstOrDefault(session =>
            string.Equals(session.Id, SelectedKey, StringComparison.Ordinal));
        if (selected is not null)
        {
            if (_missingSessionSinceUtc != default)
            {
                Debug.WriteLine($"[MediaSessionSelection] Browser source recovered: {SelectedSourceId}");
            }

            SelectedSourceId = selected.ControlSession.SourceAppUserModelId ?? SelectedSourceId;
            ClearMissingSession();
            return selected;
        }

        var restored = FindRestoredSession(sessions);
        if (restored is not null)
        {
            SelectedKey = restored.Id;
            SelectedSourceId = restored.ControlSession.SourceAppUserModelId ?? string.Empty;
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
                string.Equals(session.Id, focused.Id, StringComparison.Ordinal))
            : null;
        selected ??= sessions.FirstOrDefault(IsPlaying) ?? sessions.FirstOrDefault();
        if (selected is null)
        {
            SelectedKey = null;
            SelectedSourceId = null;
            return null;
        }

        SelectedKey = selected.Id;
        SelectedSourceId = selected.ControlSession.SourceAppUserModelId ?? string.Empty;
        return selected;
    }

    /// <summary>
    /// 在自动跟随模式下尝试切换到正在播放的会话，并报告选择是否变化。
    /// Attempts to follow a playing session in automatic mode and reports whether the selection changed.
    /// </summary>
    public bool TryAutoSwitchToPlaying(IReadOnlyList<MediaSession> sessions)
    {
        var current = sessions.FirstOrDefault(session =>
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

        var currentSourceId = current.ControlSession.SourceAppUserModelId ?? string.Empty;
        if (IsBrowserSource(currentSourceId))
        {
            ClearPendingAutoSwitch();
            return false;
        }

        var replacement = sessions.FirstOrDefault(candidate =>
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
        SelectedSourceId = replacement.ControlSession.SourceAppUserModelId ?? string.Empty;
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
                session.ControlSession.SourceAppUserModelId ?? string.Empty,
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
