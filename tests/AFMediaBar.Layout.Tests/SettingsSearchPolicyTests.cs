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
        // 显示模式页按模式切换内容，因此序号以「页面 + 模式分区」为单位计算。
        // Group indices must line up one-to-one with the declaration order of the SettingsGroup containers.
        // A hole means a group was added or removed in XAML without updating the index, and a search hit would
        // jump to the wrong group. The display-mode page swaps content by mode, so the unit is page plus mode.
        foreach (var group in SettingsSearchIndex.Entries.GroupBy(entry => (entry.Page, entry.Mode)))
        {
            var indices = group.Select(entry => entry.GroupIndex).OrderBy(index => index).ToArray();
            CollectionAssert.AreEqual(
                Enumerable.Range(0, indices.Length).ToArray(),
                indices,
                $"{group.Key} 的分组序号必须从 0 连续排列。");
        }
    }

    /// <summary>
    /// 显示模式页每种模式都必须从「选择显示模式」开始：分组序号是标签条里的位置，
    /// 而标签条永远以模式选择为第一项，漏掉它会让所有命中都偏移一格。
    /// Every mode on the display-mode page must start with the mode picker: a group index is a position in the tab
    /// strip, and the strip always begins with the picker, so omitting it would shift every hit by one.
    /// </summary>
    [TestMethod]
    public void Index_DisplayModePickerIsTheFirstGroupInEveryMode()
    {
        foreach (var mode in Enum.GetValues<SettingsSearchMode>())
        {
            var picker = SettingsSearchIndex.Entries.SingleOrDefault(entry =>
                entry.Page == SettingsPageKey.DisplayModes && entry.Mode == mode && entry.GroupIndex == 0);

            Assert.AreEqual("选择显示模式", picker.Title, $"{mode} 模式下序号 0 必须是模式选择分组。");
        }
    }

    /// <summary>
    /// 每种模式下索引覆盖的分组数必须与页面实际渲染的分组数一致：
    /// 任务栏是「模式选择 + 五个分组」，灵动岛是「模式选择 + 一个分组」，另外两种只有模式选择。
    /// The number of groups the index covers per mode must match what the page actually renders: taskbar is the
    /// picker plus five groups, the island is the picker plus one, and the other two are the picker alone.
    /// </summary>
    [TestMethod]
    public void Index_DisplayModeGroupCountsMatchThePageSections()
    {
        (SettingsSearchMode Mode, int Groups)[] expected =
        [
            (SettingsSearchMode.Taskbar, 6),
            (SettingsSearchMode.DynamicIsland, 2),
            (SettingsSearchMode.DesktopCard, 1),
            (SettingsSearchMode.FloatingBall, 1),
        ];

        foreach (var (mode, groups) in expected)
        {
            var count = SettingsSearchIndex.Entries.Count(entry =>
                entry.Page == SettingsPageKey.DisplayModes && entry.Mode == mode);

            Assert.AreEqual(groups, count, $"{mode} 模式下的分组数必须与页面分区一致。");
        }
    }

    [TestMethod]
    public void Index_CoversEveryPageExactlyOncePerGroup()
    {
        var pages = SettingsSearchIndex.Entries.Select(entry => entry.Page).Distinct().ToArray();

        CollectionAssert.AreEquivalent(Enum.GetValues<SettingsPageKey>(), pages);
        foreach (var group in SettingsSearchIndex.Entries.GroupBy(entry => (entry.Page, entry.Mode, entry.GroupIndex)))
        {
            Assert.AreEqual(1, group.Count(), $"{group.Key} 出现了重复条目。");
        }
    }

    [TestMethod]
    public void Index_EveryEntryIsSearchableByItsOwnTitleAndPageTitle()
    {
        foreach (var entry in SettingsSearchIndex.Entries)
        {
            // 同一分组的索引在显示模式页上按模式各有一条（序号只在各自模式内成立），而候选列表会去重，
            // 因此同名条目只要求「同名同序号的一条命中出现」，只有独此一份的条目才要求模式也吻合。
            // A group on the display-mode page has one index entry per mode, because an index only holds inside its
            // own mode, while the suggestion list de-duplicates. So a name shared by several entries only has to
            // appear once under that name, and only a uniquely named entry must also match the mode.
            var sharesTitle = SettingsSearchIndex.Entries.Count(candidate =>
                candidate.Page == entry.Page && candidate.Title == entry.Title) > 1;

            var byGroup = SettingsSearchPolicy.Search(entry.Title, SettingsSearchIndex.Entries);
            Assert.IsTrue(
                byGroup.Any(hit => hit.Page == entry.Page && hit.GroupIndex == entry.GroupIndex && (sharesTitle || hit.Mode == entry.Mode)),
                $"索引条目“{entry.Title}”无法被自己的分组名搜到。");

            var byPage = SettingsSearchPolicy.Search(entry.PageTitle, SettingsSearchIndex.Entries);
            Assert.IsTrue(
                byPage.Any(hit => hit.Page == entry.Page && hit.Title == entry.Title && (sharesTitle || hit.Mode == entry.Mode)),
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

    /// <summary>
    /// 界面改名之后，旧说法必须仍然搜得到。
    ///
    /// 这些词出现在 README、旧截图和用户的记忆里（承载模式、修饰键、字重、信息密度…）。
    /// 只改标题而不把旧词留进关键词，会让按旧词搜索的人一无所获，改名于是变成一次功能倒退。
    /// After the interface is reworded, the previous terms must still be findable. They appear in the README,
    /// in old screenshots, and in users' memory. Renaming titles without keeping the old words as keywords would
    /// make those searches return nothing, turning a wording change into a capability regression.
    /// </summary>
    [TestMethod]
    public void Index_KeepsEveryRetiredTermSearchable()
    {
        (string Term, SettingsPageKey Page)[] retiredTerms =
        [
            ("承载模式", SettingsPageKey.DisplayModes),
            ("承载显示器", SettingsPageKey.DisplayModes),
            ("承载与位置", SettingsPageKey.DisplayModes),
            ("固定长度", SettingsPageKey.DisplayModes),
            ("修饰键", SettingsPageKey.Interaction),
            ("共用修饰键", SettingsPageKey.Interaction),
            ("字重", SettingsPageKey.Appearance),
            ("播放器文字", SettingsPageKey.Appearance),
            // 灵动岛外观搬到了显示模式页，因此这两个旧词现在应当命中显示模式，而不是外观。
            // The island appearance moved to the display-mode page, so these two retired terms must now resolve to
            // display modes rather than appearance.
            ("灵动岛表面", SettingsPageKey.DisplayModes),
            ("基础表面风格", SettingsPageKey.DisplayModes),
        ];

        foreach (var (term, page) in retiredTerms)
        {
            var hits = SettingsSearchPolicy.Search(term, SettingsSearchIndex.Entries);
            Assert.IsTrue(
                hits.Any(hit => hit.Page == page),
                $"改名后的旧词「{term}」必须仍然能搜到 {page} 页，否则按旧说法搜索的人会一无所获。");
        }
    }

    /// <summary>界面不再使用的内部说法不能出现在任何索引标题里，只能留在关键词中。/ Internal wording the interface no longer uses must not appear as an index title, only among keywords.</summary>
    [TestMethod]
    public void Index_TitlesDoNotUseInternalWording()
    {
        string[] banned = ["承载", "修饰键", "字重", "表面"];

        foreach (var entry in SettingsSearchIndex.Entries)
        {
            foreach (var word in banned)
            {
                Assert.IsFalse(
                    entry.Title.Contains(word, StringComparison.Ordinal),
                    $"索引标题「{entry.Title}」仍在用内部说法「{word}」，它只能出现在关键词里。");
            }
        }
    }
}
