using AFMediaBar.Classes.Abstractions;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 组合根使用的歌词提供器序列：顺序即优先级。
/// The lyric provider sequence used by the composition root: the order is the priority.
///
/// 序列放在这里而不是直接写在组合根里，是因为"精确来源在前、模糊搜索在后"是本模块的一条真实不变量，放在这里才能被测试盯住；
/// 组合根仍然只负责把结果注册进容器。
/// The sequence lives here instead of inline in the composition root because "exact sources first, fuzzy searches last" is a
/// real invariant of this module, and only here can a test watch it; the composition root still just registers the result.
/// </summary>
public static class LyricsProviderFactory
{
    /// <summary>
    /// 建立默认提供器序列：网易云按 id 精确取词 → 网易云搜索兜底 → LRCLIB → QQ 音乐 → 酷狗 → 汽水音乐。
    /// Builds the default provider sequence: NetEase by id, the NetEase search fallback, LRCLIB, QQ Music, Kugou, and Soda Music.
    ///
    /// 中文曲库在前是因为它们同时提供译文或逐字时间轴；顺序的权威在 <see cref="LyricsSourceCatalog.DefaultOrder"/>，
    /// 用户可以在设置里改用它；这里只负责按那个顺序构造提供器。
    /// The Chinese catalogues come first because they also carry a translation or a syllable timeline; the authoritative order
    /// lives in <see cref="LyricsSourceCatalog.DefaultOrder"/> and the user may override it in the settings, while this method
    /// only builds the providers in that order.
    /// </summary>
    public static IReadOnlyList<ILyricsProvider> CreateDefault()
    {
        var providers = new Dictionary<string, ILyricsProvider>(StringComparer.Ordinal)
        {
            [LyricsSourceCatalog.NetEase] = new NetEaseLyricsProvider(),
            [LyricsSourceCatalog.NetEaseSearch] = new NetEaseSearchLyricsProvider(),
            [LyricsSourceCatalog.Lrclib] = new LrclibLyricsProvider(),
            [LyricsSourceCatalog.QQMusic] = new QQMusicLyricsProvider(),
            [LyricsSourceCatalog.Kugou] = new KugouLyricsProvider(),
            [LyricsSourceCatalog.SodaMusic] = new SodaMusicLyricsProvider()
        };

        var ordered = new List<ILyricsProvider>(providers.Count);
        foreach (var sourceId in LyricsSourceCatalog.DefaultOrder)
        {
            if (providers.TryGetValue(sourceId, out var provider))
            {
                ordered.Add(provider);
            }
        }

        return ordered;
    }

    /// <summary>
    /// 建立使用默认预算与默认提供器序列的歌词服务。
    /// Creates the lyric service with the default budgets and the default provider sequence.
    /// </summary>
    public static LyricsService CreateDefaultService() =>
        new(LyricsService.DefaultPerSourceBudget, LyricsService.DefaultTotalBudget, [.. CreateDefault()]);
}
