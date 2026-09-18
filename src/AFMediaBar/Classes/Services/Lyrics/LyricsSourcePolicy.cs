using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 按用户的来源设置挑出本次取词要用的提供器：启用了哪些、按什么顺序。
/// Picks the providers one retrieval uses according to the user's source settings: which are enabled and in what order.
///
/// 三条规则：
/// 1. 从未配置（null）表示"全部来源按默认顺序"——旧设置文件与新增来源都能自动生效；
/// 2. 空数组表示用户明确关掉了全部来源，此时不返回任何提供器（一次网络请求都不发）；
/// 3. 非空数组的顺序就是优先级，不做二次排序，用户把某个来源排到最前就是这个意思；未知 id 直接忽略（它来自旧设置文件或
///    已删除的来源），因此它不会让整条链失效。
/// Three rules: never configured (null) means every source in the default order, so an old settings file and a newly added source
/// both keep working; an empty array means the user turned every source off, so no provider is returned and not a single request is
/// sent; and a non-empty array's order is the priority and is never re-sorted, since putting a source first means exactly that,
/// while an unknown id is ignored (it comes from an old settings file or a removed source) rather than invalidating the chain.
/// </summary>
public static class LyricsSourcePolicy
{
    /// <summary>
    /// 解析本次取词要用的提供器。
    /// Resolves the providers one retrieval uses.
    /// </summary>
    /// <param name="providers">全部提供器（组合根构造的默认顺序）/ Every provider in the default order built at the composition root.</param>
    /// <param name="settings">用户的来源设置 / The user's source settings.</param>
    /// <returns>启用的提供器，按用户顺序；用户关掉全部来源时为空 / The enabled providers in the user's order, empty when every source is off.</returns>
    public static IReadOnlyList<ILyricsProvider> ResolveActive(
        IReadOnlyList<ILyricsProvider> providers,
        LyricsSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(providers);
        if (providers.Count == 0)
        {
            return [];
        }

        var enabledIds = settings.Normalize().EnabledSourceIds;
        if (enabledIds is null)
        {
            return providers;
        }

        if (enabledIds.Count == 0)
        {
            return [];
        }

        var byId = new Dictionary<string, ILyricsProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            byId.TryAdd(provider.SourceName, provider);
        }

        var active = new List<ILyricsProvider>(enabledIds.Count);
        foreach (var id in enabledIds)
        {
            if (byId.TryGetValue(id, out var provider) && !active.Contains(provider))
            {
                active.Add(provider);
            }
        }

        return active;
    }
}
