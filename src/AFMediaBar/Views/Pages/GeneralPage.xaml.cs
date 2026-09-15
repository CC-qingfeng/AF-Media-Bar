using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using AFMediaBar.Classes.Utils;
using AFMediaBar.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Views.Pages
{
    /// <summary>
    /// 常规设置页：承载生命周期、语言、更新和设置文件入口。
    /// General settings page hosting lifecycle, language, update, and settings-file entry points.
    /// </summary>
    public partial class GeneralPage : INavigableView<GeneralViewModel>
    {
        public GeneralViewModel ViewModel { get; }
        private readonly SettingsPersistenceService _persistence;

        public GeneralPage(GeneralViewModel viewModel, SettingsPersistenceService persistence)
        {
            ViewModel = viewModel;
            _persistence = persistence;
            DataContext = this;

            InitializeComponent();
        }

        /// <summary>页面首次加载时执行入场揭示；重复加载由执行器自行忽略。/ Reveals the page on first load; repeat loads are ignored by the animator.</summary>
        private void OnPageLoaded(object sender, RoutedEventArgs e) => SettingsRevealAnimator.Play(sender as Panel);

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (await SettingsResetDialog.ConfirmAsync("常规页")) ViewModel.ResetGeneral();
        }

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e) => _persistence.OpenSettingsFolder();
    }
}
