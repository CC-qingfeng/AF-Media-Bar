using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 缓存的歌词文档按播放位置选行，产出静置层需要的三处文本。
/// Caches the lyric document and selects the active line by playback position, producing the three texts the rest layer needs.
///
/// 解析已由取词侧完成，这里只做选择：文档不变时不重建任何状态，位置推进只改变行下标。
/// Parsing already happened on the retrieval side, so this only selects: an unchanged document rebuilds nothing, and moving
/// the position only changes the line index.
/// </summary>
public sealed class LyricLinePresenter
{
    private LyricDocument? _document;
    private int _lastIndex = -2;

    /// <summary>
    /// 根据歌词结果和播放位置计算呈现状态。
    /// Computes presentation state from the lyric result and the playback position.
    /// </summary>
    /// <param name="lyrics">歌词结果；为空或没有行时清除状态。/ Lyric result; null or line-less clears the state.</param>
    /// <param name="position">当前播放位置（秒）。/ Current playback position in seconds.</param>
    /// <returns>当前歌词、下一句、同句译文、是否发生变化及当前行对象。/ Current lyric, next line, matching translation, change state, and the active line object.</returns>
    public LyricLineUpdate Update(LyricsResult? lyrics, double position)
    {
        var document = lyrics?.Document;
        if (document is null || document.Lines.Count == 0)
        {
            var cleared = _document is not null || _lastIndex != -2;
            _document = null;
            _lastIndex = -2;
            return new LyricLineUpdate(string.Empty, string.Empty, string.Empty, string.Empty, cleared, null);
        }

        // 快照每次都带着同一个文档实例（取词结果被缓存），因此按引用比较既正确又不产生逐帧的字符串比较。
        // Every snapshot carries the same document instance because retrieval results are cached, so comparing references is
        // both correct and free of per-frame string comparisons.
        var sourceChanged = !ReferenceEquals(_document, document);
        if (sourceChanged)
        {
            _document = document;
            _lastIndex = -2;
        }

        var lines = document.Lines;
        var index = LyricLineIndexPolicy.FindIndex(lines, position);
        var currentLine = index >= 0 ? lines[index] : null;
        var nextText = index >= 0 && index + 1 < lines.Count ? lines[index + 1].Text : string.Empty;
        var changed = sourceChanged || index != _lastIndex;

        _lastIndex = index;
        return new LyricLineUpdate(
            currentLine?.Text ?? string.Empty,
            nextText,
            currentLine?.Translation ?? string.Empty,
            currentLine?.Romanization ?? string.Empty,
            changed,
            currentLine);
    }
}

/// <summary>
/// 歌词行呈现更新结果。
/// The result of one lyric-line presentation update.
/// </summary>
/// <param name="Text">当前句 / The active line.</param>
/// <param name="NextText">下一句，末尾为空 / The next line, empty at the end of the document.</param>
/// <param name="TranslationText">当前句译文，缺失为空 / The active line's translation, empty when absent.</param>
/// <param name="RomanizationText">当前句音译，缺失为空 / The active line's romanization, empty when absent.</param>
/// <param name="Changed">当前句或文档是否变化 / Whether the active line or the document changed.</param>
/// <param name="CurrentLine">当前行对象，用于逐字擦亮；位置早于第一行时为 null / The active line object used for syllable highlighting; null before the first line.</param>
public readonly record struct LyricLineUpdate(
    string Text,
    string NextText,
    string TranslationText,
    string RomanizationText,
    bool Changed,
    LyricLine? CurrentLine);
