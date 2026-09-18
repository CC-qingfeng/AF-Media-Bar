using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 第二行歌词的取词顺序：**首选项优先，其次按固定顺序回退**（翻译 → 音译 → 下一句），全部为空时第二行不显示。
///
/// 原来只取首选项：选了"译文"而这一句没有译文时，第二行就空着——而用户手里往往同时有音译或下一句。
/// 顺序是显示用的固定顺序，首选项由设置决定，因此"优先显示翻译、其次音译、最后下一句"只是把首选项设为翻译。
/// Priority order of the second lyric line: the **preferred source first, then a fixed fallback chain** (translation, romanization, next
/// line), with the second line hidden when all of them are empty.
///
/// Previously only the preferred source was read, so choosing "translation" left the second line blank whenever that line had no
/// translation — even though a romanization or the next line was available. The order is a fixed display order while the preference comes
/// from the settings, so "prefer the translation, then the romanization, then the next line" is simply the preference set to translation.
/// </summary>
public static class LyricsSecondaryLinePolicy
{
    /// <summary>固定回退顺序：翻译 → 音译 → 下一句。/ The fixed fallback order: translation, romanization, next line.</summary>
    public static readonly IReadOnlyList<LyricsSecondaryLineMode> FallbackOrder =
    [
        LyricsSecondaryLineMode.Translation,
        LyricsSecondaryLineMode.Romanization,
        LyricsSecondaryLineMode.NextLine
    ];

    /// <summary>
    /// 按首选项与固定顺序挑出第二行的内容，全部为空时返回空串（调用方据此隐藏第二行，不留空白占位）。
    /// Picks the second line's content from the preferred source and the fixed order, returning an empty string when all of them are empty so
    /// that the caller hides the row instead of leaving a blank placeholder.
    /// </summary>
    /// <param name="preferred">设置里的首选项。/ Preferred source from the settings.</param>
    /// <param name="nextLine">下一句歌词。/ The next lyric line.</param>
    /// <param name="translation">当前句译文。/ Translation of the active line.</param>
    /// <param name="romanization">当前句音译。/ Romanization of the active line.</param>
    public static string Resolve(
        LyricsSecondaryLineMode preferred,
        string? nextLine,
        string? translation,
        string? romanization)
    {
        if (Pick(preferred, nextLine, translation, romanization) is { Length: > 0 } value)
        {
            return value;
        }

        foreach (var mode in FallbackOrder)
        {
            if (mode == preferred)
            {
                continue;
            }

            if (Pick(mode, nextLine, translation, romanization) is { Length: > 0 } fallback)
            {
                return fallback;
            }
        }

        return string.Empty;
    }

    /// <summary>取某一种来源的文本；空白一律按"没有内容"处理。/ Reads one source's text, treating whitespace as absent.</summary>
    private static string? Pick(
        LyricsSecondaryLineMode mode,
        string? nextLine,
        string? translation,
        string? romanization)
    {
        var value = mode switch
        {
            LyricsSecondaryLineMode.Translation => translation,
            LyricsSecondaryLineMode.Romanization => romanization,
            _ => nextLine
        };

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
