using System;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 设置页滑杆的步进、长按加速与滚轮调节。
/// Steps, hold acceleration, and wheel adjustment for settings-page sliders.
/// </summary>
[TestClass]
public sealed class SliderAdjustmentPolicyTests
{
    [TestMethod]
    public void BaseStepPrefersTheTickSpacingThenSmallChange()
    {
        Assert.AreEqual(10, SliderAdjustmentPolicy.ResolveBaseStep(10, 400, 10, 1));
        Assert.AreEqual(100, SliderAdjustmentPolicy.ResolveBaseStep(100, 900, 100, 0.1));
        Assert.AreEqual(5, SliderAdjustmentPolicy.ResolveBaseStep(80, 125, 5, 1));
        // 没有刻度时退回小步长；区间无效时返回 0，调用方据此不做任何调节。
        // Without ticks it falls back to the small change; an invalid range returns zero and the caller then adjusts nothing.
        Assert.AreEqual(2, SliderAdjustmentPolicy.ResolveBaseStep(0, 100, 0, 2));
        Assert.AreEqual(1, SliderAdjustmentPolicy.ResolveBaseStep(0, 100, 0, 0));
        Assert.AreEqual(0, SliderAdjustmentPolicy.ResolveBaseStep(10, 10, 1, 1));
    }

    /// <summary>
    /// 大区间滑杆必须有看得见的一格：固定宽度滑杆（120–4096 DIP）既没有刻度也没有吸附，
    /// 沿用它默认的 0.1 小步长时按十几次读数才动一个单位，这正是"调节很慢"的根因。
    /// A wide-range slider needs a visible step: the fixed-width slider (120–4096 DIP) has neither ticks nor snapping, and its
    /// default 0.1 small change moved the reading by one unit only after a dozen presses — the actual cause of "adjustment is
    /// far too slow".
    /// </summary>
    [TestMethod]
    public void WideRangeSlidersGetAVisibleStepInsteadOfTinySmallChange()
    {
        var step = SliderAdjustmentPolicy.ResolveBaseStep(120, 4096, tickFrequency: 0, smallChange: 0.1);
        Assert.AreEqual(40, step, "区间下限步长必须被抬到约 1% 的整数值。");
        Assert.IsTrue(step / (4096 - 120) >= SliderAdjustmentPolicy.MinimumStepRangeFraction);

        // 小滑杆不受影响：区间下限（1% 向上取整）比它们自己的步长小。
        // Small sliders are untouched: their ratio floor is below the step they already declare.
        Assert.AreEqual(1, SliderAdjustmentPolicy.ResolveBaseStep(-20, 20, 1, 0.1));
        Assert.AreEqual(1, SliderAdjustmentPolicy.ResolveBaseStep(4, 32, 1, 0.1));
        Assert.AreEqual(1, SliderAdjustmentPolicy.ResolveBaseStep(9, 24, 1, 0.1));
        Assert.AreEqual(0.5, SliderAdjustmentPolicy.ResolveBaseStep(0.5, 5, 0.5, 0.1));
        Assert.AreEqual(5, SliderAdjustmentPolicy.ResolveBaseStep(50, 150, 5, 1));

        // 区间比步长还窄时步长收敛到区间本身。
        // When the range is narrower than the step, the step collapses onto the range.
        Assert.AreEqual(3, SliderAdjustmentPolicy.ResolveBaseStep(0, 3, 10, 1));
    }

    [TestMethod]
    public void OneStepIsVisibleAndReachesTheBoundWithinAFewPresses()
    {
        var step = SliderAdjustmentPolicy.ResolveBaseStep(120, 4096, 0, 0.1);
        var value = 120d;
        for (var press = 0; press < 6; press++)
        {
            value = SliderAdjustmentPolicy.Adjust(value, step, SliderAdjustmentDirection.Increase, 120, 4096);
        }

        Assert.AreEqual(120 + 6 * step, value, 0.001);
        Assert.IsTrue(value - 120 >= 200, "按六次至少要移动两百多 DIP，否则一次按键看不出变化。");
    }

    [TestMethod]
    public void SnapAlignsOffGridValuesBeforeAdjusting()
    {
        // 网格是"下限 + 整数倍步长"：300 距 350 与 100 一样远，但只有 300 在网格上。
        // The grid is the minimum plus whole steps: 350 is not on it, 300 and 400 are, and 347 is nearer 300.
        Assert.AreEqual(300, SliderAdjustmentPolicy.Snap(347, 100, 900, 100));
        Assert.AreEqual(400, SliderAdjustmentPolicy.Snap(350, 100, 900, 100));
        Assert.AreEqual(100, SliderAdjustmentPolicy.Snap(0, 100, 900, 100));
        Assert.AreEqual(900, SliderAdjustmentPolicy.Snap(5000, 100, 900, 100));
        Assert.AreEqual(60, SliderAdjustmentPolicy.Snap(63, 10, 400, 10));
        // 非正步长只做夹取，不做网格对齐。
        // A non-positive step only clamps instead of snapping.
        Assert.AreEqual(63, SliderAdjustmentPolicy.Snap(63, 10, 400, 0));
    }

    [TestMethod]
    public void AdjustMovesExactlyOneStepAndStopsAtTheBounds()
    {
        Assert.AreEqual(20, SliderAdjustmentPolicy.Adjust(10, 10, SliderAdjustmentDirection.Increase, 10, 400));
        Assert.AreEqual(10, SliderAdjustmentPolicy.Adjust(20, 10, SliderAdjustmentDirection.Decrease, 10, 400));
        Assert.AreEqual(10, SliderAdjustmentPolicy.Adjust(10, 10, SliderAdjustmentDirection.Decrease, 10, 400));
        Assert.AreEqual(400, SliderAdjustmentPolicy.Adjust(400, 10, SliderAdjustmentDirection.Increase, 10, 400));
        // 一个跨过整个区间的步长也只能走到边界，绝不能越界。
        // A step larger than the whole range still stops at the bound and never overshoots.
        Assert.AreEqual(400, SliderAdjustmentPolicy.Adjust(10, 5000, SliderAdjustmentDirection.Increase, 10, 400));
        Assert.AreEqual(10, SliderAdjustmentPolicy.Adjust(400, 5000, SliderAdjustmentDirection.Decrease, 10, 400));
        // 网格外的旧值先对齐再走一步：347 先落到 300，再减一格到 200，因为"按一次走一格"必须始终成立。
        // An off-grid legacy value is aligned and then stepped: 347 lands on 300 and then on 200, because "one press moves one
        // step" has to hold every time.
        Assert.AreEqual(200, SliderAdjustmentPolicy.Adjust(347, 100, SliderAdjustmentDirection.Decrease, 100, 900));
    }

    [TestMethod]
    public void HoldMultiplierStartsAtOneAndGrowsWithTheHeldTime()
    {
        Assert.AreEqual(1, SliderAdjustmentPolicy.ResolveRepeatMultiplier(TimeSpan.Zero));
        Assert.AreEqual(1, SliderAdjustmentPolicy.ResolveRepeatMultiplier(SliderAdjustmentPolicy.AccelerationStartDelay));
        Assert.AreEqual(2, SliderAdjustmentPolicy.ResolveRepeatMultiplier(
            SliderAdjustmentPolicy.AccelerationStartDelay + SliderAdjustmentPolicy.AccelerationDoublingPeriod));
        Assert.AreEqual(4, SliderAdjustmentPolicy.ResolveRepeatMultiplier(
            SliderAdjustmentPolicy.AccelerationStartDelay + SliderAdjustmentPolicy.AccelerationDoublingPeriod * 2));

        // 单调不减，并封顶。
        // It never decreases and it is capped.
        var previous = 1;
        for (var milliseconds = 0; milliseconds <= 20000; milliseconds += 100)
        {
            var multiplier = SliderAdjustmentPolicy.ResolveRepeatMultiplier(TimeSpan.FromMilliseconds(milliseconds));
            Assert.IsTrue(multiplier >= previous, "长按倍率不得随时间回落。");
            Assert.IsTrue(multiplier <= SliderAdjustmentPolicy.MaximumRepeatMultiplier, "长按倍率必须封顶。");
            previous = multiplier;
        }

        Assert.AreEqual(
            SliderAdjustmentPolicy.MaximumRepeatMultiplier,
            SliderAdjustmentPolicy.ResolveRepeatMultiplier(TimeSpan.FromMinutes(5)));
    }

    [TestMethod]
    public void RepeatStepNeverExceedsTheSliderRange()
    {
        Assert.AreEqual(10, SliderAdjustmentPolicy.ResolveRepeatStep(10, TimeSpan.Zero, 10, 400));
        Assert.AreEqual(390, SliderAdjustmentPolicy.ResolveRepeatStep(
            30,
            SliderAdjustmentPolicy.AccelerationStartDelay + SliderAdjustmentPolicy.AccelerationDoublingPeriod * 4,
            10,
            400),
            "一个采样步长超过区间宽度时，最多也只能走完整个区间。");
        Assert.AreEqual(0, SliderAdjustmentPolicy.ResolveRepeatStep(0, TimeSpan.Zero, 0, 100));
    }

    /// <summary>
    /// 字体粗细与频谱灵敏度改用明确的步进：前者只有九个真实字重，后者按 10 步进，两者都必须落在网格上。
    /// The font weight and spectrum sensitivity use explicit steps now: the first has only nine real weights and the second steps
    /// by ten, and both must land on their grid.
    /// </summary>
    [TestMethod]
    public void WeightAndSensitivityStepsKeepTheSlidersOnTheirGrid()
    {
        Assert.AreEqual(100, AppearanceSettings.MinimumFontWeight);
        Assert.AreEqual(900, AppearanceSettings.MaximumFontWeight);
        Assert.AreEqual(100, AppearanceSettings.FontWeightStep);
        Assert.AreEqual(100, SliderAdjustmentPolicy.ResolveBaseStep(
            AppearanceSettings.MinimumFontWeight,
            AppearanceSettings.MaximumFontWeight,
            AppearanceSettings.FontWeightStep,
            0.1));
        for (var step = 0; step <= 8; step++)
        {
            Assert.AreEqual(
                AppearanceSettings.MinimumFontWeight + step * AppearanceSettings.FontWeightStep,
                AppearanceSettings.SnapFontWeight(AppearanceSettings.MinimumFontWeight + step * AppearanceSettings.FontWeightStep));
        }

        Assert.AreEqual(10, SpectrumComponentSettings.MinimumSensitivityPercent);
        Assert.AreEqual(400, SpectrumComponentSettings.MaximumSensitivityPercent);
        Assert.AreEqual(10, SpectrumComponentSettings.SensitivityStepPercent);
        Assert.AreEqual(10, SpectrumComponentSettings.SnapSensitivityPercent(9));
        Assert.AreEqual(10, SpectrumComponentSettings.SnapSensitivityPercent(14));
        Assert.AreEqual(20, SpectrumComponentSettings.SnapSensitivityPercent(16));
        Assert.AreEqual(400, SpectrumComponentSettings.SnapSensitivityPercent(1000));
        Assert.AreEqual(
            new SpectrumComponentSettings(9, 20, 10),
            new SpectrumComponentSettings(9, 20, 1).Normalize());
    }
}
