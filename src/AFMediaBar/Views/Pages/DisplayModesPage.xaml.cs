using System.Windows.Controls;
using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>显示模式设置页面。 / Display-mode settings page.</summary>
public partial class DisplayModesPage : INavigableView<DisplayModesViewModel>
{
    public DisplayModesViewModel ViewModel { get; }
    public DisplayModesPage(DisplayModesViewModel viewModel) { ViewModel = viewModel; DataContext = this; InitializeComponent(); }
    /// <summary>页面首次加载时执行入场揭示；不可见分组不参与动画。/ Reveals the page on first load; collapsed groups are not animated.</summary>
    private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);
    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    { if (await SettingsResetDialog.ConfirmAsync("显示模式页")) ViewModel.ResetDisplayModes(); }
}
