using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AFMediaBar.Components;

/// <summary>
/// 父卡片内部的子区域：把“必须先启用父项才能生效”的选项嵌在父项里面，而不是摊平成另一张平级卡片。
///
/// 关闭状态有两种表达，由 <see cref="CollapseWhenDisabled"/> 选择：
/// 折叠用于悬停层和完整层，因为运行时不变量要求关闭后其详细设置折叠；
/// 变暗加芯片用于歌词第二行和未实现的承载模式，因为那些选项在父项关闭时仍然值得被看到。
/// A child region inside a parent card, so that options which only take effect once the parent is enabled
/// are nested inside that parent instead of becoming another sibling card.
///
/// A disabled region is expressed one of two ways, chosen by <see cref="CollapseWhenDisabled"/>: collapse
/// for the hover and full layers, because a runtime invariant requires their detail settings to fold away,
/// and dim-plus-chip for the lyric second line and the unimplemented hosting modes, because those options
/// remain worth seeing while the parent is off.
/// </summary>
public class SettingsSubRegion : HeaderedContentControl
{
    /// <summary>关闭状态下是否整体折叠。/ Whether the region collapses while it is disabled.</summary>
    public static readonly DependencyProperty CollapseWhenDisabledProperty = DependencyProperty.Register(
        nameof(CollapseWhenDisabled),
        typeof(bool),
        typeof(SettingsSubRegion),
        new PropertyMetadata(false));

    /// <summary>父项状态，决定子区域是可用、仅变暗还是折叠。/ Parent state; decides whether the region is usable, dimmed, or collapsed.</summary>
    public static readonly DependencyProperty IsSubRegionEnabledProperty = DependencyProperty.Register(
        nameof(IsSubRegionEnabled),
        typeof(bool),
        typeof(SettingsSubRegion),
        new PropertyMetadata(true, OnIsSubRegionEnabledChanged));

    /// <summary>子区域说明，用来说明该子区域与父项的关系。/ Sub-region description, explaining how it relates to its parent.</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingsSubRegion),
        new PropertyMetadata(string.Empty));

    /// <summary>状态芯片文本；为空时用默认的“需先启用”。/ Status chip text; empty falls back to the default "needs enabling".</summary>
    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText),
        typeof(string),
        typeof(SettingsSubRegion),
        new PropertyMetadata(string.Empty));

    /// <summary>状态芯片语气。/ Tone of the status chip.</summary>
    public static readonly DependencyProperty StatusToneProperty = DependencyProperty.Register(
        nameof(StatusTone),
        typeof(SettingsChipTone),
        typeof(SettingsSubRegion),
        new PropertyMetadata(SettingsChipTone.Attention));

    private FrameworkElement? _contentHost;

    /// <summary>关闭时是否折叠整个子区域。/ Whether the whole region collapses while disabled.</summary>
    public bool CollapseWhenDisabled
    {
        get => (bool)GetValue(CollapseWhenDisabledProperty);
        set => SetValue(CollapseWhenDisabledProperty, value);
    }

    /// <summary>父项是否已启用。/ Whether the parent is enabled.</summary>
    public bool IsSubRegionEnabled
    {
        get => (bool)GetValue(IsSubRegionEnabledProperty);
        set => SetValue(IsSubRegionEnabledProperty, value);
    }

    /// <summary>子区域说明。/ Sub-region description.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>状态芯片文本。/ Status chip text.</summary>
    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    /// <summary>状态芯片语气。/ Tone of the status chip.</summary>
    public SettingsChipTone StatusTone
    {
        get => (SettingsChipTone)GetValue(StatusToneProperty);
        set => SetValue(StatusToneProperty, value);
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _contentHost = GetTemplateChild("PART_Content") as FrameworkElement;

        // 模板换过之后已经处于启用状态的子区域不会再收到属性变更通知，因此这里补一次终值，
        // 否则重新应用模板会把内容留在揭示动画的起始透明度上。
        // A region that is already enabled will not receive another property change after the template is
        // reapplied, so land the final value here; otherwise the content would stay at the reveal's start.
        if (_contentHost is not null && IsSubRegionEnabled)
        {
            Settle(_contentHost);
        }
    }

    private static void OnIsSubRegionEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is SettingsSubRegion region && args.NewValue is true)
        {
            region.PlayReveal();
        }
    }

    /// <summary>
    /// 启用时用一次 200 ms 的透明度加位移揭示，让“选项现在生效了”这件事可见。
    /// 只用 Opacity 与 RenderTransform：两者都不触发布局与重绘。
    /// On enable, a 200 ms opacity plus offset reveal makes "these options are live now" visible. It writes
    /// only Opacity and RenderTransform, the two properties that skip layout and paint.
    /// </summary>
    private void PlayReveal()
    {
        if (_contentHost is null || CollapseWhenDisabled)
        {
            return;
        }

        var transform = _contentHost.RenderTransform as TranslateTransform;
        if (transform is null)
        {
            transform = new TranslateTransform();
            _contentHost.RenderTransform = transform;
        }

        // 起始值写成动画的起点而不是本地值：延迟为零时本地值会在动画被清除后残留。
        // Begin from the animation rather than a local value: with a zero delay a local start value would
        // survive once the animation is cleared.
        var opacity = new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        opacity.Completed += (_, _) => Settle(_contentHost);

        var translate = new DoubleAnimation(8d, 0d, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };

        _contentHost.BeginAnimation(UIElement.OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(TranslateTransform.YProperty, translate, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// 清除动画后写本地终值。顺序不能反过来：先写终值再清除动画会把元素回退到动画起始时快照的值。
    /// Clears the animation before writing the final local value. The order cannot be reversed: writing the
    /// final value first and clearing afterwards rolls the element back to the value snapshotted at the start.
    /// </summary>
    private static void Settle(FrameworkElement host)
    {
        host.BeginAnimation(UIElement.OpacityProperty, null);
        host.Opacity = 1d;

        if (host.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = 0d;
        }
    }
}
