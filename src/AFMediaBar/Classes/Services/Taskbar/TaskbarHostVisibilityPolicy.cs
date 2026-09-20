namespace AFMediaBar.Classes.Services;

/// <summary>任务栏宿主窗口应当的可见性。/ Visibility the taskbar host window should have.</summary>
public enum TaskbarHostVisibility
{
    /// <summary>显示窗口。/ The window is shown.</summary>
    Visible = 0,

    /// <summary>隐藏窗口。/ The window is hidden.</summary>
    Collapsed = 1
}

/// <summary>
/// 任务栏宿主窗口显隐的纯策略：把任务栏运动状态与"静置层是否为空"合成一个结论。
/// Pure policy for the taskbar host window's visibility: it folds the taskbar's motion state and "is the rest layer empty" into one verdict.
/// </summary>
public static class TaskbarHostVisibilityPolicy
{
    /// <summary>
    /// 解析宿主窗口这次应当显示还是隐藏。
    ///
    /// 三种状态的区别是刻意的：**稳定收起**时宿主 MUST 自己隐藏窗口——只依赖"子窗口跟着父窗口运动"偶尔会失败，
    /// 而任务栏随后展开时那条留在原地的媒体栏就正好卡在任务栏边缘、被裁掉一截；**收起动画进行中**不做这件事（父窗口
    /// 正带着我们走，中途隐藏只会在连续触发时闪一下）；**从收起转为展开**时立刻按静置层自己的判据恢复显示，否则要等
    /// 动画稳定（约 200 毫秒）之后才出现，看上去就是"任务栏回来了而媒体栏慢半拍"。
    /// Decides whether the host window should be shown or hidden this time.
    ///
    /// The three states differ on purpose: while the taskbar is **settled at the screen edge** the host MUST hide its own window — relying
    /// only on "the child follows its parent" occasionally fails, and when the taskbar then reveals, the bar left behind ends up straddling
    /// the taskbar edge and gets clipped; while the **hide animation runs** that must not happen (the parent is carrying us, and hiding
    /// mid-animation only flickers when the user keeps re-triggering it); and the moment the taskbar starts **revealing** the bar is shown
    /// again by the rest layer's own rule, because waiting for the motion to settle (about 200 ms) reads as "the taskbar is back but the
    /// media bar lags behind".
    /// </summary>
    /// <param name="motion">当前任务栏运动状态。/ Current taskbar motion state.</param>
    /// <param name="restLayerEmpty">静置层是否一个组件都不显示（控件算出的结论）。/ Whether the rest layer shows no component at all, as the control computed it.</param>
    public static TaskbarHostVisibility Resolve(in TaskbarMotionState motion, bool restLayerEmpty)
    {
        if (motion.IsHidden && !motion.IsMoving)
        {
            return TaskbarHostVisibility.Collapsed;
        }

        return restLayerEmpty ? TaskbarHostVisibility.Collapsed : TaskbarHostVisibility.Visible;
    }
}
