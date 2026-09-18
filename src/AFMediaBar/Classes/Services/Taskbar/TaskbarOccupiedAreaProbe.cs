using System.Runtime.InteropServices;
using System.Windows.Automation;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;
using static AFMediaBar.Classes.Interop.NativeMethods;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 使用 UI Automation 和 Shell 子窗口回退探测任务栏占用区域。
/// Probes taskbar occupancy with UI Automation and Shell child-window fallbacks.
///
/// 每次探测在单独的 MTA 后台线程运行，避免持有 Explorer 子 HWND 的 WPF 线程同步等待 Explorer。
/// Each probe runs on a dedicated MTA background thread so the WPF thread owning an Explorer child HWND never waits on Explorer.
/// </summary>
public sealed class TaskbarOccupiedAreaProbe : ITaskbarOccupiedAreaProbe
{
    private const int MinimumElementPrimaryPixels = 8;

    /// <summary>
    /// UIA 元素的长度上限：再长的元素是容器而不是一个槽位。取值要能容下最宽的搜索框（Windows 11 上它接近 300 像素），
    /// 同时远小于半条任务栏，以免把容器当成占用。
    /// Upper length bound for a UIA element: anything longer is a container rather than a slot. The value has to admit the widest
    /// search box (close to 300 pixels on Windows 11) while staying far below half a taskbar, so a container is never counted.
    /// </summary>
    private const int MaximumElementPrimaryPixels = 420;

    /// <summary>子窗口槽位递归的最大层数：搜索框可能挂在 XAML 岛下面，因此要往下看一层。/ Maximum nesting depth for child slots: the search box may hang under the XAML island, so one level further is inspected.</summary>
    private const int MaximumChildDepth = 2;

    /// <summary>单次探测最多检查的任务栏子窗口数：Explorer 的树异常时也要有界。/ Upper bound on taskbar child windows inspected per probe: even an abnormal Explorer tree stays bounded.</summary>
    private const int MaximumChildWindows = 48;

    /// <summary>上一次写进日志的探测结果：探测每 250 毫秒重跑，内容不变时不重复写。/ Last probe result written to the log; the probe re-runs every 250 ms and unchanged content is not written again.</summary>
    private static string? _lastProbeSignature;

    /// <inheritdoc />
    public void Start(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels,
        Action<IReadOnlyList<TaskbarPrimaryRange>> succeeded,
        Action failed)
    {
        ArgumentNullException.ThrowIfNull(succeeded);
        ArgumentNullException.ThrowIfNull(failed);

        try
        {
            var thread = new Thread(() => Probe(
                taskbarHandle,
                taskbarRect,
                orientation,
                dpiScale,
                edgePaddingPixels,
                succeeded,
                failed))
            {
                IsBackground = true,
                Name = "AFMediaBar.TaskbarUiaProbe"
            };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }
        catch (Exception)
        {
            failed();
        }
    }

    private static void Probe(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels,
        Action<IReadOnlyList<TaskbarPrimaryRange>> succeeded,
        Action failed)
    {
        IReadOnlyList<TaskbarPrimaryRange> ranges;
        try
        {
            ranges = ProbeSafePrimaryRanges(
                taskbarHandle,
                taskbarRect,
                orientation,
                dpiScale,
                edgePaddingPixels);
        }
        catch (Exception)
        {
            // Explorer 可能正在重启或替换 UI Automation 树；外层服务不会发布本次结果。
            // Explorer may be restarting or replacing its UI Automation tree; the outer service will not publish this result.
            failed();
            return;
        }

        succeeded(ranges);
    }

    private static IReadOnlyList<TaskbarPrimaryRange> ProbeSafePrimaryRanges(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels)
    {
        var primaryLength = orientation == LayoutOrientation.Horizontal
            ? taskbarRect.Right - taskbarRect.Left
            : taskbarRect.Bottom - taskbarRect.Top;
        if (primaryLength <= 0)
            return [];

        var occupied = new List<TaskbarPrimaryRange>();
        var sources = new List<string>();
        TryCollectAutomationRanges(taskbarHandle, taskbarRect, orientation, primaryLength, occupied, sources);
        var hasAutomationRanges = occupied.Count > 0;

        // TrayNotifyWnd and taskband remain useful on systems where the XAML taskbar tree is not exposed to UIA.
        AddShellFallbackRange(taskbarHandle, "TrayNotifyWnd", taskbarRect, orientation, primaryLength, occupied, sources);
        if (!hasAutomationRanges)
        {
            AddShellFallbackRange(taskbarHandle, "MSTaskSwWClass", taskbarRect, orientation, primaryLength, occupied, sources);
            AddShellFallbackRange(taskbarHandle, "MSTaskListWClass", taskbarRect, orientation, primaryLength, occupied, sources);
        }

        // 别的进程叠在任务栏上的窗口（Windows 10 的搜索框与任务视图）既枚举不到也躲不开，只能按"盖在任务栏上"识别。
        // Windows over the taskbar from other processes (the Windows 10 search box and Task View) are neither descendants nor
        // avoidable any other way, so they are identified by covering the taskbar.
        AddOverlayRanges(taskbarHandle, taskbarRect, orientation, primaryLength, occupied, sources);

        // 任务栏自己的子窗口里也有"槽位"（开始按钮、任务列表、搜索框），UIA 拿不到其中一部分（搜索框在 Windows 10 上是一个文本控件，
        // 既不是按钮也不是列表项），因此这里按"槽位大小"再收一遍——它是唯一不依赖 UIA 的兜底。
        // The taskbar's own child windows include slots (the Start button, the task list, the search box) and UIA does not reach all of
        // them (on Windows 10 the search box is a text control, neither a button nor a list item), so they are collected again by slot
        // size. This is the only fallback that does not depend on UIA.
        AddTaskbarChildSlotRanges(taskbarHandle, taskbarRect, orientation, primaryLength, occupied, sources);

        var gap = Math.Max(8, (int)Math.Round(8 * Math.Max(1, dpiScale)));
        var free = TaskbarFreeRangeCalculator.Calculate(primaryLength, occupied, Math.Max(0, edgePaddingPixels), gap);
        LogProbeResult(primaryLength, free, sources);
        return free;
    }

    /// <summary>
    /// 采集任务栏自己的子窗口"槽位"（含一层嵌套，因为搜索框可能挂在 XAML 岛下面）。
    /// 槽位 = 与任务栏横轴相交、主轴长度在 [8, 45% 任务栏长度] 之间的窗口；整条任务栏宽的容器（XAML 岛）与零尺寸占位不算。
    /// Collects the taskbar's own child "slots", one nesting level deep because the search box may hang under the XAML island.
    /// A slot is a window that intersects the taskbar on the cross axis and is between 8 pixels and 45% of the taskbar long on the
    /// primary axis; full-width containers (the XAML island) and zero-sized placeholders do not count.
    /// </summary>
    private static void AddTaskbarChildSlotRanges(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength,
        List<TaskbarPrimaryRange> occupied,
        List<string> sources)
    {
        var ownProcessId = Environment.ProcessId;
        var visited = 0;

        void Collect(IntPtr parent, int depth)
        {
            for (var child = FindWindowEx(parent, IntPtr.Zero, null, null);
                 child != IntPtr.Zero && visited < MaximumChildWindows;
                 child = FindWindowEx(parent, child, null, null))
            {
                visited++;
                _ = GetWindowThreadProcessId(child, out var processId);
                if (processId == ownProcessId)
                    continue;

                if (!IsWindowVisible(child) || !GetWindowRect(child, out var rect))
                    continue;

                if (TaskbarOverlayRangePolicy.TryResolveSlot(rect, taskbarRect, orientation, out var range, out var rejection))
                {
                    occupied.Add(range);
                    sources.Add($"子窗口:{DescribeClass(child)}({range.Start}..{range.End})");
                    continue;
                }

                // 容器（整条任务栏宽）与零尺寸占位继续往下看一层：搜索框可能挂在 XAML 岛里。
                // Containers (full taskbar width) and zero-sized placeholders are descended into: the search box may live inside the
                // XAML island.
                if (depth < MaximumChildDepth && rejection is TaskbarOccupancyRejection.Container or TaskbarOccupancyRejection.TooSmall)
                    Collect(child, depth + 1);
            }
        }

        Collect(taskbarHandle, 0);
    }

    /// <summary>
    /// 把"盖在任务栏上、又不属于任务栏"的顶层窗口计入占用区间，两条采集方式取并集：
    ///
    /// ① **Z 序扫描**（`EnumWindows`，只看到任务栏**之上**的窗口）：与谁盖在谁上面无关，因此媒体栏自己压在搜索框上时也能发现它——
    /// 这正是 Windows 10 上"启动后卡在搜索框里、播放时又被放到正确位置"的成因：窄媒体栏落在搜索框那一段，采样时命中的是媒体栏
    /// 自己（属于任务栏），搜索框因此永远发现不了。
    /// ② **命中测试**（`WindowFromPoint`）：返回的正是**会抢走鼠标输入的那个窗口**，用来补上矩形不规则的覆盖窗口。
    ///
    /// 两条都按类名、进程与几何条件过滤，并把**被拒的候选**也写进同一行日志——现场机器（Release 构建）拿不到调试输出，
    /// 这一行是"媒体栏为什么放在这里/为什么没发现搜索框"的唯一证据。
    /// Counts top-level windows lying over the taskbar that do not belong to it, taking the union of two collections:
    ///
    /// 1. **Z-order scan** (`EnumWindows`, only windows **above** the taskbar): independent of who covers whom, so it still finds the
    ///    search box while the media bar sits on top of it — which is exactly the Windows 10 loop of "stuck in the search box at
    ///    startup, placed correctly while playing": a narrow bar lands on the search box, and hit testing then returns the bar itself
    ///    (part of the taskbar), so the search box can never be discovered.
    /// 2. **Hit test** (`WindowFromPoint`): returns the very window that would take the mouse input, covering overlays with irregular
    ///    rectangles.
    ///
    /// Both apply the class, process, and geometry filters, and **rejected candidates are logged in the same line**: a reporting
    /// machine runs a Release build with no debug output, and that line is the only evidence for why the bar ended up where it did.
    /// </summary>
    private static void AddOverlayRanges(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength,
        List<TaskbarPrimaryRange> occupied,
        List<string> sources)
    {
        var ownProcessId = Environment.ProcessId;
        var seen = new HashSet<IntPtr>();
        var accepted = new List<(IntPtr Handle, TaskbarPrimaryRange Range)>();
        var rejected = new List<(IntPtr Handle, TaskbarOccupancyRejection Reason)>();

        void Consider(IntPtr candidate)
        {
            if (candidate == IntPtr.Zero || candidate == taskbarHandle || IsDescendantOf(candidate, taskbarHandle))
                return;

            // 本程序自己的窗口（媒体栏、设置窗口、通知）不计入：媒体栏本身是任务栏的子窗口，而其余是我们自己的界面。
            // This application's own windows (the bar, settings, notification) are ignored: the bar is a child of the taskbar and the
            // rest are our own interface.
            _ = GetWindowThreadProcessId(candidate, out var processId);
            if (processId == ownProcessId)
                return;

            if (!IsWindowVisible(candidate))
                return;

            if (!seen.Add(candidate))
                return;

            if (!GetWindowRect(candidate, out var windowRect))
                return;

            if (TaskbarOverlayRangePolicy.TryResolve(windowRect, taskbarRect, orientation, out var range, out var rejection))
            {
                accepted.Add((candidate, range));
                occupied.Add(range);
                return;
            }

            // 不覆盖任务栏的窗口不值得记录：它们与"媒体栏放在哪里"无关。
            // Windows that do not lie over the taskbar are not worth recording: they cannot affect where the bar goes.
            if (rejection is TaskbarOccupancyRejection.TooTall or TaskbarOccupancyRejection.TooWide)
                rejected.Add((candidate, rejection));
        }

        CollectWindowsAboveTaskbar(taskbarHandle, Consider);
        CollectHitTestWindows(taskbarHandle, taskbarRect, orientation, primaryLength, ownProcessId, Consider);

        foreach (var candidate in accepted)
            sources.Add($"外部窗口:{DescribeClass(candidate.Handle)}({candidate.Range.Start}..{candidate.Range.End})");

        foreach (var candidate in rejected)
            sources.Add($"外部窗口被拒:{DescribeClass(candidate.Handle)}/{candidate.Reason}{DescribeRect(candidate.Handle)}");
    }

    /// <summary>
    /// 按 Z 序枚举任务栏**之上**的顶层窗口（`EnumWindows` 从最上面开始，遇到任务栏即停止）。
    /// Enumerates the top-level windows **above** the taskbar in Z-order (`EnumWindows` starts at the top and stops at the taskbar).
    /// </summary>
    private static void CollectWindowsAboveTaskbar(IntPtr taskbarHandle, Action<IntPtr> consider)
    {
        var reachedTaskbar = false;
        _ = EnumWindows((handle, _) =>
        {
            if (handle == taskbarHandle)
            {
                reachedTaskbar = true;
                return false;
            }

            consider(handle);
            return true;
        }, IntPtr.Zero);

        if (!reachedTaskbar)
        {
            // 任务栏不在枚举序列里（极少见）：此时不发布这一轮结果，交给下一次探测。
            // The taskbar was not in the enumeration (very rare): publish nothing this round and let the next probe try again.
            throw new InvalidOperationException("taskbar not reachable in Z-order enumeration");
        }
    }

    /// <summary>沿任务栏中线取样，把命中的顶层窗口交给同一个判定。/ Samples the taskbar's centre line and hands the hit top-level windows to the same decision.</summary>
    private static void CollectHitTestWindows(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength,
        int ownProcessId,
        Action<IntPtr> consider)
    {
        const int SampleStepPixels = 16;
        var crossCentre = orientation == LayoutOrientation.Horizontal
            ? taskbarRect.Top + (taskbarRect.Bottom - taskbarRect.Top) / 2
            : taskbarRect.Left + (taskbarRect.Right - taskbarRect.Left) / 2;
        var primaryStart = orientation == LayoutOrientation.Horizontal ? taskbarRect.Left : taskbarRect.Top;

        for (var offset = 0; offset < primaryLength; offset += SampleStepPixels)
        {
            var primary = primaryStart + offset;
            var point = orientation == LayoutOrientation.Horizontal
                ? new POINT { X = primary, Y = crossCentre }
                : new POINT { X = crossCentre, Y = primary };
            var hit = WindowFromPoint(point);
            if (hit == IntPtr.Zero)
                continue;

            var root = GetAncestor(hit, GA_ROOT);
            if (root == IntPtr.Zero)
                root = hit;

            // 命中媒体栏自己时 MUST NOT 当作"没有外部窗口"：媒体栏压在搜索框上时，正是它挡住了搜索框。
            // A hit on the bar itself MUST NOT be read as "nothing is over the taskbar": while the bar covers the search box, the bar
            // is exactly what hides it.
            _ = GetWindowThreadProcessId(root, out var processId);
            if (root == taskbarHandle || processId == ownProcessId || IsDescendantOf(root, taskbarHandle))
                continue;

            consider(root);
        }
    }

    private static string DescribeClass(IntPtr handle)
    {
        var className = new System.Text.StringBuilder(128);
        _ = GetClassName(handle, className, className.Capacity);
        return className.ToString();
    }

    private static string DescribeRect(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var rect))
            return string.Empty;

        return $"({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom})";
    }

    private static bool IsDescendantOf(IntPtr handle, IntPtr ancestor)
    {
        for (var current = GetParent(handle); current != IntPtr.Zero; current = GetParent(current))
        {
            if (current == ancestor)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 把这一轮探测的结果写进日志（内容去重）：空闲区间与每个占用区间的来源。
    ///
    /// 现场机器跑的是 Release 构建、拿不到调试输出，而"媒体栏为什么被放在这里"只能由这一行回答：
    /// 空闲区间的起点、以及占用区间分别来自 UIA、Shell 回退、任务栏子窗口还是外部窗口，缺了哪一段一眼就能看出来。
    /// Writes this probe round into the log, deduplicated by content: the free ranges and the source of every occupied range.
    ///
    /// A reporting machine runs a Release build with no debug output, and this line is the only thing that answers "why was the bar put
    /// here": the start of the free range plus whether each occupied range came from UIA, a shell fallback, a taskbar child window, or a
    /// foreign window. Whichever segment is missing shows up immediately.
    /// /// </summary>
    private static void LogProbeResult(int primaryLength, IReadOnlyList<TaskbarPrimaryRange> free, IReadOnlyList<string> sources)
    {
        var freeText = free.Count == 0
            ? "（无）"
            : string.Join(" ", free.Select(range => $"{range.Start}..{range.End}"));
        var sourceText = sources.Count == 0 ? "（无）" : string.Join(" ", sources);
        var description = $"长度={primaryLength} 空闲=[{freeText}] 占用来源=[{sourceText}]";

        if (string.Equals(_lastProbeSignature, description, StringComparison.Ordinal))
            return;

        _lastProbeSignature = description;
        AppLogService.Current?.Info("Taskbar", $"避让探测 / occupancy probe: {description}");
    }

    private static void TryCollectAutomationRanges(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength,
        List<TaskbarPrimaryRange> occupied,
        List<string> sources)
    {
        try
        {
            var root = AutomationElement.FromHandle(taskbarHandle);
            // 文本型控件也要：Windows 10 的搜索框是一个可输入的文本框，既不是按钮也不是列表项，
            // 只匹配按钮类控件时会整段漏掉，左半条任务栏于是被当成空闲区。
            // Text-ish controls count too: the Windows 10 search box is an editable field, neither a button nor a list item, and
            // matching only button-like controls misses the whole segment, leaving the left half of the taskbar "free".
            var interactiveControlCondition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.SplitButton),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            var cacheRequest = new CacheRequest
            {
                TreeScope = TreeScope.Element,
                TreeFilter = Automation.ControlViewCondition
            };
            cacheRequest.Add(AutomationElement.IsOffscreenProperty);
            cacheRequest.Add(AutomationElement.BoundingRectangleProperty);

            AutomationElementCollection elements;
            using (cacheRequest.Activate())
            {
                elements = root.FindAll(TreeScope.Descendants, interactiveControlCondition);
            }

            foreach (AutomationElement element in elements)
            {
                try
                {
                    if (element.Cached.IsOffscreen)
                        continue;

                    AddAutomationRange(
                        element.Cached.BoundingRectangle,
                        taskbarRect,
                        orientation,
                        primaryLength,
                        occupied,
                        sources);
                }
                catch (ElementNotAvailableException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (COMException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void AddAutomationRange(
        System.Windows.Rect bounds,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength,
        List<TaskbarPrimaryRange> occupied,
        List<string> sources)
    {
        if (!IsUsefulTaskbarElement(bounds, taskbarRect, orientation, primaryLength))
            return;

        var start = orientation == LayoutOrientation.Horizontal
            ? (int)Math.Round(bounds.Left - taskbarRect.Left)
            : (int)Math.Round(bounds.Top - taskbarRect.Top);
        var length = orientation == LayoutOrientation.Horizontal
            ? (int)Math.Round(bounds.Width)
            : (int)Math.Round(bounds.Height);
        occupied.Add(new TaskbarPrimaryRange(start, start + length));
        sources.Add($"UIA({start}..{start + length})");
    }

    private static bool IsUsefulTaskbarElement(
        System.Windows.Rect bounds,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength)
    {
        var crossOverlap = orientation == LayoutOrientation.Horizontal
            ? Math.Min(bounds.Bottom, taskbarRect.Bottom) - Math.Max(bounds.Top, taskbarRect.Top)
            : Math.Min(bounds.Right, taskbarRect.Right) - Math.Max(bounds.Left, taskbarRect.Left);
        if (crossOverlap <= 0)
            return false;

        var primary = orientation == LayoutOrientation.Horizontal ? bounds.Width : bounds.Height;
        return primary >= MinimumElementPrimaryPixels &&
               primary <= Math.Min(MaximumElementPrimaryPixels, primaryLength * 0.45);
    }

    private static void AddShellFallbackRange(
        IntPtr taskbarHandle,
        string className,
        RECT taskbarRect,
        LayoutOrientation orientation,
        int primaryLength,
        List<TaskbarPrimaryRange> occupied,
        List<string> sources)
    {
        var child = FindDescendantByClass(taskbarHandle, className);
        if (child == IntPtr.Zero || !GetWindowRect(child, out var rect))
            return;

        var start = orientation == LayoutOrientation.Horizontal
            ? rect.Left - taskbarRect.Left
            : rect.Top - taskbarRect.Top;
        var end = orientation == LayoutOrientation.Horizontal
            ? rect.Right - taskbarRect.Left
            : rect.Bottom - taskbarRect.Top;
        start = Math.Clamp(start, 0, primaryLength);
        end = Math.Clamp(end, start, primaryLength);
        if (end > start)
        {
            occupied.Add(new TaskbarPrimaryRange(start, end));
            sources.Add($"Shell:{className}({start}..{end})");
        }
    }

    private static IntPtr FindDescendantByClass(IntPtr parent, string className)
    {
        for (var child = FindWindowEx(parent, IntPtr.Zero, null, null);
             child != IntPtr.Zero;
             child = FindWindowEx(parent, child, null, null))
        {
            var buffer = new System.Text.StringBuilder(256);
            GetClassName(child, buffer, buffer.Capacity);
            if (string.Equals(buffer.ToString(), className, StringComparison.Ordinal))
                return child;

            var nested = FindDescendantByClass(child, className);
            if (nested != IntPtr.Zero)
                return nested;
        }

        return IntPtr.Zero;
    }
}
