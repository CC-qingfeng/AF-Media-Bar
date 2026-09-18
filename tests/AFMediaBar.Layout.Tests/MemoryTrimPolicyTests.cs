using System;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 按需回收的判定：哪个原因用多大强度，以及多久之内只做一次。
/// The on-demand reclaim decision: which strength a reason deserves, and how often a reclaim may run.
/// </summary>
[TestClass]
public sealed class MemoryTrimPolicyTests
{
    /// <summary>
    /// 强度映射：只有"短时间不会再回来"的两条（设置窗口关闭、用户手动点击）才交还工作集，其余只收垃圾。
    /// Strength mapping: only the two reasons where the process will not be needed again right away — the settings window closing and a manual click —
    /// return the working set, while everything else only collects garbage.
    /// </summary>
    [TestMethod]
    public void OnlyTheSettingsWindowAndManualRequestsTrimTheWorkingSet()
    {
        Assert.AreEqual(MemoryTrimStrength.Deep, MemoryTrimPolicy.ResolveStrength(MemoryTrimTrigger.SettingsWindowClosed));
        Assert.AreEqual(MemoryTrimStrength.Deep, MemoryTrimPolicy.ResolveStrength(MemoryTrimTrigger.ManualRequest));

        // 空闲档之后用户可能马上回来；浮层则随时会被再打开：这两条剥离工作集只会换来一次硬缺页。
        // The user may come straight back after the idle level, and a panel is reopened at any moment, so returning the working set would only buy a hard
        // page fault.
        Assert.AreEqual(MemoryTrimStrength.Gentle, MemoryTrimPolicy.ResolveStrength(MemoryTrimTrigger.IdleLevelEntered));
        Assert.AreEqual(MemoryTrimStrength.Gentle, MemoryTrimPolicy.ResolveStrength(MemoryTrimTrigger.PanelClosed));
    }

    /// <summary>手动请求只有一处判定来源。/ A manual request has exactly one source of truth.</summary>
    [TestMethod]
    public void OnlyTheManualTriggerCountsAsManual()
    {
        Assert.IsTrue(MemoryTrimPolicy.IsManual(MemoryTrimTrigger.ManualRequest));
        Assert.IsFalse(MemoryTrimPolicy.IsManual(MemoryTrimTrigger.SettingsWindowClosed));
        Assert.IsFalse(MemoryTrimPolicy.IsManual(MemoryTrimTrigger.PanelClosed));
        Assert.IsFalse(MemoryTrimPolicy.IsManual(MemoryTrimTrigger.IdleLevelEntered));
    }

    /// <summary>从未回收过时立刻执行。/ The first reclaim always runs.</summary>
    [TestMethod]
    public void TheFirstReclaimAlwaysRuns()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

        Assert.IsTrue(MemoryTrimPolicy.ShouldTrim(MemoryTrimTrigger.PanelClosed, default, now));
        Assert.IsTrue(MemoryTrimPolicy.ShouldTrim(MemoryTrimTrigger.IdleLevelEntered, default, now));
    }

    /// <summary>
    /// 非手动请求受最小间隔限制：刚好到间隔算可以执行，差一毫秒不算。
    /// A non-manual request is held back by the minimum interval: exactly at the interval it may run, one millisecond short it may not.
    /// </summary>
    [TestMethod]
    public void NonManualReclaimsRespectTheMinimumInterval()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

        Assert.IsFalse(MemoryTrimPolicy.ShouldTrim(MemoryTrimTrigger.PanelClosed, now, now));
        Assert.IsFalse(MemoryTrimPolicy.ShouldTrim(
            MemoryTrimTrigger.PanelClosed,
            now,
            now + MemoryTrimPolicy.MinimumInterval - TimeSpan.FromMilliseconds(1)));
        Assert.IsTrue(MemoryTrimPolicy.ShouldTrim(
            MemoryTrimTrigger.PanelClosed,
            now,
            now + MemoryTrimPolicy.MinimumInterval));
        Assert.IsTrue(MemoryTrimPolicy.ShouldTrim(
            MemoryTrimTrigger.SettingsWindowClosed,
            now,
            now + MemoryTrimPolicy.MinimumInterval + TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    /// 手动请求绕过节流：用户点了按钮就必须有反应，被 5 秒节流吞掉比多跑一次 GC 更糟。
    /// A manual request bypasses the throttle: clicking the button has to do something, and being swallowed by the five-second throttle is worse than one
    /// extra collection.
    /// </summary>
    [TestMethod]
    public void ManualRequestsBypassTheThrottle()
    {
        var now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

        Assert.IsTrue(MemoryTrimPolicy.ShouldTrim(MemoryTrimTrigger.ManualRequest, now, now));
        Assert.IsTrue(MemoryTrimPolicy.ShouldTrim(
            MemoryTrimTrigger.ManualRequest,
            now,
            now + TimeSpan.FromMilliseconds(1)));
    }

    /// <summary>节流窗口是一个正整数秒的取值：0 会让每次关窗都回收，过大则等于没有按需回收。/ The throttle window is a positive number of seconds: zero would reclaim on every close and a huge value would disable on-demand reclaims.</summary>
    [TestMethod]
    public void MinimumIntervalStaysInASaneRange()
    {
        Assert.IsTrue(MemoryTrimPolicy.MinimumInterval >= TimeSpan.FromSeconds(1));
        Assert.IsTrue(MemoryTrimPolicy.MinimumInterval <= TimeSpan.FromSeconds(30));
    }
}
