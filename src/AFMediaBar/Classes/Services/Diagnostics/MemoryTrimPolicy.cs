namespace AFMediaBar.Classes.Services;

/// <summary>
/// 触发一次"按需回收"的原因。与档位无关：档位是"系统状态决定该不该省内存"，而这里回答的是"某件事刚做完，现在值得收一遍吗"。
/// Why an on-demand reclaim was requested. This is independent of the prune levels: a level answers "does the system state allow saving memory",
/// while these triggers answer "something just finished, is a reclaim worth it now".
/// </summary>
public enum MemoryTrimTrigger
{
    /// <summary>进入空闲档：没有媒体、用户也已经离开，值得收一遍托管垃圾。/ The idle level was entered: no media and the user is gone, so collecting managed garbage is worth it.</summary>
    IdleLevelEntered = 0,

    /// <summary>设置窗口关闭：它是最大的界面树与缓存持有者，关掉之后回收最划算。/ The settings window closed: it holds the largest visual tree and the most caches, so a reclaim pays off most there.</summary>
    SettingsWindowClosed = 1,

    /// <summary>浮层关闭：完整层、曲目通知等按需窗口，关闭后释放它们持有的封面临时位图。/ A panel closed: on-demand windows such as the full panel or the track notification, whose temporary artwork bitmaps are released afterwards.</summary>
    PanelClosed = 2,

    /// <summary>用户在设置页明确点击"立即压缩物理内存"。/ The user explicitly clicked "compress physical memory now" on the settings page.</summary>
    ManualRequest = 3
}

/// <summary>
/// 回收强度。两档的差别只在"要不要动工作集"，不动的是托管垃圾的回收。
/// How deep a reclaim goes. The two strengths differ only in whether the working set is touched; collecting managed garbage happens either way.
/// </summary>
public enum MemoryTrimStrength
{
    /// <summary>
    /// 温和：后台 GC + 等待终结器，**不碰工作集**。用在"刚关掉一个窗口，但用户随时可能再打开"的场合，
    /// 因为剥离工作集换来的是下一次交互时的硬缺页。
    /// Gentle: a background GC plus a finalizer drain, leaving the working set alone. Used where a window has just closed but the user may reopen it at
    /// any moment, because returning the working set costs a hard page fault on the next interaction.
    /// </summary>
    Gentle = 0,

    /// <summary>
    /// 深度：压缩 GC ×2 + 交还工作集。用在"短时间不会再回来"的场合（设置窗口关闭、用户手动点击）。
    /// Deep: a compacting GC twice over plus returning the working set. Used where the process is unlikely to be needed again right away, such as after
    /// the settings window closes or when the user asks for it.
    /// </summary>
    Deep = 1
}

/// <summary>
/// 按需回收的判定：什么原因该用多大强度，以及多久内只做一次。
/// The on-demand reclaim decision: which strength a reason deserves, and how often it may run.
///
/// 判定刻意留成纯逻辑（无 I/O、无计时器），因为"两次回收之间的最短间隔"和"手动请求可以绕过间隔"是两条会被改坏的规则，
/// 它们必须能被单元测试直接钉住。
/// The decision stays pure logic — no I/O, no timers — because "the shortest interval between two reclaims" and "a manual request may bypass that
/// interval" are exactly the rules that get broken by accident, so they have to be pinned down by unit tests.
/// </summary>
public static class MemoryTrimPolicy
{
    /// <summary>
    /// 两次非手动回收之间的最短间隔。5 秒来自对照项目（StarPie 的 `MemoryOptimizer`）的实测取值：界面连续开关时，
    /// 每次关闭都做一遍 GC 只会白白吃掉 CPU，而 5 秒已足够覆盖"关掉一个窗口再打开另一个"的节奏。
    /// The shortest interval between two non-manual reclaims. Five seconds comes from the behaviour measured in the reference project (StarPie's
    /// `MemoryOptimizer`): while panels are opened and closed in a row, collecting on every close only burns CPU, and five seconds already covers the
    /// pace of closing one window and opening another.
    /// </summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 这一次请求该用多大强度。
    /// Which strength this request deserves.
    /// </summary>
    /// <param name="trigger">触发原因。/ The trigger.</param>
    /// <returns>回收强度。/ The reclaim strength.</returns>
    public static MemoryTrimStrength ResolveStrength(MemoryTrimTrigger trigger) => trigger switch
    {
        // 设置窗口是最大的界面树与缓存持有者，而且关掉之后用户短期内不会一直开关它；手动请求理所当然按最深的来。
        // The settings window holds the largest visual tree and the most caches, and it is not something the user opens and closes in a loop; a manual
        // request obviously goes as deep as it can.
        MemoryTrimTrigger.SettingsWindowClosed => MemoryTrimStrength.Deep,
        MemoryTrimTrigger.ManualRequest => MemoryTrimStrength.Deep,

        // 空闲档与浮层关闭都只收垃圾：空闲档的"省内存"由档位本身（清缓存、降频）负责，工作集交给显示器关闭/睡眠档，
        // 浮层则随时可能被再打开。
        // The idle level and a panel closing only collect garbage: at the idle level the saving comes from the level itself — dropping caches and slowing
        // timers — while the working set is left to the display-off and suspend levels, and a panel may be reopened at any moment.
        _ => MemoryTrimStrength.Gentle
    };

    /// <summary>判断一次请求是否来自用户的明确动作（这类请求不受最小间隔限制）。/ Whether a request comes from an explicit user action, which the minimum interval does not hold back.</summary>
    /// <param name="trigger">触发原因。/ The trigger.</param>
    /// <returns>是否为手动请求。/ Whether it is a manual request.</returns>
    public static bool IsManual(MemoryTrimTrigger trigger) => trigger == MemoryTrimTrigger.ManualRequest;

    /// <summary>
    /// 判断现在是否该执行这次回收。
    /// Decides whether this reclaim should run now.
    /// </summary>
    /// <param name="trigger">触发原因。/ The trigger.</param>
    /// <param name="lastTrimUtc">上一次回收的时间；从未回收过时传 <see langword="default"/>。
    /// When the last reclaim ran, or <see langword="default"/> if there has never been one.</param>
    /// <param name="nowUtc">现在的时间。/ The current time.</param>
    /// <returns>该执行为 true。/ True when it should run.</returns>
    public static bool ShouldTrim(MemoryTrimTrigger trigger, DateTime lastTrimUtc, DateTime nowUtc)
    {
        // 手动请求永远执行：用户点了按钮就要有反应，被 5 秒节流吞掉比多跑一次 GC 更糟。
        // A manual request always runs: clicking the button has to do something, and being swallowed by the five-second throttle is worse than one extra
        // collection.
        if (IsManual(trigger))
        {
            return true;
        }

        if (lastTrimUtc == default)
        {
            return true;
        }

        return nowUtc - lastTrimUtc >= MinimumInterval;
    }
}
