namespace AFMediaBar.Classes.Settings;

/// <summary>播放器文字颜色模式。 / Player text color mode.</summary>
public enum PlayerForegroundMode
{
    Automatic = 0,
    LightText = 1,
    DarkText = 2
}

/// <summary>应用主题模式。 / Application theme mode.</summary>
public enum ApplicationThemeMode
{
    Automatic = 0,
    Light = 1,
    Dark = 2
}

/// <summary>应用窗口和菜单使用的背景材质。 / Background material used by application windows and menus.</summary>
public enum ApplicationBackdropMode
{
    FluentSolid = 0,
    Mica = 1,
    Acrylic = 2
}

/// <summary>西文字体预设。 / Latin font preset.</summary>
public enum LatinFontPreset
{
    SegoeUi = 0,
    Arial = 1,
    Calibri = 2,
    Verdana = 3,
    Consolas = 4,
    TimesNewRoman = 5
}

/// <summary>中文字体预设。 / CJK font preset.</summary>
public enum CjkFontPreset
{
    SystemDefault = 0,
    MicrosoftYaHei = 1,
    DengXian = 2,
    SimSun = 3,
    SimHei = 4,
    KaiTi = 5,
    FangSong = 6
}

/// <summary>
/// 集中保存外观页设置，并生成稳定的 WPF 字体回退链。
/// Stores appearance-page settings and builds a stable WPF font fallback chain.
/// </summary>
public readonly record struct AppearanceSettings(
    LatinFontPreset LatinFont,
    CjkFontPreset CjkFont,
    int FontWeight,
    PlayerForegroundMode PlayerForegroundMode,
    // 保留旧 JSON 字段以避免设置 schema 迁移；该选项已从 UI 和呈现逻辑移除。
    // Retain the legacy JSON field to avoid a settings-schema migration; the option is no longer presented or applied.
    bool EnhancedReadability,
    ApplicationThemeMode ApplicationThemeMode,
    ApplicationBackdropMode BackdropMode)
{
    /// <summary>
    /// 字体粗细下限。取 OpenType 的 Thin：WPF 只有 100–900 九个真实字重，界面按 100 步进在三者之间取值，
    /// 因此下限必须落在真实字重上，否则滑杆会停在一个会被就近取整的值上。
    /// Lower font-weight bound: OpenType Thin. WPF has only the nine real weights from 100 to 900, the interface steps by 100
    /// across them, and the bound therefore has to land on a real weight instead of a value that is only rounded to one.
    /// </summary>
    public const int MinimumFontWeight = 100;

    /// <summary>字体粗细上限（OpenType Black）。 / Upper font-weight bound (OpenType Black).</summary>
    public const int MaximumFontWeight = 900;

    /// <summary>
    /// 字体粗细滑杆的步进。间距必须等于真实字重的间隔：100–900 之间只有九个字形族，
    /// 用 50 或 10 步进会产生大量看起来完全相同的取值。
    /// Step of the font-weight slider. The spacing must equal the real weight interval: only nine typeface weights exist between
    /// 100 and 900, so stepping by 50 or 10 would produce many values that look exactly alike.
    /// </summary>
    public const int FontWeightStep = 100;

    public static AppearanceSettings Default { get; } = new(
        LatinFontPreset.SegoeUi,
        CjkFontPreset.SystemDefault,
        400,
        PlayerForegroundMode.Automatic,
        false,
        ApplicationThemeMode.Automatic,
        ApplicationBackdropMode.Mica);

    public AppearanceSettings Normalize()
    {
        var defaults = Default;
        var normalized = this with
        {
            LatinFont = Enum.IsDefined(LatinFont) ? LatinFont : defaults.LatinFont,
            CjkFont = Enum.IsDefined(CjkFont) ? CjkFont : defaults.CjkFont,
            PlayerForegroundMode = Enum.IsDefined(PlayerForegroundMode) ? PlayerForegroundMode : defaults.PlayerForegroundMode,
            ApplicationThemeMode = Enum.IsDefined(ApplicationThemeMode) ? ApplicationThemeMode : defaults.ApplicationThemeMode,
            BackdropMode = Enum.IsDefined(BackdropMode) ? BackdropMode : defaults.BackdropMode,
            FontWeight = SnapFontWeight(FontWeight)
        };
        return normalized;
    }

    /// <summary>
    /// 把任意粗细吸附到最近的真实字重并夹取。旧设置文件里可能存在 350 或 250 这类值，
    /// 不吸附的话界面会显示一个滑杆位置无法表达的读数。
    /// Snaps any weight onto the nearest real weight and clamps it. Older settings files may hold values such as 350 or 250, and
    /// without this snap the interface would show a reading its own slider position cannot express.
    /// </summary>
    /// <param name="fontWeight">待换算的粗细。/ Weight to normalize.</param>
    public static int SnapFontWeight(int fontWeight)
    {
        var snapped = (int)Math.Round(fontWeight / (double)FontWeightStep, MidpointRounding.AwayFromZero) * FontWeightStep;
        return Math.Clamp(snapped, MinimumFontWeight, MaximumFontWeight);
    }

    /// <summary>生成西文优先、中文和东亚字符回退在后的字体链。 / Builds a Latin-first fallback chain with CJK coverage.</summary>
    public string ResolveFontFamilySource(string systemFontFamily)
    {
        var latin = LatinFont switch
        {
            LatinFontPreset.Arial => "Arial",
            LatinFontPreset.Calibri => "Calibri",
            LatinFontPreset.Verdana => "Verdana",
            LatinFontPreset.Consolas => "Consolas",
            LatinFontPreset.TimesNewRoman => "Times New Roman",
            _ => "Segoe UI Variable Text, Segoe UI"
        };
        var cjk = CjkFont switch
        {
            CjkFontPreset.MicrosoftYaHei => "Microsoft YaHei UI",
            CjkFontPreset.DengXian => "DengXian",
            CjkFontPreset.SimSun => "SimSun",
            CjkFontPreset.SimHei => "SimHei",
            CjkFontPreset.KaiTi => "KaiTi",
            CjkFontPreset.FangSong => "FangSong",
            _ => systemFontFamily
        };

        return string.Join(", ", new[]
        {
            latin,
            cjk,
            "Microsoft JhengHei UI",
            "Yu Gothic UI",
            "Malgun Gothic"
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
