using AFMediaBar.Classes.Models.Credits;

namespace AFMediaBar.Classes.Models.Credits;

/// <summary>
/// 一份名单快照：关于页要展示的全部内容，加上它是从哪来、什么时候来的。
/// A name-list snapshot: everything the about page shows, plus where it came from and when.
/// </summary>
/// <param name="Contributors">贡献者。/ Contributors.</param>
/// <param name="Sponsors">赞助者。/ Sponsors.</param>
/// <param name="FromCache">是否来自本地缓存。/ Whether this came from the local cache.</param>
/// <param name="FetchedUtc">数据写入时间；从未取到过时为 null。/ When the data was written, or null when it has never been fetched.</param>
/// <param name="FailureReason">失败原因；有新数据时为 null。/ The failure reason, or null when fresh data arrived.</param>
/// <param name="IsLoading">是否正在取。/ Whether a fetch is in flight.</param>
public sealed record CreditsSnapshot(
    IReadOnlyList<ContributorInfo> Contributors,
    IReadOnlyList<SponsorInfo> Sponsors,
    bool FromCache,
    DateTimeOffset? FetchedUtc,
    string? FailureReason,
    bool IsLoading)
{
    /// <summary>空快照：什么都没取到时使用。/ The empty snapshot, used when nothing has been fetched.</summary>
    public static CreditsSnapshot Empty { get; } =
        new([], [], FromCache: false, FetchedUtc: null, FailureReason: null, IsLoading: false);

    /// <summary>是否拿到了任何可以展示的内容。/ Whether anything showable arrived.</summary>
    public bool HasAny => Contributors.Count > 0 || Sponsors.Count > 0;
}
