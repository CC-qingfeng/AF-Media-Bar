using System;
using System.Linq;
using AFMediaBar.Classes.Services.Credits;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 名单的缓存时效与端点回退顺序。
/// The cache lifetime for the name lists and the endpoint fallback order.
///
/// 前者写错的后果是"每次进页面都打 GitHub 接口"（未认证限流 60 次/小时）或"永远不更新"；
/// 后者写错的后果是国内网络下名单整块消失，因此顺序本身值得钉住。
/// A mistake in the former means hitting the GitHub API on every page open (rate-limited to 60 per hour without a token) or never updating, while a
/// mistake in the latter makes the lists vanish entirely on a China network, so the order itself is worth pinning down.
/// </summary>
[TestClass]
public sealed class CreditsSourcePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>有效期内的缓存直接用，过期就要取，手动请求一律要取。/ A cache within its lifetime is used as-is, an expired one is refetched, and a manual request always fetches.</summary>
    [TestMethod]
    public void CacheIsUsedUntilItExpiresAndManualRequestsAlwaysFetch()
    {
        Assert.IsTrue(CreditsCachePolicy.IsFresh(Now - TimeSpan.FromHours(1), Now));
        Assert.IsFalse(CreditsCachePolicy.ShouldFetch(Now - TimeSpan.FromHours(1), Now, isManual: false));

        Assert.IsFalse(CreditsCachePolicy.IsFresh(Now - CreditsCachePolicy.CacheLifetime - TimeSpan.FromMinutes(1), Now));
        Assert.IsTrue(CreditsCachePolicy.ShouldFetch(Now - CreditsCachePolicy.CacheLifetime - TimeSpan.FromMinutes(1), Now, isManual: false));

        Assert.IsTrue(CreditsCachePolicy.ShouldFetch(Now, Now, isManual: true));
    }

    /// <summary>没有缓存时一定要取；缓存时间在未来（时钟被改）时按过期处理，而不是永远不更新。/ With no cache a fetch always happens, and a timestamp in the future (a changed clock) counts as expired instead of never updating again.</summary>
    [TestMethod]
    public void MissingOrFutureTimestampsFetch()
    {
        Assert.IsTrue(CreditsCachePolicy.ShouldFetch(null, Now, isManual: false));
        Assert.IsFalse(CreditsCachePolicy.IsFresh(Now + TimeSpan.FromDays(1), Now));
        Assert.IsTrue(CreditsCachePolicy.ShouldFetch(Now + TimeSpan.FromDays(1), Now, isManual: false));
    }

    /// <summary>缓存有效期是一个有界的取值：太短会打接口限流，太长等于不更新。/ The cache lifetime is bounded: too short hits the API limit and too long means it never updates.</summary>
    [TestMethod]
    public void CacheLifetimeStaysInASaneRange()
    {
        Assert.IsTrue(CreditsCachePolicy.CacheLifetime >= TimeSpan.FromHours(6));
        Assert.IsTrue(CreditsCachePolicy.CacheLifetime <= TimeSpan.FromDays(7));
    }

    /// <summary>raw 地址能转出 jsDelivr 镜像，其它来源转不出来（避免拼出无效地址）。/ A raw address converts to its jsDelivr mirror while other sources do not, which avoids building an invalid address.</summary>
    [TestMethod]
    public void RawAddressesConvertToTheirJsDelivrMirror()
    {
        Assert.AreEqual(
            "https://cdn.jsdelivr.net/gh/Fervent-Tempo/AF-Media-Bar@main/docs/sponsors.json",
            CreditsSourcePolicy.ToJsDelivrMirror(CreditsSourcePolicy.SponsorsUrl));

        Assert.AreEqual(string.Empty, CreditsSourcePolicy.ToJsDelivrMirror("https://example.com/a/b"));
        Assert.AreEqual(string.Empty, CreditsSourcePolicy.ToJsDelivrMirror(null));
        Assert.AreEqual(string.Empty, CreditsSourcePolicy.ToJsDelivrMirror("https://raw.githubusercontent.com/only-owner"));
    }

    /// <summary>
    /// 回退顺序：直连在前、镜像在后；不传加速模板时用内置默认，传空数组表示明确关闭加速。
    /// Fallback order: direct first, mirror second; omitting the templates uses the built-in defaults, and an empty array means acceleration is
    /// explicitly disabled.
    /// </summary>
    [TestMethod]
    public void FallbackOrderStartsDirectThenMirror()
    {
        var withDefaults = CreditsSourcePolicy.BuildListPlan(CreditsSourcePolicy.SponsorsUrl, accelerators: null);
        Assert.IsTrue(withDefaults.Count >= 2);
        Assert.AreEqual(CreditsSourcePolicy.SponsorsUrl, withDefaults[0]);
        Assert.AreEqual(CreditsSourcePolicy.ToJsDelivrMirror(CreditsSourcePolicy.SponsorsUrl), withDefaults[1]);
        Assert.AreEqual(withDefaults.Count, withDefaults.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var withoutAcceleration = CreditsSourcePolicy.BuildListPlan(CreditsSourcePolicy.SponsorsUrl, []);
        Assert.AreEqual(2, withoutAcceleration.Count);
        CollectionAssert.AreEqual(
            new[] { withDefaults[0], withDefaults[1] },
            withoutAcceleration.ToArray());

        Assert.AreEqual(0, CreditsSourcePolicy.BuildListPlan(null, null).Count);
    }

    /// <summary>
    /// 覆盖变量**替换**整份尝试序列，并且被识别为"有覆盖"（服务据此跳过缓存）。
    ///
    /// 后一半是关键：第一次验收时环境变量设了、程序却一次请求都没发——因为 24 小时内的缓存直接命中了。
    /// An override variable **replaces** the whole attempt sequence and is reported as such, which the service uses to skip the cache.
    ///
    /// The second half matters: during the first acceptance run the variable was set while the program made no request at all, because a cache younger than 24
    /// hours simply matched.
    /// </summary>
    [TestMethod]
    public void AnOverrideReplacesThePlanAndIsReported()
    {
        const string variable = CreditsSourcePolicy.SponsorsUrlOverrideVariable;
        var original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Assert.IsFalse(CreditsSourcePolicy.HasAnyOverride);

            Environment.SetEnvironmentVariable(variable, @"E:\temp\sponsors.json");
            Assert.IsTrue(CreditsSourcePolicy.HasAnyOverride);
            CollectionAssert.AreEqual(
                new[] { @"E:\temp\sponsors.json" },
                CreditsSourcePolicy.BuildSponsorsPlan(accelerators: null).ToArray());

            // 覆盖地址不带加速候选，也不追加镜像：验收看到的顺序必须与线上一致。
            // An override carries no accelerators and no mirror is appended: what acceptance sees has to match production order exactly.
            Assert.AreEqual(1, CreditsSourcePolicy.BuildSponsorsPlan(accelerators: null).Count);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }

        Assert.IsFalse(CreditsSourcePolicy.HasAnyOverride);
    }

    /// <summary>
    /// 贡献者接口主机不是 github.com，因此它只有直连一档；快照才走"直连 + 镜像 + 加速"这条链。
    /// The contributors API host is not github.com, so it has exactly one attempt; only the snapshot goes through the "direct + mirror + accelerator"
    /// chain.
    /// </summary>
    [TestMethod]
    public void TheApiHasExactlyOneAttemptAndTheSnapshotHasSeveral()
    {
        var apiPlan = CreditsSourcePolicy.BuildContributorsApiPlan();
        Assert.AreEqual(1, apiPlan.Count);
        Assert.AreEqual(CreditsSourcePolicy.ContributorsApiUrl, apiPlan[0]);
        StringAssert.Contains(apiPlan[0], "api.github.com/repos/");

        var snapshotPlan = CreditsSourcePolicy.BuildListPlan(CreditsSourcePolicy.ContributorsSnapshotUrl, null);
        Assert.IsTrue(snapshotPlan.Count >= 2);
        StringAssert.Contains(snapshotPlan[0], "raw.githubusercontent.com");
    }
}
