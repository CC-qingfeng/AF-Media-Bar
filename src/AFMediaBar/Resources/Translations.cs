using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Localization.Strings;
using System.Globalization;

namespace AFMediaBar.Resources;

/// <summary>
/// 界面文案的唯一权威。
///
/// 界面有三种取值入口，但它们读的是同一张表，因此不可能出现"设置页已翻译、托盘仍是旧语言"这种分裂：
/// 1. XAML 通过 <c>{DynamicResource Loc.&lt;键&gt;}</c> 取值，键由 <c>LocalizationService</c> 在切换语言时整批重发；
/// 2. 代码通过 <see cref="Get(string)"/> / <see cref="Format(string, object?[])"/> 取值；
/// 3. 纯策略与可复用控件同样用第 2 条——它们拿不到依赖注入，而当前语言是进程级状态（与
///    <see cref="CultureInfo.CurrentUICulture"/> 同类），因此这里是有意的静态入口，唯一写入方是
///    <c>LocalizationService</c>。
///
/// 文案本身按语言存放：<c>Classes/Services/Localization/Strings</c> 下每种语言一个文件
/// （<c>StringsZhHans</c> / <c>StringsZhHant</c> / <c>StringsEn</c>），三份文件的键顺序完全一致，同一行号指的是
/// 同一条文案；翻译、校对与"再加一门语言"都只碰一个文件。三份文件由 <see cref="BuildTable"/> 合并，缺一条时
/// 该语言取空值并回退简体中文——漏一句翻译只表现为该语言少一句，不会让程序打不开。
/// The single authority for interface text.
///
/// The interface reads it through three entries that all resolve against one table, so a split such as "the settings page
/// is translated while the tray still speaks the old language" cannot happen:
/// 1. XAML reads <c>{DynamicResource Loc.&lt;key&gt;}</c>, and <c>LocalizationService</c> republishes every key when the
///    language changes;
/// 2. code reads <see cref="Get(string)"/> or <see cref="Format(string, object?[])"/>;
/// 3. pure policies and reusable controls also use the second entry — they cannot receive dependency injection, and the
///    active language is process-wide state of the same kind as <see cref="CultureInfo.CurrentUICulture"/>. This static
///    entry is therefore deliberate, and <c>LocalizationService</c> is its only writer.
///
/// The text itself is stored per language: one file per language under <c>Classes/Services/Localization/Strings</c>
/// (<c>StringsZhHans</c>, <c>StringsZhHant</c>, <c>StringsEn</c>) with an identical key order, so the same line number means
/// the same string and translating, proofreading, or adding one more language touches exactly one file. The three files are
/// merged by <see cref="BuildTable"/>, and a language missing an entry reads as empty and falls back to simplified Chinese —
/// a gap shows up as one untranslated string rather than an application that will not start.
/// </summary>
public static class Translations
{
    private static readonly Dictionary<string, LocalizedText> Table = BuildTable();
    private static LocalizationLanguage _activeLanguage = LocalizationLanguage.SimplifiedChinese;

    /// <summary>当前生效的界面语言。/ The interface language currently in effect.</summary>
    public static LocalizationLanguage ActiveLanguage => _activeLanguage;

    /// <summary>已登记的文案键；切换语言时按它整批重发 XAML 资源。/ Every registered text key, which the language switch republishes into the XAML resources.</summary>
    public static IReadOnlyCollection<string> Keys => Table.Keys;

    /// <summary>已登记的文案条数。/ Number of registered entries.</summary>
    public static int Count => Table.Count;

    /// <summary>
    /// 按当前语言取一条文案。键不存在时返回键本身：缺失的文案必须在界面上一眼可见，而不是渲染成空白，
    /// 也不是悄悄回退成另一种语言。
    /// Looks one string up in the active language. A missing key returns the key itself: a missing string has to be
    /// visible in the interface at a glance rather than rendered as blank or silently replaced by another language.
    /// </summary>
    /// <param name="key">文案键。/ Text key.</param>
    public static string Get(string key) => Get(key, _activeLanguage);

    /// <summary>按指定语言取一条文案，供测试与解析验证使用。/ Looks a string up in a given language, which tests and resolution checks use.</summary>
    /// <param name="key">文案键。/ Text key.</param>
    /// <param name="language">目标语言。/ Target language.</param>
    public static string Get(string key, LocalizationLanguage language)
    {
        if (string.IsNullOrEmpty(key) || !Table.TryGetValue(key, out var text))
        {
            return key ?? string.Empty;
        }

        var value = language switch
        {
            LocalizationLanguage.TraditionalChinese => text.TraditionalChinese,
            LocalizationLanguage.English => text.English,
            _ => text.SimplifiedChinese,
        };

        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        // 某一语言缺这条文案时回退到简体中文，而不是在界面上留下空白；简体中文也缺（只可能来自手写的语言文件不一致）
        // 时返回键本身，让缺的那一条在界面上一眼可见。
        // A language missing this entry falls back to simplified Chinese instead of leaving a blank, and when simplified
        // Chinese is missing too — which only an inconsistent hand-written language file can produce — the key itself is
        // returned so the gap is visible in the interface.
        return string.IsNullOrEmpty(text.SimplifiedChinese) ? key : text.SimplifiedChinese;
    }

    /// <summary>
    /// 取一条带参数的文案：文案本身在表里写成 <c>{0}</c> 形式，参数在显示时才填入，因此切换语言后同一条状态
    /// 会连同参数一起换成新语言。
    /// Looks up a string with arguments: the text itself is stored in <c>{0}</c> form and the arguments are filled in at
    /// display time, so the same status turns into the new language together with its arguments after a switch.
    /// </summary>
    /// <param name="key">文案键。/ Text key.</param>
    /// <param name="arguments">填入文案的参数。/ Arguments filled into the text.</param>
    public static string Format(string key, params object?[] arguments) =>
        arguments is { Length: > 0 }
            ? string.Format(CultureInfo.CurrentCulture, Get(key), arguments)
            : Get(key);

    /// <summary>
    /// 生效语言变化时发布。它保持 internal：界面代码走 <c>LocalizationService.LanguageChanged</c>（依赖注入的那条路径），
    /// 而这个事件是给**拿不到依赖注入的可复用控件**用的——例如分组标签条把分组标题快照成了自己的标签文本，
    /// 不重建就会在切换语言后一直显示旧语言。
    /// Published when the language in effect changes. It stays internal: interface code uses
    /// <c>LocalizationService.LanguageChanged</c>, the path that comes from dependency injection, while this event exists for
    /// reusable controls that cannot receive injection — the group strip, for example, snapshots group headers into its own tab
    /// text and would keep showing the old language without a rebuild.
    /// </summary>
    internal static event EventHandler? LanguageChanged;

    /// <summary>
    /// 设置当前语言。它只允许 <c>LocalizationService</c> 调用，并且必须与 <see cref="RaiseLanguageChanged"/> 配对：
    /// 服务先写语言、再重发 XAML 资源，最后才通知订阅者，因为订阅者（例如分组标签条）要读的是**已经换过**的
    /// 动态资源值，反过来的顺序会让它们把旧语言重新快照一遍。
    /// Sets the active language. Only <c>LocalizationService</c> may call it, and it has to be paired with
    /// <see cref="RaiseLanguageChanged"/>: the service writes the language, republishes the XAML resources, and only then
    /// notifies subscribers, because a subscriber such as the group strip reads the *already changed* dynamic-resource values
    /// and the opposite order would make it snapshot the old language once more.
    /// </summary>
    /// <param name="language">新的生效语言。/ The new active language.</param>
    internal static void SetActiveLanguage(LocalizationLanguage language) => _activeLanguage = language;

    /// <summary>通知语言变化的订阅者；只由 <c>LocalizationService</c> 在资源重发之后调用。/ Notifies the language-change subscribers; only <c>LocalizationService</c> calls it, after the resources were republished.</summary>
    internal static void RaiseLanguageChanged() => LanguageChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>
    /// 把三份语言文件合并成一张"键 → 三种语言"的表。
    ///
    /// 键取三份文件的并集并按序排列：只有这样"某一语言漏了一条"才是可表示的（那一份取空值、取值时回退简体中文、
    /// 由完整性测试判失败），而不是让静态初始化直接崩掉——崩掉的话用户看到的是程序打不开，而不是少一句翻译。
    /// 语言文件的键顺序与此处一致（都由键排序），因此同一行号在三份文件里指的是同一条文案。
    /// Merges the three language files into one "key to three languages" table.
    ///
    /// The keys are the union of the three files, in order: that is what makes "one language is missing an entry" representable
    /// — the missing language reads as empty, falls back to simplified Chinese at lookup time, and is failed by the completeness
    /// test — instead of letting static initialization crash, where the user would see an application that does not start rather
    /// than one string that is not translated. The language files carry the same key order as this table (all key-sorted), so the
    /// same line number means the same string in every one of them.
    /// </summary>
    private static Dictionary<string, LocalizedText> BuildTable()
    {
        var simplifiedChinese = Build(StringsZhHans.Register);
        var traditionalChinese = Build(StringsZhHant.Register);
        var english = Build(StringsEn.Register);

        var keys = new SortedSet<string>(simplifiedChinese.Keys, StringComparer.Ordinal);
        keys.UnionWith(traditionalChinese.Keys);
        keys.UnionWith(english.Keys);

        var table = new Dictionary<string, LocalizedText>(keys.Count, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            table[key] = new LocalizedText(
                simplifiedChinese.GetValueOrDefault(key, string.Empty),
                traditionalChinese.GetValueOrDefault(key, string.Empty),
                english.GetValueOrDefault(key, string.Empty));
        }

        return table;
    }

    /// <summary>让一份语言文件登记进它自己的表。/ Lets one language file register into its own table.</summary>
    /// <param name="register">该语言文件的登记方法。/ The registration method of that language file.</param>
    private static IReadOnlyDictionary<string, string> Build(Action<LanguageTable> register)
    {
        var table = new LanguageTable();
        register(table);
        return table.Build();
    }
}
