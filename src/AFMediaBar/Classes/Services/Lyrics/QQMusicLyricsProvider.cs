using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Providers.Web.QQMusic;
using Lyricify.Lyrics.Searchers;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// QQ 音乐歌词源：按曲名与歌手搜索曲目，再取解密后的 QRC 逐字歌词与独立译文。
/// The QQ Music source: searches a track by title and artist, then retrieves the decrypted QRC syllable lyrics plus the
/// separate translation.
///
/// 两条取词路径按可用性依次尝试：新接口按数字歌曲 id 返回解密后的正文，旧接口按 songmid 返回 base64 正文；
/// 两条都拿不到正文时按未命中处理。
/// Two retrieval paths are tried in order: the new endpoint returns decrypted text for the numeric song id, and the legacy
/// endpoint returns base64 text for the songmid; when neither yields text the source counts as a miss.
/// </summary>
public sealed class QQMusicLyricsProvider : ILyricsProvider
{
    private readonly Api _api = new();

    public string SourceName => LyricsSourceCatalog.QQMusic;

    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Artist))
        {
            return null;
        }

        var track = LyricsSearch.ToTrackMetadata(request);
        var minimumMatch = LyricsMatchPolicy.ToMinimumMatch(request.MatchStrictness);
        var match = await LyricsSearch.MatchAsync(track, Searchers.QQMusic, minimumMatch, cancellationToken);
        if (match is not QQMusicSearchResult qq)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var (main, translation) = await FetchLyricAsync(qq, cancellationToken);
        if (string.IsNullOrWhiteSpace(main))
        {
            return null;
        }

        var document = LyricsTextParser.Parse(
            main,
            translation,
            request: request,
            durationSeconds: request.DurationSeconds,
            filterInfoLines: request.FilterInfoLines);
        return document.Lines.Count > 0 ? new LyricsResult(SourceName, document) : null;
    }

    private async Task<(string? Main, string? Translation)> FetchLyricAsync(
        QQMusicSearchResult match,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(match.Id))
        {
            try
            {
                var response = await _api.GetLyricsAsync(match.Id);
                if (!string.IsNullOrWhiteSpace(response?.Lyrics))
                {
                    return (response!.Lyrics, response.Trans);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 新接口失败时继续尝试旧接口。 / A failed new endpoint still leaves the legacy endpoint to try.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(match.Mid))
        {
            return (null, null);
        }

        try
        {
            var legacy = (await _api.GetLyric(match.Mid))?.Decode();
            return (legacy?.Lyric, legacy?.Trans);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (null, null);
        }
    }
}
