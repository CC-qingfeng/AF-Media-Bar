using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace AFMediaBar.Components
{
    /// <summary>
    /// 设置页说明条：用主题画刷呈现一段解释性文字，替代以前写死色值的 Border。
    /// Settings callout: presents one explanatory note with theme brushes instead of a hardcoded Border colour.
    ///
    /// 只承载文案和语气，不持有状态、不读取设置，也不参与任务栏或灵动岛的呈现路径。
    /// It carries text and tone only: no state, no settings access, and it never joins the taskbar or
    /// dynamic-island presentation path.
    /// </summary>
    public partial class SettingsCallout : UserControl
    {
        /// <summary>说明条左侧图标的标识。/ Identifies the glyph shown before the note.</summary>
        public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
            nameof(Symbol),
            typeof(SymbolRegular),
            typeof(SettingsCallout),
            new PropertyMetadata(SymbolRegular.Info24));

        /// <summary>说明条正文；调用方负责给出完整句子。/ Callout body; the caller supplies a complete sentence.</summary>
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(SettingsCallout),
            new PropertyMetadata(string.Empty));

        /// <summary>
        /// 是否使用主色语气。主色用于“必须知道的约束”，中性用于“有用但可忽略的提示”。
        /// Whether the accent tone is used. Accent marks a constraint the user must know; neutral marks a hint.
        /// </summary>
        public static readonly DependencyProperty IsAccentProperty = DependencyProperty.Register(
            nameof(IsAccent),
            typeof(bool),
            typeof(SettingsCallout),
            new PropertyMetadata(false));

        /// <summary>
        /// 创建说明条。/ Creates the callout.
        /// </summary>
        public SettingsCallout() => InitializeComponent();

        /// <summary>左侧图标；默认是信息图标。/ Leading glyph; defaults to the information icon.</summary>
        public SymbolRegular Symbol
        {
            get => (SymbolRegular)GetValue(SymbolProperty);
            set => SetValue(SymbolProperty, value);
        }

        /// <summary>说明正文。/ The note body.</summary>
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>是否使用主色语气；默认关闭。/ Whether the accent tone is used; off by default.</summary>
        public bool IsAccent
        {
            get => (bool)GetValue(IsAccentProperty);
            set => SetValue(IsAccentProperty, value);
        }
    }
}
