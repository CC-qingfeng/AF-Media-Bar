using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using Microsoft.Win32;

namespace AFMediaBar.Views.Pages;

/// <summary>额外功能设置页。/ Settings page for auxiliary features.</summary>
public partial class ExtraFeaturesPage : INavigableView<ExtraFeaturesViewModel>
{
    public ExtraFeaturesViewModel ViewModel { get; }

    public ExtraFeaturesPage(ExtraFeaturesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    /// <summary>页面首次加载时执行入场揭示。/ Reveals the page on first load.</summary>
    private void OnPageLoaded(object sender, System.Windows.RoutedEventArgs e) =>
        SettingsRevealAnimator.Play(sender as System.Windows.Controls.Panel);

    private void BrowseQuickLaunch_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择媒体应用或快捷方式",
            Filter = "应用和快捷方式 (*.exe;*.lnk)|*.exe;*.lnk",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
            ViewModel.AddQuickLaunchFile(dialog.FileName);
    }

    private async void ResetButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (await SettingsResetDialog.ConfirmAsync("媒体与通知"))
            ViewModel.ResetExtraFeatures();
    }
}
