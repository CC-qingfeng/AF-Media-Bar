using System.Runtime.InteropServices;
using System.Windows.Media;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Utils;
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
    private const string UnknownSourceName = "未知来源";
    private readonly LyricsService _lyricsService;
    private readonly Dictionary<string, LyricsResult?> _lyricsCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingLyrics = new(StringComparer.Ordinal);

    public event Action? EnrichmentCompleted;

    /// <summary>
    /// 创建快照构建器，并使用歌词服务执行与当前曲目版本绑定的异步补全。
    /// Creates the snapshot builder and uses the lyrics service for asynchronous enrichment tied to the current track version.
    /// </summary>
    public MediaSnapshotBuilder(LyricsService lyricsService)
    {
        _lyricsService = lyricsService;
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
        try
        {
            var duration = (timelineProperties.EndTime - timelineProperties.StartTime).TotalSeconds;
            var request = new LyricsRequest(
                title,
                artist,
                songInfo.AlbumTitle ?? string.Empty,
                duration > 0 ? duration : null,
                NetEaseSongId: null);
            _lyricsCache[key] = await _lyricsService.GetLyricsAsync(request, CancellationToken.None);
        }
        catch
        {
            _lyricsCache[key] = null;
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
