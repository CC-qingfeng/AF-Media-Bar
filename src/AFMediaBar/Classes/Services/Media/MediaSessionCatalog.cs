using System.Diagnostics;
using System.Threading;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 管理 SMTC 媒体会话生命周期，并将第三方动态集合复制为稳定快照。
/// Owns the SMTC session lifecycle and copies the third-party dynamic collection into stable snapshots.
/// </summary>
public sealed class MediaSessionCatalog : IDisposable
{
    private const int SnapshotRetryCount = 4;
    private readonly MediaManager _mediaManager = new();
    private bool _isDisposed;

    public bool IsStarted => _mediaManager.IsStarted;

    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties>? AnyMediaPropertyChanged;
    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo>? AnyPlaybackStateChanged;
    public event Action<MediaSession>? AnySessionOpened;
    public event Action<MediaSession>? AnySessionClosed;
    public event Action<MediaSession>? FocusedSessionChanged;
    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties>? AnyTimelinePropertyChanged;

    /// <summary>
    /// 创建并启动唯一的 SMTC 会话目录，目录事件由本实例统一转发和释放。
    /// Creates and starts the sole SMTC session catalog whose events are forwarded and released by this instance.
    /// </summary>
    public MediaSessionCatalog()
    {
        _mediaManager.OnAnyMediaPropertyChanged += OnAnyMediaPropertyChanged;
        _mediaManager.OnAnyPlaybackStateChanged += OnAnyPlaybackStateChanged;
        _mediaManager.OnAnySessionOpened += OnAnySessionOpened;
        _mediaManager.OnAnySessionClosed += OnAnySessionClosed;
        _mediaManager.OnFocusedSessionChanged += OnFocusedSessionChanged;
        _mediaManager.OnAnyTimelinePropertyChanged += OnAnyTimelinePropertyChanged;
        _mediaManager.Start();
    }

    /// <summary>
    /// 返回当前动态会话集合的稳定数组快照；目录尚未就绪时返回 <see langword="false"/>。
    /// Returns a stable array snapshot of the dynamic session collection, or <see langword="false"/> while the catalog is unavailable.
    /// </summary>
    public bool TryGetSnapshot(out MediaSession[] sessions)
    {
        for (var attempt = 0; attempt < SnapshotRetryCount; attempt++)
        {
            try
            {
                sessions = _mediaManager.CurrentMediaSessions.Values.ToArray();
                return true;
            }
            catch (InvalidOperationException)
            {
                if (attempt + 1 < SnapshotRetryCount)
                {
                    Thread.Yield();
                }
            }
        }

        sessions = Array.Empty<MediaSession>();
        Debug.WriteLine("[MediaSessionCatalog] Failed to copy CurrentMediaSessions after retries.");
        return false;
    }

    /// <summary>
    /// 获取当前由 Windows 判定为焦点的媒体会话，底层目录异常时返回空值。
    /// Gets the media session currently focused by Windows, returning null if the underlying catalog is unavailable.
    /// </summary>
    public MediaSession? GetFocusedSession()
    {
        try
        {
            return _mediaManager.GetFocusedSession();
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"[MediaSessionCatalog] Failed to get focused session: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 请求底层媒体管理器立即重新枚举会话。
    /// Requests an immediate session enumeration from the underlying media manager.
    /// </summary>
    public void ForceUpdate() => _mediaManager.ForceUpdate();

    /// <summary>
    /// 幂等解除底层媒体管理器事件并释放会话目录。
    /// Idempotently detaches media-manager events and disposes the session catalog.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _mediaManager.OnAnyMediaPropertyChanged -= OnAnyMediaPropertyChanged;
        _mediaManager.OnAnyPlaybackStateChanged -= OnAnyPlaybackStateChanged;
        _mediaManager.OnAnySessionOpened -= OnAnySessionOpened;
        _mediaManager.OnAnySessionClosed -= OnAnySessionClosed;
        _mediaManager.OnFocusedSessionChanged -= OnFocusedSessionChanged;
        _mediaManager.OnAnyTimelinePropertyChanged -= OnAnyTimelinePropertyChanged;
        _mediaManager.Dispose();
    }

    private void OnAnyMediaPropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionMediaProperties properties) =>
        AnyMediaPropertyChanged?.Invoke(session, properties);

    private void OnAnyPlaybackStateChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo) =>
        AnyPlaybackStateChanged?.Invoke(session, playbackInfo);

    private void OnAnySessionOpened(MediaSession session) => AnySessionOpened?.Invoke(session);

    private void OnAnySessionClosed(MediaSession session) => AnySessionClosed?.Invoke(session);

    private void OnFocusedSessionChanged(MediaSession session) => FocusedSessionChanged?.Invoke(session);

    private void OnAnyTimelinePropertyChanged(
        MediaSession session,
        GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties) =>
        AnyTimelinePropertyChanged?.Invoke(session, timelineProperties);
}
