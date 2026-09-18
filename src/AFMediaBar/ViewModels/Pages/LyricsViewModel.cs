using System.Collections.ObjectModel;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>歌词呈现、取词来源与对齐设置。 / Lyric presentation, retrieval sources, and alignment settings.</summary>
public partial class LyricsViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private bool _isRefreshing;

    /// <summary>
    /// 本视图模型正在写来源设置：写回会同步触发设置变更事件，若此时重建列表，用户刚点的那一行会被换掉（焦点丢失、行闪一下）。
    /// This view model is writing the source settings: the write publishes a settings change synchronously, and rebuilding the list
    /// then would replace the very row the user just clicked (focus lost, the row flickers).
    /// </summary>
    private bool _isSavingSources;

    /// <summary>
    /// 创建歌词页视图模型。
    /// Creates the lyrics page view model.
    /// </summary>
    /// <param name="localization">语言服务，用于在语言变化时重建来源显示名 / Localization service, used to rebuild source display names after a language change.</param>
    public LyricsViewModel(LocalizationService localization)
    {
        _localization = localization;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        RefreshSourceEntries();
    }

    public bool LyricsEnabled { get => SettingsManager.Current.LyricsEnabled; set { SettingsManager.SetLyricsEnabled(value); RaiseAll(); } }
    public bool TwoLineLyricsEnabled { get => SettingsManager.Current.TwoLineLyricsEnabled; set { SettingsManager.SetTwoLineLyricsEnabled(value); RaiseAll(); } }
    public LyricsSecondaryLineMode SecondaryLineMode { get => SettingsManager.Current.LyricsSecondaryLineMode; set { SettingsManager.SetLyricsSecondaryLineMode(value); OnPropertyChanged(); } }
    public LyricsTextAlignment TextAlignment { get => SettingsManager.Current.LyricsTextAlignment; set { SettingsManager.SetLyricsTextAlignment(value); OnPropertyChanged(); } }

    /// <summary>是否启用逐字擦亮。/ Whether syllable highlighting is enabled.</summary>
    public bool SyllableHighlightEnabled
    {
        get => SettingsManager.Current.LyricsSyllableHighlightEnabled;
        set { SettingsManager.SetLyricsSyllableHighlightEnabled(value); RaiseAll(); }
    }

    /// <summary>启用逐字擦亮时底色层（未唱部分）的不透明度百分比。/ Opacity percentage of the base (unsung) layer while highlighting is on.</summary>
    public int UnsungOpacityPercent
    {
        get => SettingsManager.Current.LyricsUnsungOpacityPercent;
        set
        {
            SettingsManager.SetLyricsUnsungOpacityPercent(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(UnsungOpacityText));
        }
    }

    /// <summary>不透明度的读数文本。/ The opacity readout text.</summary>
    public string UnsungOpacityText => $"{UnsungOpacityPercent}%";

    /// <summary>是否丢弃作者、作曲、制作等信息行。/ Whether credit lines are dropped.</summary>
    public bool InfoLineFilterEnabled
    {
        get => SettingsManager.Current.LyricsInfoLineFilterEnabled;
        set { SettingsManager.SetLyricsInfoLineFilterEnabled(value); OnPropertyChanged(); }
    }

    /// <summary>搜索型来源的匹配严格度。/ Match strictness for search-based sources.</summary>
    public LyricsMatchStrictness MatchStrictness
    {
        get => SettingsManager.Current.LyricsMatchStrictness;
        set { SettingsManager.SetLyricsMatchStrictness(value); OnPropertyChanged(); }
    }

    /// <summary>取词来源列表，顺序即优先级。/ The retrieval source list, whose order is the priority.</summary>
    public ObservableCollection<LyricsSourceSettingItem> SourceEntries { get; } = [];

    /// <summary>是否一个来源都没启用：此时不会请求任何歌词服务，页面用提示条说明后果。
    /// Whether no source is enabled: no lyric service is contacted then, and a callout on the page states that consequence.</summary>
    public bool IsEverySourceDisabled => SourceEntries.Count > 0 && SourceEntries.All(entry => !entry.IsEnabled);

    public bool CanConfigureTwoLine => LyricsEnabled;
    public bool CanConfigureSecondary => LyricsEnabled && TwoLineLyricsEnabled;

    /// <summary>未唱部分不透明度只在启用逐字擦亮时可用：没有擦亮时它没有作用对象。/ The unsung opacity only applies while highlighting is on.</summary>
    public bool CanConfigureUnsungOpacity => SyllableHighlightEnabled;

    public void ResetLyrics() => SettingsManager.ResetLyrics();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.ResetScope is SettingsResetScope.Lyrics or SettingsResetScope.All)
        {
            RefreshSourceEntries();
            RaiseAll();
            return;
        }

        if (e.PropertyName == nameof(AppSettings.LyricsSource) && !_isSavingSources)
        {
            RefreshSourceEntries();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshSourceEntries();

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(LyricsEnabled)); OnPropertyChanged(nameof(TwoLineLyricsEnabled));
        OnPropertyChanged(nameof(SecondaryLineMode)); OnPropertyChanged(nameof(TextAlignment));
        OnPropertyChanged(nameof(SyllableHighlightEnabled)); OnPropertyChanged(nameof(UnsungOpacityPercent));
        OnPropertyChanged(nameof(UnsungOpacityText)); OnPropertyChanged(nameof(InfoLineFilterEnabled));
        OnPropertyChanged(nameof(MatchStrictness));
        OnPropertyChanged(nameof(CanConfigureTwoLine)); OnPropertyChanged(nameof(CanConfigureSecondary));
        OnPropertyChanged(nameof(CanConfigureUnsungOpacity)); OnPropertyChanged(nameof(IsEverySourceDisabled));
    }

    /// <summary>
    /// 按设置重建来源列表（保留用户顺序），并挂上变更回调。
    /// Rebuilds the source list from the settings while keeping the user's order, and wires the change callback.
    ///
    /// 列表始终包含全部已知来源：只列出已启用的话，用户关掉一个来源后它就从这个界面上消失、再也开不回来。顺序是"已启用的
    /// 按用户顺序在前，其余按默认顺序在后"。
    /// The list always contains every known source: listing only the enabled ones would make a source the user turned off disappear
    /// from this page and become impossible to turn back on. The order is "enabled ones in the user's order first, the rest in the
    /// default order after them".
    /// </summary>
    private void RefreshSourceEntries()
    {
        _isRefreshing = true;
        try
        {
            var enabledIds = SettingsManager.Current.LyricsSource.Normalize().EnabledSourceIds;
            var enabled = enabledIds is null
                ? null
                : new HashSet<string>(enabledIds, StringComparer.Ordinal);

            var order = new List<string>(LyricsSourceCatalog.DefaultOrder.Count);
            if (enabledIds is { Count: > 0 })
            {
                foreach (var id in enabledIds)
                {
                    if (LyricsSourceCatalog.IsKnown(id) && !order.Contains(id, StringComparer.Ordinal))
                    {
                        order.Add(id);
                    }
                }
            }

            foreach (var id in LyricsSourceCatalog.DefaultOrder)
            {
                if (!order.Contains(id, StringComparer.Ordinal))
                {
                    order.Add(id);
                }
            }

            foreach (var entry in SourceEntries)
            {
                entry.EnabledChanged -= OnSourceEnabledChanged;
            }

            SourceEntries.Clear();
            foreach (var sourceId in order)
            {
                // 未配置（null）时全部来源都是开启的；配置过之后，列表里没有的 id 视为关闭。
                // While nothing is configured (null) every source is on; once something is configured, an id missing from the list
                // counts as off.
                var isEnabled = enabled?.Contains(sourceId) ?? true;
                var entry = new LyricsSourceSettingItem(sourceId, isEnabled);
                entry.EnabledChanged += OnSourceEnabledChanged;
                SourceEntries.Add(entry);
            }

            OnPropertyChanged(nameof(SourceEntries));
            OnPropertyChanged(nameof(IsEverySourceDisabled));
        }
        finally { _isRefreshing = false; }
    }

    private void OnSourceEnabledChanged(LyricsSourceSettingItem item)
    {
        if (_isRefreshing)
        {
            return;
        }

        SaveSourceEntries();
    }

    /// <summary>
    /// 把列表状态写回设置：恰好是"全部来源按默认顺序"时写回"未配置"，让以后新增的来源自动生效。
    /// Writes the list state back into the settings: an exact "every source in the default order" is stored as "never configured", so
    /// a source added later takes effect on its own.
    /// </summary>
    private void SaveSourceEntries()
    {
        var ordered = SourceEntries.Where(entry => entry.IsEnabled).Select(entry => entry.SourceId).ToArray();
        var isDefaultOrder = ordered.Length == LyricsSourceCatalog.DefaultOrder.Count &&
                             ordered.SequenceEqual(LyricsSourceCatalog.DefaultOrder, StringComparer.Ordinal);
        var settings = new LyricsSourceSettings(isDefaultOrder ? null : ordered);

        _isSavingSources = true;
        try
        {
            SettingsManager.SetLyricsSourceSettings(settings);
        }
        finally
        {
            _isSavingSources = false;
        }

        OnPropertyChanged(nameof(IsEverySourceDisabled));
    }

    [RelayCommand]
    private void MoveSourceUp(LyricsSourceSettingItem? item) => MoveSource(item, -1);

    [RelayCommand]
    private void MoveSourceDown(LyricsSourceSettingItem? item) => MoveSource(item, 1);

    private void MoveSource(LyricsSourceSettingItem? item, int offset)
    {
        if (item is null)
        {
            return;
        }

        var index = SourceEntries.IndexOf(item);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= SourceEntries.Count)
        {
            return;
        }

        SourceEntries.Move(index, target);
        SaveSourceEntries();
    }

    [RelayCommand]
    private void ResetSourceOrder()
    {
        // 逐项切换会各自触发一次写回，因此这里先挂起回调，排序完成后再整体写一次。
        // Toggling each entry would publish one write per entry, so the callback is suspended here and the whole list is written once
        // after the reordering.
        _isRefreshing = true;
        try
        {
            foreach (var entry in SourceEntries)
            {
                entry.IsEnabled = true;
            }

            var ordered = SourceEntries.OrderBy(
                entry => LyricsSourceCatalog.DefaultOrder.ToList().IndexOf(entry.SourceId)).ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                var current = SourceEntries.IndexOf(ordered[i]);
                if (current != i)
                {
                    SourceEntries.Move(current, i);
                }
            }
        }
        finally
        {
            _isRefreshing = false;
        }

        SaveSourceEntries();
    }
}
