namespace AFMediaBar.Classes.Services;

/// <summary>
/// 发布当前任务栏媒体条可用的运行时长度范围，供设置页展示而不持久化显示环境状态。
/// Publishes the taskbar media bar's current runtime length range for settings presentation
/// without persisting display-environment state.
/// </summary>
public sealed class TaskbarLengthConstraintsService
{
    private double _minimumLengthDip = 120;
    private double _maximumLengthDip = 1200;

    /// <summary>当前悬停层和固定组件所需的最小长度。 / Current minimum required by the hover layer and fixed components.</summary>
    public double MinimumLengthDip => _minimumLengthDip;

    /// <summary>当前任务栏安全区间允许的最大长度。 / Current maximum allowed by the taskbar safe range.</summary>
    public double MaximumLengthDip => _maximumLengthDip;

    /// <summary>运行时长度范围变化时触发。 / Raised when the runtime length range changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 更新有效长度范围；非有限输入沿用上次值，最大值始终不小于最小值。
    /// Updates the effective range; non-finite inputs retain the previous value and the maximum
    /// is always at least the minimum.
    /// </summary>
    public void Update(double minimumLengthDip, double maximumLengthDip)
    {
        var minimum = double.IsFinite(minimumLengthDip)
            ? Math.Max(1, minimumLengthDip)
            : _minimumLengthDip;
        var maximum = double.IsFinite(maximumLengthDip) && maximumLengthDip > 0
            ? Math.Max(minimum, maximumLengthDip)
            : Math.Max(minimum, _maximumLengthDip);
        if (Math.Abs(_minimumLengthDip - minimum) < 0.1 &&
            Math.Abs(_maximumLengthDip - maximum) < 0.1)
        {
            return;
        }

        _minimumLengthDip = minimum;
        _maximumLengthDip = maximum;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
