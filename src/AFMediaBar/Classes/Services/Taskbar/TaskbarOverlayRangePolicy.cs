using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 判断一个"盖在任务栏上"的窗口是不是任务栏自己的一部分，并按主轴换算成占用区间。
///
/// 之所以需要这一层：Windows 11 的任务栏是一整块 XAML 岛，开始按钮、搜索框、任务按钮与托盘都是它的子窗口，UI Automation 从
/// 任务栏句柄就能枚举到全部内容；Windows 10 的左侧一簇（开始、**搜索框**、任务视图）由**别的进程的顶层窗口**叠在任务栏上绘制，
/// 它们既不是任务栏的子窗口（UIA 枚举不到），又盖在我们的媒体栏之上（抢走鼠标输入，媒体栏因此既被盖住也拖不动）。
/// 这一层给出两条判定：顶层覆盖窗口（按 Z 序扫描与命中测试找到）与任务栏自己的子窗口"槽位"（按几何找到，不依赖 UIA）。
/// Decides whether a window lying over the taskbar is part of the taskbar itself, and converts it into a primary-axis occupied range.
///
/// Why this exists: the Windows 11 taskbar is one XAML island whose children are the Start button, the search box, the task buttons
/// and the tray, so UI Automation enumerates all of it from the taskbar handle. On Windows 10 the left cluster (Start, the **search
/// box**, Task View) is drawn by top-level windows of *other processes* stacked over the taskbar: they are not descendants of the
/// taskbar (so UIA never sees them) and they paint over our media bar and swallow its mouse input, which is why the bar is both
/// covered and undraggable there. This layer offers two decisions: a covering top-level window (found by a Z-order scan and by hit
/// testing) and one of the taskbar's own child "slots" (found geometrically, without UIA).
/// </summary>
public static class TaskbarOverlayRangePolicy
{
    /// <summary>
    /// 认定"盖在任务栏上"所需的横轴重叠比例：低于它只算擦到任务栏一角，不计入占用。
    /// Cross-axis overlap ratio required to count as lying over the taskbar: below it the window merely clips a corner.
    /// </summary>
    private const double MinimumCrossOverlapRatio = 0.4;

    /// <summary>
    /// 允许的最大横轴尺寸倍数：搜索框这类任务栏元素的窗口高度与任务栏相当，而开始菜单、搜索结果与桌面窗口要高得多，
    /// 它们不是任务栏的一部分，把它们计入占用会让媒体栏为了一个浮层让位。取 6 倍是留出"搜索宿主窗口比任务栏高一些"的余量。
    /// Maximum cross-axis size as a multiple of the taskbar's: a taskbar element such as the search box has a window about as tall as
    /// the taskbar, while the Start menu, the search results, and the desktop are far taller. They are not part of the taskbar, and
    /// counting them would move the bar out of the way of a flyout. Six times leaves room for a search host window that is somewhat
    /// taller than the taskbar.
    /// </summary>
    private const double MaximumCrossSizeRatio = 6;

    /// <summary>
    /// 允许的最大主轴占比：占掉任务栏大半宽度的窗口（开始菜单、全宽浮层）不是任务栏元素；把它们计入占用会让空闲区间归零，
    /// 媒体栏反而会退回保守区间跳到最左边。
    /// Maximum share of the primary axis: a window taking most of the taskbar's width (the Start menu, a full-width flyout) is not a
    /// taskbar element, and counting it would leave no free range at all, which pushes the bar back to the conservative range at the
    /// far left.
    /// </summary>
    private const double MaximumPrimaryShare = 0.5;

    /// <summary>
    /// 槽位允许的最大主轴占比：容器是覆盖**整条**任务栏的窗口（XAML 岛、任务列表外壳），而槽位本身可以很宽——固定了很多图标时
    /// 任务列表能超过半条任务栏。因此这里的上限只排除接近整条任务栏宽的窗口，明显宽于 UIA 那条 45% 的判据。
    /// Maximum primary share for a slot: a container covers the **whole** taskbar (the XAML island, the task-list shell), while a slot
    /// itself can be wide — with many pinned icons the task list exceeds half a taskbar. The bound therefore only excludes windows
    /// close to the full taskbar width, which is noticeably looser than the 45% rule used for UIA elements.
    /// </summary>
    private const double MaximumSlotPrimaryShare = 0.9;

    /// <summary>认定一个槽位所需的最小主轴长度：更短的是分隔条或零尺寸占位窗口。/ Minimum primary length for a slot: anything shorter is a separator or a zero-sized placeholder.</summary>
    private const int MinimumSlotPrimaryPixels = 8;

    /// <summary>
    /// 判断任务栏**自己的子窗口**是不是一个"槽位"（开始按钮、任务列表、搜索框），并换算成占用区间。
    /// Decides whether one of the taskbar's own child windows is a "slot" and converts it into an occupied range.
    /// </summary>
    /// <param name="windowRect">候选子窗口的矩形（屏幕物理像素）。/ Candidate child window rectangle in physical screen pixels.</param>
    /// <param name="taskbarRect">任务栏矩形（屏幕物理像素）。/ Taskbar rectangle in physical screen pixels.</param>
    /// <param name="orientation">任务栏方向。/ Taskbar orientation.</param>
    /// <param name="range">换算出的占用区间（任务栏内的主轴坐标）。/ Resulting occupied range in taskbar-relative primary-axis coordinates.</param>
    /// <returns>算作槽位时为 true。/ True when the window counts as a slot.</returns>
    public static bool TryResolveSlot(
        RECT windowRect,
        RECT taskbarRect,
        LayoutOrientation orientation,
        out TaskbarPrimaryRange range) =>
        TryResolveSlot(windowRect, taskbarRect, orientation, out range, out _);

    /// <summary>
    /// 判断任务栏自己的子窗口是不是一个"槽位"，并给出被拒原因。
    ///
    /// 这条路径不依赖 UI Automation：Windows 10 的搜索框是一个文本控件，只按按钮类控件过滤时整段都会漏掉；而它的窗口若挂在
    /// 任务栏之下（XAML 岛的子树里），顶层窗口那两条采集也看不到。判定因此只看几何：与任务栏横轴相交、主轴长度落在
    /// [8 像素, 45% 任务栏长度]——整条任务栏宽的容器与零尺寸占位会被报成"不是槽位"，调用方可以据此再往下一层找。
    /// Decides whether one of the taskbar's own child windows is a "slot" and reports the rejection reason.
    ///
    /// This path does not depend on UI Automation: the Windows 10 search box is a text control, so filtering by button-like controls
    /// skips the whole segment, and if its window hangs under the taskbar (inside the XAML island's subtree) neither of the two
    /// top-level collections can see it either. The decision is therefore purely geometric: intersects the taskbar on the cross axis and
    /// measures between 8 pixels and 45% of the taskbar on the primary axis. Full-width containers and zero-sized placeholders are
    /// reported as "not a slot" so the caller can descend one level further.
    /// </summary>
    /// <param name="windowRect">候选子窗口的矩形（屏幕物理像素）。/ Candidate child window rectangle in physical screen pixels.</param>
    /// <param name="taskbarRect">任务栏矩形（屏幕物理像素）。/ Taskbar rectangle in physical screen pixels.</param>
    /// <param name="orientation">任务栏方向。/ Taskbar orientation.</param>
    /// <param name="range">换算出的占用区间（任务栏内的主轴坐标）。/ Resulting occupied range in taskbar-relative primary-axis coordinates.</param>
    /// <param name="rejection">不是槽位时的原因。/ Reason when the window is not a slot.</param>
    /// <returns>算作槽位时为 true。/ True when the window counts as a slot.</returns>
    public static bool TryResolveSlot(
        RECT windowRect,
        RECT taskbarRect,
        LayoutOrientation orientation,
        out TaskbarPrimaryRange range,
        out TaskbarOccupancyRejection rejection)
    {
        range = default;
        rejection = TaskbarOccupancyRejection.None;

        var (primaryLength, crossLength) = ResolveLengths(taskbarRect, orientation);
        if (primaryLength <= 0 || crossLength <= 0)
            return false;

        var (windowStart, windowEnd, windowCrossStart, windowCrossEnd) = ToAxes(windowRect, orientation);
        var (taskbarStart, taskbarEnd, taskbarCrossStart, taskbarCrossEnd) = ToAxes(taskbarRect, orientation);

        var crossOverlap = Math.Min(windowCrossEnd, taskbarCrossEnd) - Math.Max(windowCrossStart, taskbarCrossStart);
        if (crossOverlap < crossLength * MinimumCrossOverlapRatio)
        {
            rejection = TaskbarOccupancyRejection.NotOverTaskbar;
            return false;
        }

        var primarySize = windowEnd - windowStart;
        if (primarySize < MinimumSlotPrimaryPixels)
        {
            rejection = TaskbarOccupancyRejection.TooSmall;
            return false;
        }

        if (primarySize > primaryLength * MaximumSlotPrimaryShare)
        {
            rejection = TaskbarOccupancyRejection.Container;
            return false;
        }

        var overlapStart = Math.Max(windowStart, taskbarStart);
        var overlapEnd = Math.Min(windowEnd, taskbarEnd);
        if (overlapEnd <= overlapStart)
        {
            rejection = TaskbarOccupancyRejection.OutsideTaskbar;
            return false;
        }

        range = new TaskbarPrimaryRange(
            Math.Clamp(overlapStart - taskbarStart, 0, primaryLength),
            Math.Clamp(overlapEnd - taskbarStart, 0, primaryLength));
        return range.End > range.Start;
    }

    /// <summary>
    /// 试着把一个顶层覆盖窗口换算成占用区间。
    /// Tries to convert a covering top-level window into an occupied range.
    /// </summary>
    /// <param name="windowRect">候选顶层窗口的矩形（屏幕物理像素）。/ Candidate top-level window rectangle in physical screen pixels.</param>
    /// <param name="taskbarRect">任务栏矩形（屏幕物理像素）。/ Taskbar rectangle in physical screen pixels.</param>
    /// <param name="orientation">任务栏方向。/ Taskbar orientation.</param>
    /// <param name="range">换算出的占用区间（任务栏内的主轴坐标）。/ Resulting occupied range in taskbar-relative primary-axis coordinates.</param>
    /// <returns>该窗口算作任务栏元素时为 true。/ True when the window counts as part of the taskbar.</returns>
    public static bool TryResolve(
        RECT windowRect,
        RECT taskbarRect,
        LayoutOrientation orientation,
        out TaskbarPrimaryRange range) =>
        TryResolve(windowRect, taskbarRect, orientation, out range, out _);

    /// <summary>
    /// 试着把一个顶层覆盖窗口换算成占用区间，并给出被拒的原因（供日志记录现场）。
    /// Tries to convert a covering top-level window into an occupied range and reports the rejection reason, which the log records on site.
    /// </summary>
    /// <param name="windowRect">候选顶层窗口的矩形（屏幕物理像素）。/ Candidate top-level window rectangle in physical screen pixels.</param>
    /// <param name="taskbarRect">任务栏矩形（屏幕物理像素）。/ Taskbar rectangle in physical screen pixels.</param>
    /// <param name="orientation">任务栏方向。/ Taskbar orientation.</param>
    /// <param name="range">换算出的占用区间（任务栏内的主轴坐标）。/ Resulting occupied range in taskbar-relative primary-axis coordinates.</param>
    /// <param name="rejection">被拒原因；接受时为 <see cref="TaskbarOccupancyRejection.None"/>。/ Rejection reason, <see cref="TaskbarOccupancyRejection.None"/> when accepted.</param>
    /// <returns>该窗口算作任务栏元素时为 true。/ True when the window counts as part of the taskbar.</returns>
    public static bool TryResolve(
        RECT windowRect,
        RECT taskbarRect,
        LayoutOrientation orientation,
        out TaskbarPrimaryRange range,
        out TaskbarOccupancyRejection rejection)
    {
        range = default;
        rejection = TaskbarOccupancyRejection.None;

        var (primaryLength, crossLength) = ResolveLengths(taskbarRect, orientation);
        if (primaryLength <= 0 || crossLength <= 0)
            return false;

        var (windowStart, windowEnd, windowCrossStart, windowCrossEnd) = ToAxes(windowRect, orientation);
        var (taskbarStart, taskbarEnd, taskbarCrossStart, taskbarCrossEnd) = ToAxes(taskbarRect, orientation);

        var crossOverlap = Math.Min(windowCrossEnd, taskbarCrossEnd) - Math.Max(windowCrossStart, taskbarCrossStart);
        if (crossOverlap < crossLength * MinimumCrossOverlapRatio)
        {
            rejection = TaskbarOccupancyRejection.NotOverTaskbar;
            return false;
        }

        var windowCrossSize = windowCrossEnd - windowCrossStart;
        if (windowCrossSize > crossLength * MaximumCrossSizeRatio)
        {
            rejection = TaskbarOccupancyRejection.TooTall;
            return false;
        }

        var overlapStart = Math.Max(windowStart, taskbarStart);
        var overlapEnd = Math.Min(windowEnd, taskbarEnd);
        if (overlapEnd <= overlapStart)
        {
            rejection = TaskbarOccupancyRejection.OutsideTaskbar;
            return false;
        }

        if (overlapEnd - overlapStart > primaryLength * MaximumPrimaryShare)
        {
            rejection = TaskbarOccupancyRejection.TooWide;
            return false;
        }

        range = new TaskbarPrimaryRange(
            Math.Clamp(overlapStart - taskbarStart, 0, primaryLength),
            Math.Clamp(overlapEnd - taskbarStart, 0, primaryLength));
        return range.End > range.Start;
    }

    private static (int PrimaryLength, int CrossLength) ResolveLengths(RECT taskbarRect, LayoutOrientation orientation) =>
        orientation == LayoutOrientation.Horizontal
            ? (taskbarRect.Right - taskbarRect.Left, taskbarRect.Bottom - taskbarRect.Top)
            : (taskbarRect.Bottom - taskbarRect.Top, taskbarRect.Right - taskbarRect.Left);

    private static (int Start, int End, int CrossStart, int CrossEnd) ToAxes(RECT rect, LayoutOrientation orientation) =>
        orientation == LayoutOrientation.Horizontal
            ? (rect.Left, rect.Right, rect.Top, rect.Bottom)
            : (rect.Top, rect.Bottom, rect.Left, rect.Right);
}

/// <summary>候选窗口未被计入任务栏占用的原因。/ Why a candidate window was not counted as taskbar occupancy.</summary>
public enum TaskbarOccupancyRejection
{
    /// <summary>被接受。/ Accepted.</summary>
    None = 0,

    /// <summary>横轴几乎不与任务栏重叠。/ Almost no cross-axis overlap with the taskbar.</summary>
    NotOverTaskbar = 1,

    /// <summary>比任务栏高太多，是浮层而不是任务栏元素。/ Far taller than the taskbar, so a flyout rather than a taskbar element.</summary>
    TooTall = 2,

    /// <summary>主轴完全不与任务栏重叠。/ No primary-axis overlap with the taskbar at all.</summary>
    OutsideTaskbar = 3,

    /// <summary>占掉任务栏大半主轴长度，是浮层而不是任务栏元素。/ Takes most of the taskbar's primary axis, so a flyout rather than a taskbar element.</summary>
    TooWide = 4,

    /// <summary>主轴长度超过任务栏的 45%，是容器（XAML 岛、任务列表外壳）而不是槽位。/ Longer than 45% of the taskbar, so a container (the XAML island, the task-list shell) rather than a slot.</summary>
    Container = 5,

    /// <summary>主轴长度太短，是分隔条或零尺寸占位窗口。/ Too short, so a separator or a zero-sized placeholder.</summary>
    TooSmall = 6
}
