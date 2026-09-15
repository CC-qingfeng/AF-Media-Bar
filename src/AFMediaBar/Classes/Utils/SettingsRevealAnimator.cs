using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Classes.Utils;

/// <summary>
/// 设置页入场揭示执行器：按 <see cref="SettingsRevealPolicy"/> 解析出的参数，对页面根容器的直接子块执行
/// 透明度与纵向位移动画。完成后把元素留在终值上并清除动画，避免动画值长期驻留在可视树上。
/// Applies what <see cref="SettingsRevealPolicy"/> resolves to the direct children of a settings page root:
/// opacity plus a vertical offset. Afterwards the element rests on its final value with no animation left
/// attached, so no animated value lingers on the visual tree.
///
/// 只写 Opacity 和 RenderTransform：两者都不触发布局和重绘，这是 WPF 侧唯一等于“只动画 transform 和 opacity”的等价做法。
/// 元素已经自带非位移变换（例如选项卡片的按下缩放）时只保留透明度，因为覆盖 RenderTransform 会破坏该控件的自身反馈。
/// Only Opacity and RenderTransform are written, since those are the only WPF properties that skip layout and
/// paint. When an element already carries a non-translate transform (a choice card's press scale, for example),
/// only the opacity runs, because overwriting RenderTransform would destroy that control's own feedback.
/// </summary>
public static class SettingsRevealAnimator
{
    /// <summary>每个页面根容器只揭示一次；重复触发的 Loaded 不再重放动画。/ A page root reveals once; repeated Loaded events do not replay it.</summary>
    private static readonly DependencyProperty HasRevealedProperty =
        DependencyProperty.RegisterAttached(
            "HasRevealed",
            typeof(bool),
            typeof(SettingsRevealAnimator),
            new PropertyMetadata(false));

    /// <summary>与 SettingsAppearanceResources.xaml 中 AfSplineEaseOut 保持一致的强 ease-out 兜底曲线。</summary>
    /// <remarks>资源缺失时仍然使用同一曲线，避免动画在字典未合并时退化成默认缓动。</remarks>
    private static readonly KeySpline FallbackEaseOut = new(0.23d, 1d, 0.32d, 1d);

    /// <summary>
    /// 对根容器的直接子块执行入场揭示；空引用、已揭示和无需动画的情况都是无操作。
    /// Reveals the direct children of the host panel. Null hosts, already-revealed pages, and motion levels that
    /// resolve to no animation are all no-ops.
    /// </summary>
    /// <param name="host">页面根容器，通常是设置页最外层的 StackPanel。/ Page root, normally the outermost settings StackPanel.</param>
    public static void Play(Panel? host)
    {
        if (host is null || (bool)host.GetValue(HasRevealedProperty))
            return;

        host.SetValue(HasRevealedProperty, true);

        var motion = MotionPolicy.ResolveCurrent();
        var spline = ResolveEaseOut();

        for (var index = 0; index < host.Children.Count; index++)
        {
            if (host.Children[index] is not UIElement block)
                continue;

            var reveal = SettingsRevealPolicy.Resolve(index, motion);
            if (!reveal.ShouldAnimate || reveal.Duration <= TimeSpan.Zero)
                continue;

            PlayBlock(block, reveal, spline);
        }
    }

    private static void PlayBlock(UIElement block, SettingsReveal reveal, KeySpline spline)
    {
        block.BeginAnimation(UIElement.OpacityProperty, null);

        // 位移只在策略给出了非零偏移时才写；减少动效时元素根本不会被挂上变换。
        // The translate is only wired when the policy asks for a non-zero offset, so reduced motion never
        // attaches a transform at all.
        var offset = ResolveOffsetTransform(block, reveal.OffsetY);
        offset?.BeginAnimation(TranslateTransform.YProperty, null);

        // 起始值写成本地值。错峰块的动画在延迟期间处于“开始之前”阶段，这一阶段生效的正是本地值，
        // 所以块在轮到自己之前保持不可见；若把本地值设成终值，它会先以完全可见闪一下再重新淡入。
        // The start state is the local value. A staggered block's animation sits in its before-period during the
        // delay, and the local value is what applies there, so the block stays hidden until its turn. Writing the
        // final value as the local value instead would flash it fully visible before fading it back in.
        block.Opacity = 0d;
        if (offset is not null)
            offset.Y = reveal.OffsetY;

        void Settle()
        {
            // HoldEnd 在结束瞬间已经停在终值上，因此先落到本地终值再清除动画不会产生回弹。
            // HoldEnd already rests on the end value when this runs, so landing the local value before clearing the
            // animation cannot bounce.
            block.Opacity = 1d;
            if (offset is not null)
                offset.Y = 0d;

            block.BeginAnimation(UIElement.OpacityProperty, null);
            offset?.BeginAnimation(TranslateTransform.YProperty, null);
        }

        block.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(1d, reveal, spline, Settle),
            HandoffBehavior.SnapshotAndReplace);

        if (offset is not null)
        {
            offset.BeginAnimation(
                TranslateTransform.YProperty,
                CreateAnimation(0d, reveal, spline, onCompleted: null),
                HandoffBehavior.SnapshotAndReplace);
        }
    }

    /// <summary>
    /// 在元素没有自带其它变换时挂上位移变换；自带变换时返回 null，只保留透明度揭示。
    /// Attaches a translate transform when the element carries none, and returns null otherwise so only the
    /// opacity reveals.
    /// </summary>
    private static TranslateTransform? ResolveOffsetTransform(UIElement block, double offsetY)
    {
        if (offsetY == 0d)
            return null;

        return block.RenderTransform switch
        {
            null => Attach(block),
            TranslateTransform existing => existing,
            _ => null
        };

        static TranslateTransform Attach(UIElement target)
        {
            var created = new TranslateTransform();
            target.RenderTransform = created;
            return created;
        }
    }

    private static DoubleAnimationUsingKeyFrames CreateAnimation(
        double to,
        SettingsReveal reveal,
        KeySpline spline,
        Action? onCompleted)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = reveal.Delay > TimeSpan.Zero ? reveal.Delay : null,
            Duration = reveal.Duration,
            FillBehavior = FillBehavior.HoldEnd
        };

        // 起点关键帧必须显式存在：否则首个关键帧之前的时间段会保持本地值，动画会看起来先等一段再突变。
        // The start keyframe must be explicit, otherwise the span before the first keyframe holds the local value
        // and the animation would appear to wait and then jump.
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(reveal.Duration), spline));

        if (onCompleted is not null)
            animation.Completed += (_, _) => onCompleted();

        return animation;
    }

    private static KeySpline ResolveEaseOut() =>
        Application.Current?.TryFindResource("AfSplineEaseOut") as KeySpline ?? FallbackEaseOut;
}
