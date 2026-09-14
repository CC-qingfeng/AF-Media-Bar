using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace AFMediaBar.Views.Pages;

/// <summary>额外功能设置页。/ Settings page for auxiliary features.</summary>
public partial class ExtraFeaturesPage : INavigableView<DisplayModesViewModel>
{
    public DisplayModesViewModel ViewModel { get; }

    public ExtraFeaturesPage(DisplayModesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private async void ResetButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (await SettingsResetDialog.ConfirmAsync("额外功能页"))
            ViewModel.ResetExtraFeatures();
    }
}
