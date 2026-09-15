using AFMediaBar.ViewModels.Windows;
using AFMediaBar.Classes.Services;
using AFMediaBar.Components;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using AFMediaBar.Views.Pages;

namespace AFMediaBar.Views.Windows
{
    /// <summary>
    /// 显示应用设置页面并承载 WPF-UI 导航。
    /// Displays application settings pages and hosts WPF-UI navigation.
    /// </summary>
    public partial class SettingsWindow : INavigationWindow
    {
        /// <summary>
        /// 搜索命中到页面类型的映射。搜索索引刻意不引用任何 View 类型，
        /// 因此这层映射留在视图侧，索引只使用与视图解耦的页面键。
        /// Maps search hits to page types. The search index deliberately references no view type, so this mapping
        /// lives on the view side and the index uses only view-independent page keys.
        /// </summary>
        private static readonly IReadOnlyDictionary<SettingsPageKey, System.Type> SearchPageTypes =
            new Dictionary<SettingsPageKey, System.Type>
            {
                [SettingsPageKey.DisplayModes] = typeof(DisplayModesPage),
                [SettingsPageKey.MediaAndNotifications] = typeof(ExtraFeaturesPage),
                [SettingsPageKey.Interaction] = typeof(InteractionPage),
                [SettingsPageKey.Lyrics] = typeof(LyricsPage),
                [SettingsPageKey.Appearance] = typeof(AppearancePage),
                [SettingsPageKey.AppAndAbout] = typeof(AboutPage),
            };

        /// <summary>候选项文本到命中的反查表；候选文案在列表里唯一，因此可以直接做键。/ Maps suggestion text back to its hit; the label is unique within a result list, so it works as a key.</summary>
        private readonly Dictionary<string, SettingsSearchHit> _searchHits = new(StringComparer.Ordinal);

        public SettingsWindowViewModel ViewModel { get; }

        /// <summary>
        /// 创建设置窗口并连接导航和外观服务。
        /// Creates the settings window and connects navigation and appearance services.
        /// </summary>
        public SettingsWindow(
            SettingsWindowViewModel viewModel,
            INavigationViewPageProvider navigationViewPageProvider,
            INavigationService navigationService,
            WindowAppearanceService appearanceService
        )
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
            SettingsResetDialog.SetHost(RootContentDialog);
            appearanceService.Attach(this);
            SetPageService(navigationViewPageProvider);

            RootNavigation.TransitionDuration = (int)Math.Round(
                MotionPolicy.ResolveCurrent().StandardDuration.TotalMilliseconds);

            navigationService.SetNavigationControl(RootNavigation);
        }

        #region Search

        /// <summary>
        /// 按当前输入重建候选项。WPF-UI 的 AutoSuggestBox 会在 OriginalItemsSource 上再按文本过滤一次，
        /// 因此每条候选文案都被构造成包含输入本身——否则关键词命中会在显示前被它自己的过滤器丢掉。
        /// Rebuilds the suggestions for the current input. WPF-UI's AutoSuggestBox filters OriginalItemsSource by
        /// the text again, so every label is built to contain the query; otherwise a keyword hit would be dropped
        /// by the control's own filter before it could be shown.
        /// </summary>
        private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            var query = AutoSuggestBox.Text;
            _searchHits.Clear();

            var hits = SettingsSearchPolicy.Search(query, SettingsSearchIndex.Entries);
            if (hits.Count == 0)
            {
                // 空数组而不是 null：该属性在 WPF-UI 里标注为非空，而且空集合本来就更准确地表达“没有候选”。
                // An empty array rather than null: the property is annotated non-null in WPF-UI, and an empty
                // collection expresses "no suggestions" more accurately anyway.
                AutoSuggestBox.OriginalItemsSource = System.Array.Empty<string>();
                return;
            }

            var labels = new List<string>(hits.Count);
            foreach (var hit in hits)
            {
                var label = BuildSuggestionLabel(hit, query);
                labels.Add(label);
                _searchHits[label] = hit;
            }

            AutoSuggestBox.OriginalItemsSource = labels;
        }

        /// <summary>构造候选项文案：页面 › 分组；输入未出现在其中时补一个括号，说明它是凭什么命中的。/ Builds "page › group", appending the query in brackets when it does not appear, so the list says why an entry matched.</summary>
        private static string BuildSuggestionLabel(SettingsSearchHit hit, string? query)
        {
            var label = $"{hit.PageTitle} › {hit.Title}";
            var term = query?.Trim();
            if (string.IsNullOrEmpty(term) ||
                label.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }

            return $"{label}（{term}）";
        }

        /// <summary>
        /// 选中候选项后先导航到目标页面，再让该页的分组标签条滚到命中的分组。
        /// 跳转必须等到新页面完成布局，否则分组位置还是上一次的。
        /// Selecting a suggestion navigates to the page and then asks that page's group strip to scroll to the
        /// matching group. The jump waits for the new page to finish layout, because group positions would
        /// otherwise still belong to the previous page.
        /// </summary>
        private void OnSearchSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is not string label ||
                !_searchHits.TryGetValue(label, out var hit) ||
                !SearchPageTypes.TryGetValue(hit.Page, out var pageType))
            {
                return;
            }

            if (!Navigate(pageType))
            {
                return;
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => FindDescendant<SettingsGroupStrip>(RootNavigation)?.JumpToGroup(hit.GroupIndex)));
        }

        /// <summary>按深度优先在可视树中查找第一个指定类型后代。/ Finds the first descendant of a type, depth first.</summary>
        private static T? FindDescendant<T>(DependencyObject root)
            where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T match)
                {
                    return match;
                }

                if (FindDescendant<T>(child) is { } nested)
                {
                    return nested;
                }
            }

            return null;
        }

        #endregion Search

        #region INavigationWindow methods

        /// <summary>返回设置窗口导航控件。/ Returns the settings navigation control.</summary>
        public INavigationView GetNavigation() => RootNavigation;

        /// <summary>导航到指定页面类型。/ Navigates to the specified page type.</summary>
        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        /// <summary>设置导航页面提供器。/ Sets the navigation page provider.</summary>
        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) =>
            RootNavigation.SetPageProviderService(navigationViewPageProvider);

        /// <summary>显示设置窗口。/ Shows the settings window.</summary>
        public void ShowWindow() => Show();

        /// <summary>关闭设置窗口。/ Closes the settings window.</summary>
        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        INavigationView INavigationWindow.GetNavigation()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 为导航页提供应用组合根；该兼容入口只在设置窗口初始化期间调用。
        /// Supplies the application composition root to navigation pages; this compatibility entry point is used only during settings-window initialization.
        /// </summary>
        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException();
        }
    }
}
