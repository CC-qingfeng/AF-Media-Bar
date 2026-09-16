namespace AFMediaBar.Views.Pages;

using Wpf.Ui;
using Wpf.Ui.Controls;

internal static class SettingsResetDialog
{
    private static readonly ContentDialogService DialogService = new();

    public static void SetHost(ContentDialogHost host) => DialogService.SetDialogHost(host);

    public static async Task<bool> ConfirmAsync(string scope)
    {
        var dialog = new ContentDialog
        {
            Title = "恢复默认设置",
            Content = $"将重置「{scope}」中的全部选项，其它页面与当前播放不受影响。",
            PrimaryButtonText = "恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        return await DialogService.ShowAsync(dialog, CancellationToken.None) == ContentDialogResult.Primary;
    }
}
