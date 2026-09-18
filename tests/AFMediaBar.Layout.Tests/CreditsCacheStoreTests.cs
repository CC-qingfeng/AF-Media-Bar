using System;
using System.Collections.Generic;
using System.IO;
using AFMediaBar.Classes.Models.Credits;
using AFMediaBar.Classes.Services.Credits;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 名单缓存的读写：结构版本、<c>partial</c> 标记，以及"旧结构一律当作没有缓存"。
///
/// 最后一条来自一次真实故障：升级后程序被旧结构（没有 <c>partial</c> 标记）的缓存挡住整整 24 小时，
/// 用户看到的现象是"换了新版本，赞助者名单还是不出来"。丢掉一份缓存只多取一次，代价远小于此。
/// Reading and writing the name-list cache: its structure version, the <c>partial</c> flag, and the rule that an older structure counts as no cache at all.
///
/// That last rule comes from a real failure: after an upgrade the application was held back for a full 24 hours by a cache in the older structure, which has no
/// <c>partial</c> flag, and the symptom was "I installed the new version and the sponsor list still does not appear". Dropping a cache costs one extra fetch,
/// which is far cheaper.
/// </summary>
[TestClass]
public sealed class CreditsCacheStoreTests
{
    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "afmediabar-credits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>写入后读回：内容、时间戳与 <c>partial</c> 标记都要保留。/ What was written is read back: contents, timestamp, and the <c>partial</c> flag.</summary>
    [TestMethod]
    public void WhatIsWrittenIsReadBack()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new CreditsCacheStore(directory);
            var fetchedUtc = new DateTimeOffset(2026, 9, 19, 8, 30, 0, TimeSpan.Zero);
            var contributors = new List<ContributorInfo> { new("ann", 7, "https://github.com/ann", null) };
            var sponsors = new List<SponsorInfo> { new("张三", "gold", "第一批", null, "2026-03") };

            Assert.IsTrue(store.TryWrite(contributors, sponsors, fetchedUtc, isPartial: true));
            var cached = store.TryRead();

            Assert.IsNotNull(cached);
            Assert.IsTrue(cached!.Partial);
            Assert.AreEqual(fetchedUtc, cached.FetchedUtc);
            Assert.AreEqual(1, cached.Contributors.Count);
            Assert.AreEqual("ann", cached.Contributors[0].Login);
            Assert.AreEqual(1, cached.Sponsors.Count);
            Assert.AreEqual("张三", cached.Sponsors[0].Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>结构版本低于当前版本的文件被忽略（当作没有缓存），因此升级不会被旧缓存挡住。/ A file older than the current structure is ignored, so an upgrade is never held back by it.</summary>
    [TestMethod]
    public void AnOlderStructureCountsAsNoCache()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new CreditsCacheStore(directory);
            File.WriteAllText(
                store.FilePath,
                """
                {
                  "schemaVersion": 1,
                  "fetchedUtc": "2026-09-19T08:30:00+00:00",
                  "contributors": [ { "login": "ann", "contributions": 7 } ],
                  "sponsors": []
                }
                """);

            Assert.IsNull(store.TryRead(), "旧结构的缓存必须被当作不存在。");

            // 用当前版本重写之后就正常读回：内容是空的也算一份有效缓存（"确实没有赞助者"是合法状态）。
            // Once rewritten in the current structure it reads back normally: empty content is still a valid cache, because "there are no sponsors" is a legitimate state.
            Assert.IsTrue(store.TryWrite([], [], DateTimeOffset.UtcNow));
            var rewritten = store.TryRead();
            Assert.IsNotNull(rewritten);
            Assert.IsFalse(rewritten!.Partial);
            Assert.AreEqual(0, rewritten.Sponsors.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>缺失、损坏或没有时间戳的缓存都返回空，而不是抛异常。/ A missing, damaged, or timestamp-less cache returns nothing instead of throwing.</summary>
    [TestMethod]
    public void MissingOrDamagedCachesReturnNothing()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new CreditsCacheStore(directory);
            Assert.IsNull(store.TryRead());

            File.WriteAllText(store.FilePath, "{ not json");
            Assert.IsNull(store.TryRead());

            File.WriteAllText(store.FilePath, """{ "cacheSchemaVersion": 2, "contributors": [], "sponsors": [] }""");
            Assert.IsNull(store.TryRead());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
