namespace AFMediaBar.Classes.Services.Credits;

/// <summary>
/// 名单缓存的时效判定。
/// The freshness rule for the cached name lists.
///
/// 为什么要缓存而不是每次进页面都取：贡献者接口未认证时限流 60 次/小时/IP，而"关于"页是用户随手点开的地方；
/// 一次限流就意味着那一页会显示失败，直到下一个小时。
/// Why cache instead of fetching every time the page opens: the contributors API is rate-limited to 60 requests per hour per IP without a
/// token, while the about page is something the user opens casually; one rate limit would leave that page showing a failure until the next
/// hour.
/// </summary>
public static class CreditsCachePolicy
{
    /// <summary>
    /// 缓存有效期 24 小时。名单的变化以"周"为单位（有人提了 PR、有人赞助），因此一天一次足够；
    /// 手动刷新（页面上的重试）不受它限制，用户想立刻看到变化时不该被自己的缓存挡住。
    /// The cache lives for 24 hours. These lists change on the scale of weeks — somebody merges a pull request, somebody sponsors — so once a day
    /// is plenty, while a manual refresh from the page is not held back by it: a user who wants to see a change now should not be blocked by
    /// their own cache.
    /// </summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// 是否可以直接用缓存（在有效期内且确实有缓存时间）。
    ///
    /// 「未来时间戳」按过期处理：系统时钟被往前调、或缓存文件被手工改过时，若只判断"经过时间 < 有效期"，
    /// 负数也满足，于是这份缓存会永远不再更新。宁可多取一次，也不要让它卡住。
    /// Whether the cache may be used as-is: it is within its lifetime and actually carries a timestamp.
    ///
    /// A timestamp in the future counts as expired: when the system clock is moved forward or the cache file is edited by hand, testing only
    /// "elapsed &lt; lifetime" is satisfied by a negative number as well, which would leave that cache never updating again. One extra fetch beats a
    /// cache that is stuck.
    /// </summary>
    /// <param name="fetchedUtc">缓存写入时间；没有缓存时为 null。/ When the cache was written, or null when there is none.</param>
    /// <param name="nowUtc">当前时间。/ The current time.</param>
    /// <returns>可直接使用时为 true。/ True when the cache may be used directly.</returns>
    public static bool IsFresh(DateTimeOffset? fetchedUtc, DateTimeOffset nowUtc)
    {
        if (fetchedUtc is not { } fetched)
        {
            return false;
        }

        var elapsed = nowUtc - fetched;
        return elapsed >= TimeSpan.Zero && elapsed < CacheLifetime;
    }

    /// <summary>
    /// 这一次是否应该联网取。
    /// Whether a network fetch should happen this time.
    /// </summary>
    /// <param name="fetchedUtc">缓存写入时间；没有缓存时为 null。/ When the cache was written, or null when there is none.</param>
    /// <param name="nowUtc">当前时间。/ The current time.</param>
    /// <param name="isManual">是否由用户手动触发（按钮）。/ Whether the user triggered this manually, from a button.</param>
    /// <returns>应该联网时为 true。/ True when a fetch should happen.</returns>
    public static bool ShouldFetch(DateTimeOffset? fetchedUtc, DateTimeOffset nowUtc, bool isManual) =>
        isManual || !IsFresh(fetchedUtc, nowUtc);
}
