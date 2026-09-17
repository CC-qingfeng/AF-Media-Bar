using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Models.Layout;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace AFMediaBar.Classes.Services;

/// <summary>负责用户设置 JSON 的加载、恢复、原子保存和防抖。 / Owns loading, recovery, atomic saving and debouncing of user settings JSON.</summary>
public sealed class SettingsPersistenceService : IDisposable
{
    public const int CurrentSchemaVersion = 12;
    private readonly string _directoryPath;
    private readonly string _settingsPath;
    private readonly string _backupPath;
    private readonly string _userDefaultsPath;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
    private Timer? _timer;
    private bool _initialized;
    private bool _disposed;
    private int? _loadedSchemaVersion;

    /// <summary>schema 1–3 中等待启动阶段映射的旧任务栏显示器索引。 / Legacy taskbar-display index from schema 1–3 awaiting startup-time mapping.</summary>
    public int? LegacyTaskbarMonitorIndex { get; private set; }

    /// <summary>创建设置存储服务；目录和防抖间隔可覆盖以便测试。 / Creates the settings store; directory and debounce can be overridden for tests.</summary>
    public SettingsPersistenceService(string? directoryPath = null, TimeSpan? debounce = null)
    {
        _directoryPath = directoryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMediaBar");
        _settingsPath = Path.Combine(_directoryPath, "settings.json");
        _backupPath = _settingsPath + ".bak";
        _userDefaultsPath = Path.Combine(_directoryPath, "user-defaults.json");
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
        _jsonOptions.Converters.Add(new LenientEnumConverterFactory());
    }

    public string SettingsPath => _settingsPath;

    /// <summary>在 Host 启动前加载设置并订阅后续变化。 / Loads settings before Host startup and subscribes to later changes.</summary>
    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized) return;
            _initialized = true;
            LoadCore();
            SettingsManager.SettingsChanged += OnSettingsChanged;
        }
    }

    /// <summary>打开设置所在目录。 / Opens the settings directory.</summary>
    public void OpenSettingsFolder()
    {
        Directory.CreateDirectory(_directoryPath);
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{_directoryPath}\"", UseShellExecute = true });
    }

    /// <summary>等待当前防抖写入并同步落盘。 / Flushes the pending debounced write synchronously.</summary>
    public void Flush()
    {
        Timer? timer;
        lock (_gate)
        {
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
        SaveCore(SettingsManager.Current);
    }

    /// <summary>
    /// 把当前设置保存为「我的默认设置」：写入独立的快照文件，并把内存里生效的默认值换成它。
    /// 设置本身不变，因此保存默认不会打断用户当前的使用状态。
    /// Saves the current settings as the user's defaults: the snapshot goes to its own file and the in-memory effective defaults are
    /// swapped to it. The settings themselves are untouched, so saving defaults never interrupts what the user is doing.
    /// </summary>
    /// <returns>写入失败的原因；成功时为 null。/ The failure reason, or null on success.</returns>
    public string? SaveCurrentAsUserDefaults()
    {
        var snapshot = SettingsManager.Current.Clone();
        try
        {
            Directory.CreateDirectory(_directoryPath);
            var temp = _userDefaultsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new SettingsEnvelope(CurrentSchemaVersion, snapshot), _jsonOptions));
            File.Move(temp, _userDefaultsPath, overwrite: true);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Settings] Could not write user defaults: {exception.Message}");
            return exception.Message;
        }

        SettingsManager.SetUserDefaults(snapshot);
        return null;
    }

    /// <summary>
    /// 读取「我的默认设置」快照；文件不存在或不可读时返回 null。
    /// 快照经过与设置文件相同的迁移与归一化，因此旧版本写入的快照在新版本里仍然可用，而不是被静默丢弃。
    /// Reads the user-defaults snapshot, or null when the file is missing or unreadable. The snapshot goes through the same
    /// migration and normalization as the settings file, so a snapshot written by an older version stays usable instead of being
    /// silently discarded.
    /// </summary>
    public AppSettings? LoadUserDefaults()
    {
        if (!File.Exists(_userDefaultsPath))
            return null;

        try
        {
            return ReadEnvelope(_userDefaultsPath).Normalize();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Settings] Invalid user defaults: {exception.Message}");
            return null;
        }
    }

    /// <summary>删除「我的默认设置」快照，让所有重置入口回到程序内置默认。/ Deletes the user-defaults snapshot so every reset entry falls back to the built-in defaults.</summary>
    /// <returns>删除失败的原因；成功或文件本就不存在时为 null。/ The failure reason, or null on success or when no snapshot existed.</returns>
    public string? ClearUserDefaults()
    {
        try
        {
            if (File.Exists(_userDefaultsPath))
                File.Delete(_userDefaultsPath);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Settings] Could not delete user defaults: {exception.Message}");
            return exception.Message;
        }

        SettingsManager.SetUserDefaults(null);
        return null;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Flush();
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || !_initialized) return;
            _timer?.Dispose();
            _timer = new Timer(static state => ((SettingsPersistenceService)state!).SaveFromTimer(), this, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void SaveFromTimer()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer?.Dispose();
            _timer = null;
        }
        SaveCore(SettingsManager.Current);
    }

    private void LoadCore()
    {
        LegacyTaskbarMonitorIndex = null;
        _loadedSchemaVersion = null;
        AppSettings? loaded = null;
        if (File.Exists(_settingsPath))
        {
            try { loaded = ReadEnvelope(_settingsPath); }
            catch (UnsupportedSettingsSchemaException)
            {
                Quarantine(_settingsPath, "unsupported");
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[Settings] Invalid settings file: {exception.Message}");
                Quarantine(_settingsPath, "invalid");
            }
        }

        if (loaded is null && File.Exists(_backupPath))
        {
            try { loaded = ReadEnvelope(_backupPath); }
            catch (Exception exception) { Debug.WriteLine($"[Settings] Invalid settings backup: {exception.Message}"); }
        }

        SettingsManager.Replace((loaded ?? new AppSettings()).Normalize());
        if (!File.Exists(_settingsPath) || loaded is null || _loadedSchemaVersion < CurrentSchemaVersion)
            SaveCore(SettingsManager.Current);
    }

    private AppSettings ReadEnvelope(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new JsonException("Settings envelope is empty.");
        if (node["settings"] is JsonObject settings)
        {
            foreach (var property in new[] { "layoutLengthScalePercent", "layoutThicknessScalePercent", "taskbarBarCrossAxisOffsetDip", "dynamicIslandLeft", "dynamicIslandTop" })
            {
                if (settings[property] is null) settings.Remove(property);
            }
        }
        var envelope = JsonSerializer.Deserialize<SettingsEnvelope>(node.ToJsonString(), _jsonOptions)
            ?? throw new JsonException("Settings envelope is empty.");
        if (envelope.SchemaVersion is < 1 or > CurrentSchemaVersion)
            throw new UnsupportedSettingsSchemaException(envelope.SchemaVersion);
        _loadedSchemaVersion = envelope.SchemaVersion;
        var result = envelope.Settings ?? new AppSettings();
        if (envelope.SchemaVersion == 1)
        {
            // The redesigned interaction model intentionally starts from its new defaults.
            // Stable appearance, lyric, taskbar-placement, and window-mode fields are retained.
            result.Interaction = GlobalInteractionSettings.Default;
            result.TaskbarExperience = TaskbarExperienceSettings.Default;
            result.TaskbarSurface = ModeSurfaceSettings.Default;
            result.DynamicIslandSurface = ModeSurfaceSettings.Default;
            result.LyricsTextAlignment = LyricsTextAlignment.Center;
        }
        else if (envelope.SchemaVersion == 2)
        {
            // Schema 2 had no full-panel group visibility. Preserve its all-visible experience.
            result.TaskbarExperience = result.TaskbarExperience with
            {
                FullPanel = TaskbarFullPanelSettings.Default
            };
        }
        if (envelope.SchemaVersion <= 3)
        {
            // Schema 4 introduces an opt-in notification and stable display identifiers.
            // The legacy taskbar index is retained in memory until startup can map it against
            // the live monitor topology without moving Win32 discovery into the I/O service.
            result.TrackChangeNotification = TrackChangeNotificationSettings.Default;
            LegacyTaskbarMonitorIndex = node["settings"]?["taskbarBarSelectedMonitor"] is JsonValue legacyIndexNode &&
                                        legacyIndexNode.TryGetValue<int>(out var legacyIndex)
                ? Math.Max(0, legacyIndex)
                : 0;
        }
        if (envelope.SchemaVersion <= 4)
        {
            // Schema 5 adds taskbar length behavior. Existing users retain the previous
            // content-following layout instead of receiving a new fixed width implicitly.
            result.TaskbarExperience = result.TaskbarExperience with
            {
                LengthMode = TaskbarLengthMode.FollowContent,
                FixedLengthDip = TaskbarExperienceSettings.Default.FixedLengthDip
            };
        }
        if (envelope.SchemaVersion <= 5)
        {
            // Schema 6 adds opt-in source filtering, an explicit quick-launch list, and
            // always-on taskbar spectrum/performance component parameters.
            result.SmtcSourceFilter = SmtcSourceFilterSettings.Default;
            result.QuickLaunch = QuickLaunchSettings.Default;
            result.SpectrumComponent = SpectrumComponentSettings.Default;
            result.PerformanceComponent = PerformanceComponentSettings.Default;
        }
        if (envelope.SchemaVersion <= 6)
        {
            // Schema 7 replaces interaction presets with explicit bindings and makes the
            // redesigned display-mode picker taskbar-only at runtime.
            var legacyExperience = result.TaskbarExperience.Normalize();
            var legacyLayoutNode = node["settings"]?["taskbarExperience"]?["contentLayout"];
            var legacyLayoutText = legacyLayoutNode?.ToJsonString().Trim('"');
            var legacyCenteredLayout = legacyExperience.ContentLayout == TaskbarContentLayout.CenteredStack ||
                                       string.Equals(legacyLayoutText, nameof(TaskbarContentLayout.CenteredStack), StringComparison.OrdinalIgnoreCase) ||
                                       legacyLayoutText == ((int)TaskbarContentLayout.CenteredStack).ToString() ||
                                       legacyLayoutNode is JsonValue layoutValue &&
                                       ((layoutValue.TryGetValue<string>(out var layoutName) &&
                                         string.Equals(layoutName, nameof(TaskbarContentLayout.CenteredStack), StringComparison.OrdinalIgnoreCase)) ||
                                        (layoutValue.TryGetValue<int>(out var layoutNumber) &&
                                         layoutNumber == (int)TaskbarContentLayout.CenteredStack));
            result.WindowMode = WindowMode.Taskbar;
            result.Interaction = GlobalInteractionSettings.Default;
            result.TaskbarExperience = legacyExperience with
            {
                ContentLayout = legacyCenteredLayout
                    ? TaskbarContentLayout.AdaptiveStack
                    : legacyExperience.ContentLayout,
                MediaTextAlignment = legacyCenteredLayout
                    ? TaskbarMediaTextAlignment.Center
                    : TaskbarMediaTextAlignment.Left,
                SpectrumVisible = true,
                PerformanceVisible = true,
                HoverControls = TaskbarHoverControlsSettings.Default
            };
        }
        if (envelope.SchemaVersion <= 7)
        {
            // Schema 8 adds the rest-layer media font-size scale. Older files keep the previous
            // text sizes instead of inheriting a new default scale.
            // schema 8 新增静置层媒体文字字号缩放；旧设置文件保持原有文字大小，而不是继承新的默认缩放。
            result.TaskbarExperience = result.TaskbarExperience with
            {
                MediaFontSizePercent = TaskbarExperienceSettings.Default.MediaFontSizePercent
            };
        }
        if (envelope.SchemaVersion <= 8)
        {
            // Schema 9 新增更新下载器设置。旧设置文件里没有这一段，反序列化后字段保留声明处的默认值，
            // 但这里仍然显式赋值：迁移意图必须写在代码里，而不是依赖"缺字段时恰好等于默认值"这种巧合。
            // 默认开启自动检查与自动下载安装，与全新安装后的行为一致。
            // Schema 9 adds the update-downloader settings. Older files have no such section, and deserialization
            // keeps the declared default of the backing field; the assignment is explicit anyway, because a
            // migration intent belongs in code rather than in the coincidence that a missing field equals a
            // default. Automatic checking and automatic download/install are on, matching a fresh installation.
            result.Update = UpdateSettings.Default;
        }
        if (envelope.SchemaVersion <= 9)
        {
            // Schema 10 让频谱柱数与尺寸相关（9–24 根）并新增频谱样式。旧文件的柱数可能落在新区间之外，
            // 由 Normalize 夹到 9；样式在该文件里没有对应字段，取值即柱状图，与迁移前的观感一致。
            // 性能组件的采样间隔同时收敛到 0.5–5 秒，旧取值由 Normalize 吸附到 0.5 秒网格并夹取。
            // 「点击性能组件时打开任务管理器」改为默认开启：该开关此前虽然存在，但点击被任务栏拖动逻辑吞掉，
            // 因此没有任何用户能在它关闭的状态下做出有效选择，旧文件里的 false 不代表用户意图。
            // Schema 10 ties the spectrum bar count to its size (9–24) and adds spectrum styles. Bars from an older file may
            // fall outside the new range and are clamped to nine by Normalize, while the style has no field in those files and
            // therefore reads as bars, matching the pre-migration appearance. The performance sampling interval narrows to
            // 0.5–5 seconds at the same time; Normalize snaps older values onto the 0.5-second grid and clamps them. Opening
            // Task Manager on click becomes the default: the switch existed before but the click was swallowed by the taskbar
            // drag logic, so no user could have made a meaningful choice while it was off and a stored false is not intent.
            result.SpectrumComponent = result.SpectrumComponent with { Style = SpectrumStyle.Bars };
            result.PerformanceComponent = result.PerformanceComponent with { OpenTaskManagerOnClick = true };
        }
        if (envelope.SchemaVersion <= 10)
        {
            // Schema 11 新增「完整层入口」与「静置层进度显示」两个开关，并让字体粗细改用 100–900 的真实字重、频谱灵敏度按 10 步进。
            // 旧文件没有这两个开关字段，迁移时显式写入开启，保持"细杠与悬停按钮都在、静置层底部有进度条"的既有行为；
            // 粗细与灵敏度由各自的 Normalize 吸附与夹取，这里不再重复。
            // Schema 11 adds the full-layer entry and rest-layer progress switches and moves the font weight onto the nine real
            // weights from 100 to 900 while the spectrum sensitivity steps by ten. Older files have neither switch, so the
            // migration writes both as enabled and keeps the previous behaviour where the thin bar, the hover button, and the
            // rest-layer progress bar were all present; the weight and the sensitivity are snapped and clamped by their own
            // Normalize, so they are not repeated here.
            result.TaskbarExperience = result.TaskbarExperience with
            {
                FullPanelEntryVisible = true,
                RestProgressVisible = true
            };
        }
        if (envelope.SchemaVersion <= 11)
        {
            // Schema 12 新增「随 Windows 登录自动启动」设置段。旧文件里没有这一段，反序列化会保留声明处的默认值，
            // 这里仍然显式赋值：默认开启是产品决定，必须写在迁移里而不是依赖"缺字段恰好等于默认值"。
            // Schema 12 adds the run-at-startup setting. Older files have no such field and deserialization would keep the declared
            // default; the assignment is explicit anyway, because "on by default" is a product decision that belongs in the
            // migration instead of resting on the coincidence that a missing field equals a default.
            result.LaunchAtStartup = true;
        }
        return result.Normalize();
    }

    private void SaveCore(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_directoryPath);
            var temp = Path.Combine(_directoryPath, $"settings.{Guid.NewGuid():N}.tmp");
            var envelope = new SettingsEnvelope(CurrentSchemaVersion, settings.Normalize());
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, envelope, _jsonOptions);
                stream.Flush(true);
            }

            if (File.Exists(_settingsPath))
                File.Copy(_settingsPath, _backupPath, true);
            File.Move(temp, _settingsPath, true);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Settings] Save failed; keeping in-memory settings: {exception}");
        }
    }

    private void Quarantine(string path, string reason)
    {
        try
        {
            var target = $"{path}.{reason}-{DateTime.Now:yyyyMMddHHmmssfff}";
            File.Move(path, target, true);
        }
        catch (Exception exception) { Debug.WriteLine($"[Settings] Could not quarantine {path}: {exception.Message}"); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            SettingsManager.SettingsChanged -= OnSettingsChanged;
            _timer?.Dispose();
            _timer = null;
        }
        Flush();
    }

    private sealed record SettingsEnvelope(int SchemaVersion, AppSettings? Settings);
    private sealed class UnsupportedSettingsSchemaException(int version) : Exception($"Unsupported settings schema {version}.");

    private sealed class LenientEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type type) => type.IsEnum;
        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(type))!;
    }

    private sealed class LenientEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && Enum.TryParse<TEnum>(reader.GetString(), true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(TEnum), number)) return (TEnum)Enum.ToObject(typeof(TEnum), number);
            return DefaultValue();
        }
        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());

        private static TEnum DefaultValue()
        {
            object value = typeof(TEnum) == typeof(TrayWheelBehavior) ? TrayWheelBehavior.SwitchOutputDevice :
                typeof(TEnum) == typeof(LyricsSecondaryLineMode) ? LyricsSecondaryLineMode.NextLine :
                typeof(TEnum) == typeof(TaskbarBarPosition) ? TaskbarBarPosition.Start :
                typeof(TEnum) == typeof(LayoutOrientationMode) ? LayoutOrientationMode.Auto :
                typeof(TEnum) == typeof(DynamicIslandBackgroundMode) ? DynamicIslandBackgroundMode.SystemTheme :
                typeof(TEnum) == typeof(TrackChangeNotificationPosition) ? TrackChangeNotificationPosition.BottomLeft :
                typeof(TEnum) == typeof(NotificationTargetMode) ? NotificationTargetMode.Fixed :
                typeof(TEnum) == typeof(WindowMode) ? WindowMode.Taskbar :
                typeof(TEnum) == typeof(DynamicIslandEdge) ? DynamicIslandEdge.Top :
                typeof(TEnum) == typeof(LatinFontPreset) ? LatinFontPreset.SegoeUi :
                typeof(TEnum) == typeof(CjkFontPreset) ? CjkFontPreset.SystemDefault :
                typeof(TEnum) == typeof(PlayerForegroundMode) ? PlayerForegroundMode.Automatic :
                typeof(TEnum) == typeof(ApplicationThemeMode) ? ApplicationThemeMode.Automatic :
                typeof(TEnum) == typeof(ApplicationBackdropMode) ? ApplicationBackdropMode.Mica :
                typeof(TEnum) == typeof(MediaInteractionMode) ? MediaInteractionMode.Hybrid :
                typeof(TEnum) == typeof(WheelAction) ? WheelAction.PreviousNext :
                typeof(TEnum) == typeof(MouseChordButton) ? MouseChordButton.Left :
                typeof(TEnum) == typeof(PlayerClickAction) ? PlayerClickAction.TogglePlayPause :
                typeof(TEnum) == typeof(InteractionModifier) ? InteractionModifier.Shift :
                typeof(TEnum) == typeof(TrayClickAction) ? TrayClickAction.OpenAudioControl :
                typeof(TEnum) == typeof(TaskbarInformationDensity) ? TaskbarInformationDensity.Balanced :
                typeof(TEnum) == typeof(TaskbarContentLayout) ? TaskbarContentLayout.AdaptiveStack :
                typeof(TEnum) == typeof(TaskbarMediaTextAlignment) ? TaskbarMediaTextAlignment.Left :
                typeof(TEnum) == typeof(TaskbarLengthMode) ? TaskbarLengthMode.FollowContent :
                typeof(TEnum) == typeof(PlayerSurfaceStyle) ? PlayerSurfaceStyle.Automatic :
                typeof(TEnum) == typeof(LyricsTextAlignment) ? LyricsTextAlignment.Center : default(TEnum);
            return (TEnum)value;
        }
    }
}
