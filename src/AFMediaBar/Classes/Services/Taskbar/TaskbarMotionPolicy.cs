using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 任务栏自动隐藏动画的纯状态机：矩形变化时进入移动态，连续若干次不变后才重新开放宿主交互。
/// Pure state machine for taskbar auto-hide motion: a rectangle change enters the moving state and interaction is restored only
/// after several identical observations.
/// </summary>
public static class TaskbarMotionPolicy
{
    /// <summary>确认显隐动画结束所需的连续稳定样本数。/ Consecutive stable samples required before a reveal or hide is considered complete.</summary>
    public const int RequiredStableSamples = 4;

    /// <summary>任务栏只在屏幕边缘留下不超过这些物理像素时视为已自动隐藏。/ Maximum visible physical pixels that count as auto-hidden.</summary>
    public const int HiddenEdgePixels = 4;

    /// <summary>推进一次任务栏矩形观察。/ Advances one taskbar-rectangle observation.</summary>
    public static TaskbarMotionState Observe(
        TaskbarMotionState previous,
        NativeMethods.RECT taskbarRect,
        Rect monitorBounds,
        LayoutOrientation orientation)
    {
        if (!IsValid(taskbarRect) || monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
            return previous;

        if (!previous.HasObservation)
        {
            return new TaskbarMotionState(
                true,
                taskbarRect,
                0,
                true,
                IsHidden(taskbarRect, monitorBounds, orientation));
        }

        var changed = !taskbarRect.Equals(previous.LastRect);
        var stableSamples = changed ? 0 : Math.Min(RequiredStableSamples, previous.StableSamples + 1);
        var isMoving = changed || previous.IsMoving && stableSamples < RequiredStableSamples;
        var hidden = isMoving ? previous.IsHidden : IsHidden(taskbarRect, monitorBounds, orientation);
        return new TaskbarMotionState(true, taskbarRect, stableSamples, isMoving, hidden);
    }

    /// <summary>按任务栏与显示器的横轴交集判断任务栏是否只剩边缘触发带可见。/ Detects whether only the taskbar's edge-trigger strip remains visible.</summary>
    public static bool IsHidden(
        NativeMethods.RECT taskbarRect,
        Rect monitorBounds,
        LayoutOrientation orientation)
    {
        if (!IsValid(taskbarRect) || monitorBounds.Width <= 0 || monitorBounds.Height <= 0)
            return false;

        var visibleCross = orientation == LayoutOrientation.Horizontal
            ? Math.Min(taskbarRect.Bottom, monitorBounds.Bottom) - Math.Max(taskbarRect.Top, monitorBounds.Top)
            : Math.Min(taskbarRect.Right, monitorBounds.Right) - Math.Max(taskbarRect.Left, monitorBounds.Left);
        return visibleCross <= HiddenEdgePixels;
    }

    private static bool IsValid(NativeMethods.RECT rect) => rect.Right > rect.Left && rect.Bottom > rect.Top;
}

/// <summary>任务栏移动观察状态。/ State of taskbar motion observations.</summary>
public readonly record struct TaskbarMotionState(
    bool HasObservation,
    NativeMethods.RECT LastRect,
    int StableSamples,
    bool IsMoving,
    bool IsHidden);
