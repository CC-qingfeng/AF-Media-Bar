using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>
/// 外观页 ViewModel：管理字体、播放器文字、应用主题和窗口材质设置。
/// Appearance-page ViewModel: manages font, player text, application theme, and window material settings.
/// </summary>
public partial class AppearanceViewModel : ObservableObject
{
    private LatinFontPreset _latinFont;
    private CjkFontPreset _cjkFont;
    private int _fontWeight;
    private PlayerForegroundMode _playerForegroundMode;
    private ApplicationThemeMode _applicationThemeMode;
    private ApplicationBackdropMode _backdropMode;
    private bool _isRefreshing;

    public AppearanceViewModel()
    {
        var appearance = SettingsManager.Current.Appearance.Normalize();
        _latinFont = appearance.LatinFont;
        _cjkFont = appearance.CjkFont;
        _fontWeight = appearance.FontWeight;
        _playerForegroundMode = appearance.PlayerForegroundMode;
        _applicationThemeMode = appearance.ApplicationThemeMode;
        _backdropMode = appearance.BackdropMode;
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    public LatinFontPreset LatinFont
    {
        get => _latinFont;
        set
        {
            if (SetProperty(ref _latinFont, value))
            {
                if (!_isRefreshing) Publish();
            }
        }
    }

    public CjkFontPreset CjkFont
    {
        get => _cjkFont;
        set
        {
            if (SetProperty(ref _cjkFont, value))
            {
                if (!_isRefreshing) Publish();
            }
        }
    }

    public int FontWeight
    {
        get => _fontWeight;
        set
        {
            value = Math.Clamp(value, AppearanceSettings.MinimumFontWeight, AppearanceSettings.MaximumFontWeight);
            if (SetProperty(ref _fontWeight, value))
            {
                if (!_isRefreshing) Publish();
            }
        }
    }

    /// <summary>
    /// 播放器文字颜色模式。写入即发布设置，因此下拉框可以直接双向绑定；
    /// 原有的 <c>SetPlayerForegroundModeCommand</c> 仍走同一条写入路径，两条入口行为一致。
    /// Player text colour mode. Writing publishes the setting, so a select can bind two-way; the existing
    /// <c>SetPlayerForegroundModeCommand</c> still runs through the same write path, so both entries behave alike.
    /// </summary>
    public PlayerForegroundMode PlayerForegroundMode
    {
        get => _playerForegroundMode;
        set
        {
            if (SetProperty(ref _playerForegroundMode, value))
            {
                if (!_isRefreshing) Publish();
            }
        }
    }

    /// <summary>应用主题模式。写入即发布设置，供下拉框双向绑定。/ Application theme mode. Writing publishes the setting for a two-way select.</summary>
    public ApplicationThemeMode ApplicationThemeMode
    {
        get => _applicationThemeMode;
        set
        {
            if (SetProperty(ref _applicationThemeMode, value))
            {
                if (!_isRefreshing) Publish();
            }
        }
    }

    /// <summary>窗口背景材质。写入即发布设置，供下拉框双向绑定。/ Window backdrop material. Writing publishes the setting for a two-way select.</summary>
    public ApplicationBackdropMode BackdropMode
    {
        get => _backdropMode;
        set
        {
            if (SetProperty(ref _backdropMode, value))
            {
                Publish();
            }
        }
    }

    /// <summary>
    /// 静置层媒体文字（标题、歌手、歌词）的字号缩放百分比。该值存在任务栏体验设置里，但按界面归属由本页承载：
    /// 「恢复本页默认设置」因此会连同它一起复位。
    /// Font-size scale percentage for the rest-layer media text (title, artist, lyrics). The value lives in the taskbar
    /// experience settings but this page owns it in the interface, so "restore this page's defaults" resets it as well.
    /// </summary>
    public int MediaFontSizePercent
    {
        get => SettingsManager.Current.TaskbarExperience.Normalize().MediaFontSizePercent;
        set
        {
            if (_isRefreshing || value == MediaFontSizePercent)
                return;

            SettingsManager.SetTaskbarExperienceSettings(
                SettingsManager.Current.TaskbarExperience with { MediaFontSizePercent = value });
            OnPropertyChanged();
        }
    }

    /// <summary>当前桌面环境的动效级别。/ Current motion level for the desktop environment.</summary>
    public string MotionModeText => MotionPolicy.ResolveCurrent().Mode switch
    {
        MotionMode.Full => "完整动效",
        MotionMode.Reduced => "轻量动效",
        _ => "即时更新"
    };

    /// <summary>当前动效策略的简短说明。/ Short explanation of the current motion policy.</summary>
    public string MotionDetailText => MotionPolicy.ResolveCurrent().Mode switch
    {
        MotionMode.Full => "保留展开、反馈和频谱过渡",
        MotionMode.Reduced => "已关闭模糊、跑马灯和连续频谱",
        _ => "跟随系统设置，避免过渡延迟"
    };

    [RelayCommand]
    private void SetPlayerForegroundMode(PlayerForegroundMode mode) => PlayerForegroundMode = mode;

    [RelayCommand]
    private void SetApplicationThemeMode(ApplicationThemeMode mode) => ApplicationThemeMode = mode;

    [RelayCommand]
    private void SetBackdropMode(ApplicationBackdropMode mode) => BackdropMode = mode;

    private void Publish() => SettingsManager.SetAppearanceSettings(new AppearanceSettings(
        LatinFont,
        CjkFont,
        FontWeight,
        PlayerForegroundMode,
        false,
        ApplicationThemeMode,
        BackdropMode));

    public void ResetAppearance() => SettingsManager.ResetAppearance();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        // 媒体文字大小存在任务栏体验设置里，因此它的外部变化（例如显示模式页的重置）也要回写本页读数。
        // The media text size lives in the taskbar experience settings, so an external change to it (the display-mode page's
        // reset, for example) must be reflected in this page's reading too.
        if (e.ResetScope is not (SettingsResetScope.Appearance or SettingsResetScope.All) &&
            e.PropertyName != nameof(AppSettings.TaskbarExperience))
            return;

        var appearance = SettingsManager.Current.Appearance;
        _isRefreshing = true;
        try
        {
            LatinFont = appearance.LatinFont;
            CjkFont = appearance.CjkFont;
            FontWeight = appearance.FontWeight;
            PlayerForegroundMode = appearance.PlayerForegroundMode;
            ApplicationThemeMode = appearance.ApplicationThemeMode;
            BackdropMode = appearance.BackdropMode;
            MediaFontSizePercent = SettingsManager.Current.TaskbarExperience.Normalize().MediaFontSizePercent;
        }
        finally { _isRefreshing = false; }
    }
}
