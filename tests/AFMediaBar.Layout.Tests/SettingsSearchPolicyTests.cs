using System;
using System.Collections.Generic;
using System.Linq;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 设置搜索策略与搜索索引的纯逻辑测试。
/// Pure-logic tests for the settings search policy and its index.
/// </summary>
[TestClass]
public sealed class SettingsSearchPolicyTests
{
    private static readonly SettingsSearchEntry[] Sample =
    [
        new(SettingsPageKey.Lyrics, 0, "歌词", "显示", "实时歌词、双行歌词与第二行内容", ["歌词", "lyrics"]),
        new(SettingsPageKey.Lyrics, 1, "歌词", "对齐", "歌词在静置层的对齐方式", ["对齐", "align", "居中"]),
        new(SettingsPageKey.Appearance, 0, "外观", "字体", "西文与中文字体、字重和字号预览", ["字体", "font", "weight"]),
        new(SettingsPageKey.Appearance, 1, "外观", "主题与材质", "应用明暗主题、窗口背景材质与动效状态", ["主题", "theme", "mica"]),
    ];

    [TestMethod]
    public void Search_EmptyOrWhitespaceQuery_ReturnsNothing()
    {
        Assert.AreEqual(0, SettingsSearchPolicy.Search(null, Sample).Count);
        Assert.AreEqual(0, SettingsSearchPolicy.Search(string.Empty, Sample).Count);
        Assert.AreEqual(0, SettingsSearchPolicy.Search("   ", Sample).Count);
    }

    [TestMethod]
    public void Search_EmptyIndex_ReturnsNothing()
    {
        Assert.AreEqual(0, SettingsSearchPolicy.Search("歌词", []).Count);
        Assert.AreEqual(0, SettingsSearchPolicy.Search("歌词", null!).Count);
    }

    [TestMethod]
    public void Search_ExactTitleOutranksKeywordAndDescriptionHits()
    {
        var hits = SettingsSearchPolicy.Search("对齐", Sample);

        Assert.IsTrue(hits.Count >= 1);
        Assert.AreEqual("对齐", hits[0].Title);
        Assert.AreEqual(SettingsPageKey.Lyrics, hits[0].Page);
        Assert.AreEqual(1, hits[0].GroupIndex);
    }

    [TestMethod]
    public void Search_PrefixMatchOutranksSubstringMatch()
    {
        var hits = SettingsSearchPolicy.Search("主题", Sample);

        Assert.AreEqual("主题与材质", hits[0].Title);
    }

    [TestMethod]
    public void Search_MatchesKeywordsAndIsCaseInsensitive()
    {
        var hits = SettingsSearchPolicy.Search("MICA", Sample);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("主题与材质", hits[0].Title);
    }

    [TestMethod]
    public void Search_MatchesDescriptionText()
    {
        var hits = SettingsSearchPolicy.Search("字号预览", Sample);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("字体", hits[0].Title);
    }

    [TestMethod]
    public void Search_UnknownTerm_ReturnsNothing()
    {
        Assert.AreEqual(0, SettingsSearchPolicy.Search("完全不存在的词", Sample).Count);
    }

    [TestMethod]
    public void Search_PageTitle_ListsThatPageGroupsAndOutranksDescriptionHits()
    {
        // 用页面名搜索时，用户想看到的是这一页有哪些分组。原先导航栏的搜索只能匹配菜单项文案，
        // 这里把它扩到分组级，同时保证页面名仍然可用。
        // Searching a page name should list that page's groups. The navigation bar only matched menu labels,
        // so this extends search to groups while keeping page names working.
        var hits = SettingsSearchPolicy.Search("外观", Sample);

        Assert.AreEqual(2, hits.Count);
        Assert.IsTrue(hits.All(hit => hit.Page == SettingsPageKey.Appearance));
        Assert.IsTrue(hits.All(hit => hit.PageTitle == "外观"));
        Assert.AreEqual("字体", hits[0].Title);
    }

    [TestMethod]
    public void Search_HitCarriesThePageTitleForDisplay()
    {
        var hit = SettingsSearchPolicy.Search("对齐", Sample)[0];

        Assert.AreEqual("歌词", hit.PageTitle);
        Assert.AreEqual("对齐", hit.Title);
    }

    [TestMethod]
    public void Search_NeverExceedsTheResultCap()
    {
        var many = Enumerable.Range(0, 40)
            .Select(index => new SettingsSearchEntry(SettingsPageKey.Lyrics, index, "歌词", $"显示 {index}", "歌词", ["歌词"]))
            .ToArray();

        Assert.AreEqual(SettingsSearchPolicy.MaxResults, SettingsSearchPolicy.Search("显示", many).Count);
    }

    [TestMethod]
    public void Search_EmptyKeywordEntries_AreSkippedWithoutLosingTheKeywordMatch()
    {
        var entries = new[]
        {
            new SettingsSearchEntry(SettingsPageKey.Lyrics, 0, "歌词", "显示", "当前句与下一句", [string.Empty, null!, "lyrics"]),
        };

        Assert.AreEqual(1, SettingsSearchPolicy.Search("lyrics", entries).Count);
        Assert.AreEqual(0, SettingsSearchPolicy.Search("zzz", entries).Count, "空关键词不能变成通配符。");
    }

    [TestMethod]
    public void Index_GroupIndicesAreContiguousFromZeroForEveryPage()
    {
        // 分组序号必须与页面里 SettingsGroup 的声明顺序逐一对上。序号出现空洞意味着
        // 有人在 XAML 里增删了分组却没有更新索引，搜索结果就会跳到错误的分组。
        // Group indices must line up one-to-one with the declaration order of the SettingsGroup containers.
        // A hole means a group was added or removed in XAML without updating the index, and a search hit would
        // jump to the wrong group.
        foreach (var group in SettingsSearchIndex.Entries.GroupBy(entry => entry.Page))
        {
            var indices = group.Select(entry => entry.GroupIndex).OrderBy(index => index).ToArray();
            CollectionAssert.AreEqual(
                Enumerable.Range(0, indices.Length).ToArray(),
                indices,
                $"{group.Key} 的分组序号必须从 0 连续排列。");
        }
    }

    [TestMethod]
    public void Index_CoversEveryPageExactlyOncePerGroup()
    {
        var pages = SettingsSearchIndex.Entries.Select(entry => entry.Page).Distinct().ToArray();

        CollectionAssert.AreEquivalent(Enum.GetValues<SettingsPageKey>(), pages);
        foreach (var group in SettingsSearchIndex.Entries.GroupBy(entry => (entry.Page, entry.GroupIndex)))
        {
            Assert.AreEqual(1, group.Count(), $"{group.Key} 出现了重复条目。");
        }
    }

    [TestMethod]
    public void Index_EveryEntryIsSearchableByItsOwnTitleAndPageTitle()
    {
        foreach (var entry in SettingsSearchIndex.Entries)
        {
            var byGroup = SettingsSearchPolicy.Search(entry.Title, SettingsSearchIndex.Entries);
            Assert.IsTrue(
                byGroup.Any(hit => hit.Page == entry.Page && hit.GroupIndex == entry.GroupIndex),
                $"索引条目“{entry.Title}”无法被自己的分组名搜到。");

            var byPage = SettingsSearchPolicy.Search(entry.PageTitle, SettingsSearchIndex.Entries);
            Assert.IsTrue(
                byPage.Any(hit => hit.Page == entry.Page && hit.GroupIndex == entry.GroupIndex),
                $"索引条目“{entry.PageTitle} › {entry.Title}”无法被页面名搜到。");
        }
    }

    [TestMethod]
    public void Index_EveryEntryHasAKeywordADescriptionAndAMatchingPageTitle()
    {
        foreach (var entry in SettingsSearchIndex.Entries)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Title), "索引条目必须有标题。");
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Description), $"“{entry.Title}”缺少说明。");
            Assert.IsTrue(
                entry.Keywords.Any(keyword => !string.IsNullOrWhiteSpace(keyword)),
                $"“{entry.Title}”至少需要一个可搜索关键词。");
            Assert.AreEqual(
                SettingsSearchIndex.GetPageTitle(entry.Page),
                entry.PageTitle,
                $"“{entry.Title}”的页面名必须与页面标题表一致。");
        }
    }

    [TestMethod]
    public void Index_PageTitlesAreNonEmptyAndDistinct()
    {
        var titles = Enum.GetValues<SettingsPageKey>()
            .Select(SettingsSearchIndex.GetPageTitle)
            .ToArray();

        Assert.IsTrue(titles.All(title => !string.IsNullOrWhiteSpace(title)), "每个页面都必须有导航名称。");
        CollectionAssert.AllItemsAreUnique(titles);
    }
}
