using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Providers.Web.Netease;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 网易云取词的共用实现：按歌曲 id 先取带逐字的新版结果，再退回行级结果，并统一构建歌词文档。
/// The shared NetEase retrieval implementation: it asks for the word-level result first, falls back to the line-level one,
/// and builds the lyric document in one place.
///
/// 网易云来源有两个入口（来源专用提供器按 id 精确取词、通用链上的搜索兜底提供器先搜 id），两者必须取同一份歌词，
/// 因此取词与文档构建都留在这里，两个提供器只负责自己拿到 id 的方式。
/// NetEase has two entry points (the source-specific provider that retrieves by id and the search fallback on the generic
/// chain), and both have to retrieve the same lyrics, so retrieval and document building live here while each provider only
/// owns the way it obtains an id.
///
/// 新版接口的参数取值来自库自身实现，无法在本机验证其服务端语义；因此它只作为"更丰富"的第一优先，取不到或没有内容时
/// 一律退回行级接口，最差情况与升级前完全相同。
/// The new endpoint's parameter values come from the library itself and their server-side semantics cannot be verified here,
/// so it is only the richer first choice: a failure or an empty payload always falls back to the line-level endpoint, which
/// leaves the worst case identical to before this change.
/// </summary>
internal static class NetEaseLyricFetcher
{
    /// <summary>
    /// 按歌曲 id 取词：优先新版（含逐字），失败或为空时退回行级接口。
    /// Retrieves by song id: the word-level endpoint first, then the line-level one when it fails or returns nothing.
    /// </summary>
    public static async Task<LyricResult?> FetchAsync(Api api, string songId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var wordLevel = await api.GetLyricNew(songId);
            if (HasLyricText(wordLevel))
            {
                return wordLevel;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 新版接口不可用不影响行级接口。 / An unavailable new endpoint does not affect the line-level one.
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await api.GetLyric(songId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 把网易云的原始结果构建成歌词文档；没有可用行时返回 null，让兜底链继续。
    /// Builds the lyric document from a raw NetEase result; it returns null without usable lines so the chain continues.
    /// </summary>
    public static LyricsResult? BuildResult(string sourceName, LyricResult? result, LyricsRequest request)
    {
        if (result is null || result.Nolyric)
        {
            return null;
        }

        // 逐字文本优先：它同时包含行文本与音节时间轴，解析器会得到更完整的模型。
        // Word-level text wins: it carries both the line text and the syllable timeline, so the parser yields a fuller model.
        var main = Normalize(result.Yrc?.Lyric) ?? Normalize(result.Lrc?.Lyric);
        if (main is null)
        {
            return null;
        }

        var translation = Normalize(result.Ytlrc?.Lyric) ?? Normalize(result.Tlyric?.Lyric);
        var romanization = Normalize(result.Yromalrc?.Lyric) ?? Normalize(result.Romalrc?.Lyric);
        var document = LyricsTextParser.Parse(main, translation, romanization, request, request.DurationSeconds);
        return document.Lines.Count > 0 ? new LyricsResult(sourceName, document) : null;
    }

    private static bool HasLyricText(LyricResult? result) =>
        result is not null && (Normalize(result.Yrc?.Lyric) is not null || Normalize(result.Lrc?.Lyric) is not null);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
