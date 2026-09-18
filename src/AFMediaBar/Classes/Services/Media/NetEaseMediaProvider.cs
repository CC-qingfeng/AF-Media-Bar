using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Services.Players;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 读取网易云客户端内存并提供更精确的进度、歌曲标识、封面和歌词。
/// Reads NetEase client memory and provides precise progress, song identity, artwork, and lyrics.
/// </summary>
public sealed class NetEaseMediaProvider : IMediaSourceProvider
{
    private const string MemoryPlayerSourceId = "cloudmusic";
    private const string NetEaseWindowClass = "OrpheusBrowserHost";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(233);

    /// <summary>
    /// 歌词缓存的容量：来源变多以后必须封顶，否则长时间播放会一直堆积解析结果。
    /// Capacity of the lyric cache: with more sources it has to be capped, otherwise long playback keeps accumulating parsed results.
    /// </summary>
    private const int LyricsCacheCapacity = 64;

    private readonly Dispatcher _dispatcher;
    private readonly LyricsService _lyricsService;
    private readonly Dictionary<string, BitmapImage?> _artworkCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingArtwork = new(StringComparer.OrdinalIgnoreCase);
    private readonly LruCache<string, LyricsResult?> _lyricsCache = new(LyricsCacheCapacity);
    private readonly HashSet<string> _pendingLyrics = new(StringComparer.Ordinal);

    /// <summary>缓存代次：取词设置变化时自增，让仍在飞行中的结果写不回来。/ Cache generation: incremented when retrieval settings change, so an in-flight result cannot be written back.</summary>
    private int _lyricsCacheGeneration;
    private CancellationTokenSource? _cancellation;
    private NetEase? _memoryPlayer;
    private PlayerInfo? _currentInfo;
    private MediaSnapshot _sessionSnapshot = MediaSnapshot.Disconnected;
    private int _version;
    private bool _isDisposed;

    public event Action<IMediaSourceProvider, MediaSnapshot?>? SnapshotChanged;

    /// <summary>
    /// 创建网易云来源提供器；实际进程读取在显式启动后进行，并由本实例负责释放。
    /// Creates the NetEase source provider; process reading begins only after explicit start and is owned by this instance.
    /// </summary>
    public NetEaseMediaProvider(LyricsService lyricsService)
    {
        _dispatcher = Application.Current.Dispatcher;
        _lyricsService = lyricsService;
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>
    /// 取词相关设置变化时清空缓存：本提供器每 233 毫秒轮询一次，因此下一次轮询会用新设置重新取词。
    /// Clears the cache after a retrieval-related settings change: this provider polls every 233 ms, so the next poll refetches with
    /// the new settings.
    /// </summary>
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!LyricsCacheInvalidationPolicy.ShouldClearCache(e.PropertyName, e.ResetScope))
        {
            return;
        }

        _lyricsCache.Clear();
        _lyricsCacheGeneration++;
    }

    /// <summary>
    /// 判断来源标识是否属于网易云音乐。
    /// Determines whether the source identifier belongs to NetEase Cloud Music.
    /// </summary>
    public bool CanHandle(string sourceId) =>
        sourceId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("163music", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 更新 SMTC 基线快照，供来源专用数据合并时保持媒体身份一致。
    /// Updates the SMTC baseline snapshot used to preserve media identity during source-specific enrichment.
    /// </summary>
    public void UpdateSessionSnapshot(MediaSnapshot snapshot)
    {
        _sessionSnapshot = snapshot;
    }

    /// <summary>
    /// 幂等启动来源轮询；重复调用不会创建额外计时器。
    /// Idempotently starts source polling without creating additional timers on repeated calls.
    /// </summary>
    public void Start()
    {
        if (_isDisposed || _cancellation is not null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _ = PollAsync(cancellation, cancellation.Token);
    }

    /// <summary>
    /// 停止轮询并释放当前播放器读取器，阻止释放后继续发布快照。
    /// Stops polling and disposes the active player reader so no snapshots are published after disposal.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _cancellation?.Cancel();
        _memoryPlayer?.Dispose();
        _memoryPlayer = null;
    }

    private async Task PollAsync(CancellationTokenSource cancellation, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                PlayerInfo? playerInfo = null;
                try
                {
                    playerInfo = ReadMemoryPlayerInfo();
                }
                catch
                {
                    ResetMemoryPlayer();
                }

                if (_isDisposed)
                {
                    return;
                }

                if (playerInfo is { } info && ShouldUseMemoryPlayerInfo(info))
                {
                    PublishPlayerInfo(info, token);
                }
                else
                {
                    PublishSnapshot(null);
                }

                await Task.Delay(PollInterval, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private PlayerInfo? ReadMemoryPlayerInfo()
    {
        var hwnd = NativeMethods.FindWindow(NetEaseWindowClass, null);
        if (hwnd == IntPtr.Zero)
        {
            ResetMemoryPlayer();
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId <= 0)
        {
            ResetMemoryPlayer();
            return null;
        }

        if (_memoryPlayer is null || !_memoryPlayer.Validate(processId))
        {
            _memoryPlayer?.Dispose();
            _memoryPlayer = new NetEase(processId);
        }

        return _memoryPlayer.GetPlayerInfo();
    }

    private void ResetMemoryPlayer()
    {
        _memoryPlayer?.Dispose();
        _memoryPlayer = null;
        _currentInfo = null;
        _version++;
    }

    private bool ShouldUseMemoryPlayerInfo(PlayerInfo playerInfo)
    {
        if (!playerInfo.Pause)
        {
            return true;
        }

        return !_sessionSnapshot.IsConnected ||
            CanHandle(_sessionSnapshot.SourceId) ||
            string.Equals(
                _sessionSnapshot.SourceName,
                MediaSourceNameFormatter.GetDisplayName(MemoryPlayerSourceId, Translations.Get("Service.MediaSource.Unknown")),
                StringComparison.OrdinalIgnoreCase);
    }

    private void PublishPlayerInfo(PlayerInfo info, CancellationToken token)
    {
        var version = _currentInfo is { } current &&
            string.Equals(current.Identity, info.Identity, StringComparison.Ordinal) &&
            string.Equals(current.Cover, info.Cover, StringComparison.Ordinal)
                ? _version
                : ++_version;
        _currentInfo = info;

        ImageSource? artwork = null;
        if (_artworkCache.TryGetValue(info.Cover, out var cachedArtwork))
        {
            artwork = cachedArtwork;
        }
        else if (!string.IsNullOrWhiteSpace(info.Cover) && _pendingArtwork.Add(info.Cover))
        {
            _ = LoadArtworkAsync(info.Cover, version, token);
        }

        var hasCachedLyrics = _lyricsCache.TryGetValue(info.Identity, out var lyrics);
        var shouldLoadLyrics = !hasCachedLyrics && _pendingLyrics.Add(info.Identity);
        PublishSnapshot(CreateSnapshot(info, artwork, lyrics));
        if (shouldLoadLyrics)
        {
            _ = LoadLyricsAsync(info, token);
        }
    }

    private MediaSnapshot CreateSnapshot(PlayerInfo info, ImageSource? artwork, LyricsResult? lyrics) =>
        new(
            true,
            !info.Pause,
            false,
            false,
            false,
            info.Title,
            info.Artists,
            MemoryPlayerSourceId,
            MediaSourceNameFormatter.GetDisplayName(MemoryPlayerSourceId, Translations.Get("Service.MediaSource.Unknown")),
            artwork,
            lyrics,
            info.Schedule,
            info.Duration,
            false,
            false,
            MediaRepeatMode.Unavailable,
            1,
            DateTimeOffset.UtcNow);

    private void PublishSnapshot(MediaSnapshot? snapshot)
    {
        if (_isDisposed || _dispatcher.HasShutdownStarted)
        {
            return;
        }

        _dispatcher.BeginInvoke(
            () => SnapshotChanged?.Invoke(this, snapshot),
            DispatcherPriority.Background);
    }

    private async Task LoadArtworkAsync(string coverUrl, int version, CancellationToken token)
    {
        try
        {
            var artwork = await ArtworkLoader.GetImageFromUrlAsync(coverUrl, token);
            _artworkCache[coverUrl] = artwork;
            if (artwork is not null && !_isDisposed && version == _version &&
                _currentInfo is { } info && string.Equals(info.Cover, coverUrl, StringComparison.OrdinalIgnoreCase))
            {
                _lyricsCache.TryGetValue(info.Identity, out var lyrics);
                PublishSnapshot(CreateSnapshot(info, artwork, lyrics));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _artworkCache[coverUrl] = null;
        }
        finally
        {
            _pendingArtwork.Remove(coverUrl);
        }
    }

    private async Task LoadLyricsAsync(PlayerInfo info, CancellationToken token)
    {
        var generation = _lyricsCacheGeneration;
        try
        {
            var request = new LyricsRequest(info.Title, info.Artists, info.Album, info.Duration, info.Identity);
            var result = await _lyricsService.GetLyricsAsync(request, token);

            // 取词过程中设置若被改过，这次结果已经不属于当前配置，写入只会让用户以为设置没生效。
            // If the settings changed while this retrieval ran, the result no longer belongs to the current configuration and
            // writing it would only make the setting look ineffective.
            if (generation == _lyricsCacheGeneration)
            {
                _lyricsCache.Set(info.Identity, result);
            }

            if (!_isDisposed && _currentInfo is { } current &&
                string.Equals(current.Identity, info.Identity, StringComparison.Ordinal))
            {
                _artworkCache.TryGetValue(current.Cover, out var artwork);
                PublishSnapshot(CreateSnapshot(current, artwork, result));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (generation == _lyricsCacheGeneration)
            {
                _lyricsCache.Set(info.Identity, null);
            }
        }
        finally
        {
            _pendingLyrics.Remove(info.Identity);
        }
    }

}
