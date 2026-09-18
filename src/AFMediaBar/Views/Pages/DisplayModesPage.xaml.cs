using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>显示模式设置页面。 / Display-mode settings page.</summary>
public partial class DisplayModesPage : INavigableView<DisplayModesViewModel>
{
    private readonly StackPanel[] _modeSections;
    private string _currentSectionName = string.Empty;

    public DisplayModesViewModel ViewModel { get; }

    public DisplayModesPage(DisplayModesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        // 用名字而不是页面里的字段声明顺序来配模式与分区，顺序变化时不会静默错配。
        // Sections are matched to modes by name rather than by declaration order, so reordering the page cannot
        // silently pair a mode with the wrong section.
        _modeSections = [TaskbarModeSection, DynamicIslandModeSection, DesktopCardModeSection, FloatingBallModeSection];
    }

    /// <summary>
    /// 页面首次加载时执行入场揭示，并跟随页面内模式的切换。
    ///
    /// 切换模式换的是整块内容，因此三件事必须一起做：分组标签条重建（否则会列出另一个模式的分组）、
    /// 新显示出来的分区重放一次入场揭示（否则内容瞬间替换）、滚动回到顶部（否则会停在上一段内容的位置）。
    /// Reveals the page on first load and follows in-page mode switches.
    ///
    /// A mode switch swaps the whole content block, so three things happen together: the group strip is rebuilt
    /// (or it would list the other mode's groups), the newly visible section replays its entrance reveal (or the
    /// content would simply snap), and the scroller returns to the top (or it would land inside the previous
    /// content).
    /// </summary>
    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        SettingsRevealAnimator.Play(sender as Panel);
        ApplyModeSection();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DisplayModesViewModel.SelectedMode)
            or nameof(DisplayModesViewModel.IsTaskbarMode)
            or nameof(DisplayModesViewModel.IsDynamicIslandMode)
            or nameof(DisplayModesViewModel.IsDesktopCardMode)
            or nameof(DisplayModesViewModel.IsFloatingBallMode))
        {
            ApplyModeSection();
        }
    }

    /// <summary>重建标签条、重放揭示并回到顶部，让页面与当前选择的模式一致。/ Rebuilds the tabs, replays the reveal, and returns to the top so the page matches the selected mode.</summary>
    private void ApplyModeSection()
    {
        Strip.Rebuild();

        var visible = _modeSections.FirstOrDefault(section => section.Visibility == Visibility.Visible);
        if (visible is null || string.Equals(_currentSectionName, visible.Name, StringComparison.Ordinal))
        {
            return;
        }

        _currentSectionName = visible.Name;
        SettingsRevealAnimator.Replay(visible);
        PageScroll.ScrollToTop();
    }

    /// <summary>
    /// 把页面切到指定模式分区，供搜索命中跳转使用。
    /// 模式选择是页面自己的状态，因此这个映射留在页面里，设置窗口不需要知道有哪些命令。
    /// Switches the page to the given mode section, for a search hit to land on.
    /// Mode selection is the page's own state, so the mapping lives here and the settings window never has to
    /// know which commands exist.
    /// </summary>
    /// <param name="mode">目标模式分区。/ Target mode section.</param>
    public void SelectMode(SettingsSearchMode mode)
    {
        switch (mode)
        {
            case SettingsSearchMode.DynamicIsland:
                ViewModel.SwitchToDynamicIslandModeCommand.Execute(null);
                break;
            case SettingsSearchMode.DesktopCard:
                ViewModel.SwitchToDesktopCardModeCommand.Execute(null);
                break;
            case SettingsSearchMode.FloatingBall:
                ViewModel.SwitchToFloatingBallModeCommand.Execute(null);
                break;
            default:
                ViewModel.SwitchToTaskbarModeCommand.Execute(null);
                break;
        }
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        // 传的是作用域名称的文案键而不是名称本身：句子由语言文件拼装，页面不参与翻译。
        // The argument is the localization key of the scope name rather than the name itself: the language file composes
        // the sentence and the page never takes part in translation.
        if (await SettingsResetDialog.ConfirmAsync("Common.Page.DisplayModes")) ViewModel.ResetDisplayModes();
    }
}
