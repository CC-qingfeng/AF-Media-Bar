using System.Runtime.InteropServices;
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 从选中的 SMTC 会话构建统一媒体快照，并异步补充歌词。
/// Builds the unified media snapshot from a selected SMTC session and enriches it with lyrics asynchronously.
/// </summary>
public sealed class MediaSnapshotBuilder
{
    /// <summary>
    /// 无法识别来源时的回退名称，按快照构建时的语言取值：它不能是常量，否则切换语言后来源名会停在启动时的语言上。
    /// Fallback name for an unrecognized source, read while the snapshot is built: it cannot be a constant, because a
    /// language switch would otherwise leave the source name in the language the application started in.
    /// </summary>
    private static string UnknownSourceName => Translations.Get("Service.MediaSource.Unknown");

    /// <summary>
    /// 歌词缓存的容量：来源变多以后必须封顶，否则长时间播放会一直堆积解析结果。
    /// Capacity of the lyric cache: with more sources it has to be capped, otherwise long playback keeps accumulating parsed results.
    /// </summary>
    private const int LyricsCacheCapacity = 64;

    private readonly LyricsService _lyricsService;
    private readonly LruCache<string, LyricsResult?> _lyricsCache = new(LyricsCacheCapacity);
    private readonly HashSet<string> _pendingLyrics = new(StringComparer.Ordinal);

    /// <summary>
    /// 缓存代次：设置变化清空缓存时自增，让仍在飞行中的取词结果写不回来（否则它会把按旧设置取到的歌词塞进新缓存）。
    /// Cache generation: incremented when a settings change clears the cache, so a retrieval still in flight cannot write back
    /// (otherwise it would push lyrics fetched under the old settings into the new cache).
    /// </summary>
    private int _lyricsCacheGeneration;

    public event Action? EnrichmentCompleted;

    /// <summary>
    /// 创建快照构建器，并使用歌词服务执行与当前曲目版本绑定的异步补全。
    /// Creates the snapshot builder and uses the lyrics service for asynchronous enrichment tied to the current track version.
    /// </summary>
    public MediaSnapshotBuilder(LyricsService lyricsService)
    {
        _lyricsService = lyricsService;
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>
    /// 取词相关设置变化时清空缓存并立即重取当前曲目。
    /// Clears the cache and immediately refetches the current track after a retrieval-related settings change.
    /// </summary>
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!LyricsCacheInvalidationPolicy.ShouldClearCache(e.PropertyName, e.ResetScope))
        {
            return;
        }

        _lyricsCache.Clear();
        _lyricsCacheGeneration++;
        EnrichmentCompleted?.Invoke();
    }

    /// <summary>
    /// 从一个稳定会话读取不可变媒体快照；异步封面或歌词结果通过补全事件另行发布。
    /// Builds an immutable media snapshot from a stable session; asynchronous artwork or lyrics results are published separately.
    /// </summary>
    public MediaSnapshot? Build(MediaSession? session, bool isStarted)
    {
        if (session is null || !isStarted)
        {
            return MediaSnapshot.Disconnected;
        }

        var controlSession = session.ControlSession;

        // 第三方库可能在选中之后关闭会话并清除 ControlSession；本次构建跳过，关闭事件会安排下一次刷新。
        // The third-party library can close the session and clear ControlSession after selection; skip this build and let
        // the close event schedule the next refresh.
        if (controlSession is null)
        {
            return null;
        }

        var songInfo = TryGetMediaProperties(controlSession);
        if (songInfo is null)
        {
            return null;
        }

        var playbackInfo = controlSession.GetPlaybackInfo();
        var timelineProperties = controlSession.GetTimelineProperties();
        var timelineStart = timelineProperties.StartTime.TotalSeconds;
        var duration = Math.Max(0, (timelineProperties.EndTime - timelineProperties.StartTime).TotalSeconds);
        var position = Math.Clamp(timelineProperties.Position.TotalSeconds - timelineStart, 0, duration > 0 ? duration : double.MaxValue);
        var artwork = ArtworkLoader.GetThumbnail(songInfo.Thumbnail);
        BitmapHelper.GetDominantColors(1);
        var sourceId = controlSession.SourceAppUserModelId ?? string.Empty;
        var title = songInfo.Title ?? string.Empty;
        var artist = songInfo.Artist ?? string.Empty;
        var lyricsKey = $"{session.Id}\u001f{title}\u001f{artist}";
        var lyrics = GetLyrics(lyricsKey, sourceId, title, artist, songInfo, timelineProperties);

        return new MediaSnapshot(
            true,
            playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            playbackInfo.Controls?.IsPlayPauseToggleEnabled ?? false,
            playbackInfo.Controls?.IsPreviousEnabled ?? false,
            playbackInfo.Controls?.IsNextEnabled ?? false,
            title,
            artist,
            sourceId,
            MediaSourceNameFormatter.GetDisplayName(sourceId, UnknownSourceName),
            artwork,
            lyrics,
            position,
            duration,
            duration > 0 && (playbackInfo.Controls?.IsPlaybackPositionEnabled ?? false),
            playbackInfo.Controls?.IsRepeatEnabled ?? false,
            MapRepeatMode(playbackInfo.AutoRepeatMode),
            playbackInfo.PlaybackRate is > 0 ? playbackInfo.PlaybackRate.Value : 1,
            timelineProperties.LastUpdatedTime);
    }

    private static MediaRepeatMode MapRepeatMode(Windows.Media.MediaPlaybackAutoRepeatMode? mode) => mode switch
    {
        Windows.Media.MediaPlaybackAutoRepeatMode.None => MediaRepeatMode.Off,
        Windows.Media.MediaPlaybackAutoRepeatMode.List => MediaRepeatMode.All,
        Windows.Media.MediaPlaybackAutoRepeatMode.Track => MediaRepeatMode.One,
        _ => MediaRepeatMode.Unavailable
    };

    private LyricsResult? GetLyrics(
        string key,
        string sourceId,
        string title,
        string artist,
        GlobalSystemMediaTransportControlsSessionMediaProperties songInfo,
        GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
    {
        if (_lyricsCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        if (_pendingLyrics.Contains(key) ||
            sourceId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
            sourceId.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        _pendingLyrics.Add(key);
        _ = LoadLyricsAsync(key, title, artist, songInfo, timelineProperties);
        return null;
    }

    private async Task LoadLyricsAsync(
        string key,
        string title,
        string artist,
        GlobalSystemMediaTransportControlsSessionMediaProperties songInfo,
        GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
    {
        var generation = _lyricsCacheGeneration;
        try
        {
            var duration = (timelineProperties.EndTime - timelineProperties.StartTime).TotalSeconds;
            var request = new LyricsRequest(
                title,
                artist,
                songInfo.AlbumTitle ?? string.Empty,
                duration > 0 ? duration : null,
                NetEaseSongId: null);
            var result = await _lyricsService.GetLyricsAsync(request, CancellationToken.None);

            // 取词过程中设置若被改过（来源、严格度、署名行过滤），这次结果已经不属于当前配置，写入只会让用户以为设置没生效。
            // If the settings changed while this retrieval ran (sources, strictness, credit filtering), the result no longer belongs
            // to the current configuration and writing it would only make the setting look ineffective.
            if (generation == _lyricsCacheGeneration)
            {
                _lyricsCache.Set(key, result);
            }
        }
        catch
        {
            if (generation == _lyricsCacheGeneration)
            {
                _lyricsCache.Set(key, null);
            }
        }
        finally
        {
            _pendingLyrics.Remove(key);
            EnrichmentCompleted?.Invoke();
        }
    }

    private static GlobalSystemMediaTransportControlsSessionMediaProperties? TryGetMediaProperties(
        GlobalSystemMediaTransportControlsSession controlSession)
    {
        try
        {
            return controlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
        }
        catch (COMException)
        {
            return null;
        }
    }
}
