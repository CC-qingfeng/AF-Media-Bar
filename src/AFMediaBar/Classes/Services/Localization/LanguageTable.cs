namespace AFMediaBar.Classes.Services.Localization;

/// <summary>
/// 一条界面文案的三种语言取值。
/// The three language values of one interface string.
/// </summary>
/// <param name="SimplifiedChinese">简体中文文案；缺省时的回退目标。/ Simplified-Chinese text, which is also the fallback.</param>
/// <param name="TraditionalChinese">繁体中文文案；为空时回退到简体中文。/ Traditional-Chinese text, falling back to simplified Chinese when empty.</param>
/// <param name="English">英文文案；为空时回退到简体中文。/ English text, falling back to simplified Chinese when empty.</param>
internal readonly record struct LocalizedText(string SimplifiedChinese, string TraditionalChinese, string English);

/// <summary>
/// 一种语言的文案登记表：每份语言文件往这里登记"键 → 该语言的文案"。
///
/// 一条文案在三种语言里各占一行、分布在三份文件里，因此"某一语言漏了一条"是完全可能的，这里的处理是：
/// 键缺失的那一份取下空值，取值时回退到简体中文——
/// 漏翻译会立刻暴露，不会变成界面上少一门语言。重复键与空文案在登记时就抛异常，而不是静默覆盖。
/// The text registry of one language: each language file registers "key → the text in that language" here.
///
/// One string occupies one line in each of the three language files, so "a language is missing an entry" is entirely
/// possible. That case is handled by giving the missing language an empty value which falls back to simplified Chinese at
/// lookup time, so a missing translation surfaces as that one language still reading Chinese instead of as a crash;
/// immediately instead of turning into an interface that speaks one language less. A duplicate key or an empty text throws
/// at registration time rather than overwriting silently.
/// </summary>
internal sealed class LanguageTable
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

    /// <summary>登记一条本语言文案。/ Registers one string of this language.</summary>
    /// <param name="key">文案键，不含 <c>Loc.</c> 前缀。/ Text key without the <c>Loc.</c> prefix.</param>
    /// <param name="text">该语言的文案，不允许为空。/ The text in this language, which may not be empty.</param>
    /// <exception cref="ArgumentException">键或文案为空时抛出。/ Thrown when the key or the text is empty.</exception>
    /// <exception cref="InvalidOperationException">同一份语言文件里键重复时抛出。/ Thrown when the key is already registered in this language file.</exception>
    internal void Add(string key, string text)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A localization key may not be empty.", nameof(key));
        }

        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException($"Localization entry '{key}' has no text in this language.", nameof(text));
        }

        if (!_entries.TryAdd(key, text))
        {
            throw new InvalidOperationException($"Duplicate localization key '{key}'.");
        }
    }

    /// <summary>取出该语言已登记的键值。/ Returns the registered pairs of this language.</summary>
    internal IReadOnlyDictionary<string, string> Build() => _entries;
}


