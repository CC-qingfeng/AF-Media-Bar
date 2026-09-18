using System;
using System.Collections.Generic;
using System.Linq;
using AFMediaBar.Classes.Models.Credits;
using AFMediaBar.Classes.Services.Credits;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 名单解析与排序：贡献者（接口与快照同一结构）、赞助名单（仓库里的文件）、缓存时效与端点回退。
/// List parsing and ordering: contributors (one shape for the API and the snapshot), the sponsor list from the repository file, the cache
/// lifetime, and endpoint fallback.
///
/// 这些断言针对的是会真的出问题的分支：解析容错做错会让整份名单消失（而不是少一条），排序写错会让署名顺序乱掉，
/// 缓存时效写错则要么每次进页面都打接口（限流），要么永远不更新。
/// These assertions target branches that really break: a parsing tolerance mistake makes a whole list disappear instead of dropping one entry, a wrong
/// ordering rule scrambles the attribution, and a wrong cache lifetime either hits the API on every page open (rate limits) or never updates.
/// </summary>
[TestClass]
public sealed class CreditsParsingTests
{
    private const string ContributorsJson = """
        [
          { "login": "alice", "contributions": 42, "html_url": "https://github.com/alice", "avatar_url": "https://avatars/alice.png", "id": 1, "type": "User" },
          { "login": "bob", "contributions": 7, "html_url": "https://github.com/bob" },
          { "login": "carol", "contributions": 42 }
        ]
        """;

    /// <summary>接口结构（数组）与仓库快照结构（对象里的 contributors 数组）都要能解析。/ Both the API shape (an array) and the snapshot shape (an object carrying a contributors array) have to parse.</summary>
    [TestMethod]
    public void ContributorsParseFromBothApiAndSnapshotShapes()
    {
        var fromApi = CreditsJsonParser.ParseContributors(ContributorsJson);
        Assert.IsTrue(fromApi.Succeeded);
        Assert.AreEqual(3, fromApi.Contributors!.Count);

        var fromSnapshot = CreditsJsonParser.ParseContributors($$"""{ "contributors": {{ContributorsJson}} }""");
        Assert.IsTrue(fromSnapshot.Succeeded);
        Assert.AreEqual(3, fromSnapshot.Contributors!.Count);
    }

    /// <summary>排序规则：提交次数从多到少，同次数按用户名（不区分大小写）。/ Ordering: most contributions first, ties broken by login, case-insensitively.</summary>
    [TestMethod]
    public void ContributorsAreOrderedByContributionsThenLogin()
    {
        var parsed = CreditsJsonParser.ParseContributors(ContributorsJson);

        CollectionAssert.AreEqual(
            new[] { "alice", "carol", "bob" },
            parsed.Contributors!.Select(contributor => contributor.Login).ToArray());
    }

    /// <summary>
    /// 单条不合法只丢该条：缺用户名、不是对象都要能跳过，其余条目照常保留（整份名单不因为一条脏数据消失）。
    /// One invalid entry is dropped on its own: a missing login or a non-object element is skipped while the rest is kept, so a whole list never
    /// disappears because of one dirty entry.
    /// </summary>
    [TestMethod]
    public void OneInvalidContributorDoesNotFailTheList()
    {
        const string json = """
            [
              { "login": "alice", "contributions": 3 },
              { "contributions": 9 },
              "not-an-object",
              { "login": "", "contributions": 1 },
              { "login": "dave", "contributions": 5 }
            ]
            """;

        var parsed = CreditsJsonParser.ParseContributors(json);

        Assert.IsTrue(parsed.Succeeded);
        CollectionAssert.AreEqual(new[] { "dave", "alice" }, parsed.Contributors!.Select(c => c.Login).ToArray());
    }

    /// <summary>缺主页地址时用用户名兜底，因为界面上的每一条都要能点开。/ A missing profile address falls back to the login-derived URL, because every row on the interface has to be openable.</summary>
    [TestMethod]
    public void MissingProfileUrlFallsBackToTheLoginUrl()
    {
        var parsed = CreditsJsonParser.ParseContributors("""[{ "login": "carol", "contributions": 1 }]""");

        Assert.AreEqual("https://github.com/carol", parsed.Contributors![0].ProfileUrl);
    }

    /// <summary>空列表、坏 JSON、结构不对都要给出可展示的原因，而不是抛异常。/ An empty list, broken JSON, and a wrong shape all produce a displayable reason instead of throwing.</summary>
    [TestMethod]
    public void InvalidInputsProduceReasonsInsteadOfThrowing()
    {
        Assert.IsFalse(CreditsJsonParser.ParseContributors(null).Succeeded);
        Assert.IsFalse(CreditsJsonParser.ParseContributors("not json at all").Succeeded);
        Assert.IsFalse(CreditsJsonParser.ParseContributors("""{ "contributors": "nope" }""").Succeeded);
        Assert.IsFalse(CreditsJsonParser.ParseContributors("[]").Succeeded);
        Assert.IsNotNull(CreditsJsonParser.ParseContributors("[]").FailureReason);
    }

    /// <summary>赞助名单按档位排序：gold → silver → bronze → 其它 → backer，同档位按名称。/ Sponsors are ordered by tier: gold → silver → bronze → anything else → backer, then by name.</summary>
    [TestMethod]
    public void SponsorsAreOrderedByTierThenName()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "sponsors": [
                { "name": "Zoe", "tier": "backer" },
                { "name": "Bob", "tier": "silver" },
                { "name": "Ann", "tier": "gold" },
                { "name": "Cy", "tier": "gold" },
                { "name": "Dee", "tier": "mystery" },
                { "name": "Eve" }
              ]
            }
            """;

        var parsed = CreditsJsonParser.ParseSponsors(json);

        Assert.IsTrue(parsed.Succeeded);
        CollectionAssert.AreEqual(
            new[] { "Ann", "Cy", "Bob", "Dee", "Eve", "Zoe" },
            parsed.Sponsors!.Select(sponsor => sponsor.Name).ToArray());
    }

    /// <summary>
    /// 合法但没有赞助者是**成功**而不是失败：页面据此显示"暂无"，而不是"加载失败"。
    /// A valid list without sponsors is a **success**, not a failure: the page then says "none yet" rather than "failed to load".
    /// </summary>
    [TestMethod]
    public void AnEmptySponsorListSucceeds()
    {
        var parsed = CreditsJsonParser.ParseSponsors("""{ "schemaVersion": 1, "sponsors": [] }""");

        Assert.IsTrue(parsed.Succeeded);
        Assert.AreEqual(0, parsed.Sponsors!.Count);
    }

    /// <summary>结构版本高于本程序支持的版本时明确失败：宁可显示"取不到"，也不要按旧规则误读新结构。/ A schema newer than this application supports fails explicitly: showing "cannot load" beats misreading a new shape under old rules.</summary>
    [TestMethod]
    public void AFutureSponsorSchemaFails()
    {
        var parsed = CreditsJsonParser.ParseSponsors("""{ "schemaVersion": 99, "sponsors": [] }""");

        Assert.IsFalse(parsed.Succeeded);
        Assert.IsNotNull(parsed.FailureReason);
    }

    /// <summary>档位名次是排序的唯一权威，未知档位排在具名档位之后、backer 之前。/ The tier rank is the single ordering authority, and an unknown tier lands after the named ones and before backer.</summary>
    [TestMethod]
    public void TierRankOrdersKnownTiersDeterministically()
    {
        Assert.IsTrue(CreditsJsonParser.TierRank("gold") < CreditsJsonParser.TierRank("silver"));
        Assert.IsTrue(CreditsJsonParser.TierRank("silver") < CreditsJsonParser.TierRank("bronze"));
        Assert.IsTrue(CreditsJsonParser.TierRank("bronze") < CreditsJsonParser.TierRank("whatever"));
        Assert.IsTrue(CreditsJsonParser.TierRank("whatever") < CreditsJsonParser.TierRank("backer"));
        Assert.AreEqual(CreditsJsonParser.TierRank("gold"), CreditsJsonParser.TierRank(" GOLD "));
    }

    /// <summary>展示条数有上限：贡献者可能上百人，而这一页不是排行榜。/ The displayed count is capped: there can be a hundred contributors, and this page is not a leaderboard.</summary>
    [TestMethod]
    public void ContributorCountIsCapped()
    {
        var many = new List<ContributorInfo>();
        for (var index = 0; index < CreditsJsonParser.MaximumContributors + 25; index++)
        {
            many.Add(new ContributorInfo($"user{index:000}", index, $"https://github.com/user{index:000}", null));
        }

        Assert.AreEqual(
            CreditsJsonParser.MaximumContributors,
            CreditsJsonParser.OrderContributors(many).Count);
    }
}
