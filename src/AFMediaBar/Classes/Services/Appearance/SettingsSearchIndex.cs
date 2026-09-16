using System.Collections.Generic;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 设置窗口搜索框的静态索引。它是“搜索能搜到什么”的唯一事实来源：
/// 每条记录指向一个页面分组，分组序号必须与该页滚动内容里 <c>SettingsGroup</c> 的声明顺序一致。
///
/// 为什么用静态索引而不是在运行时遍历可视树：可视树只包含当前页，遍历它无法搜索其他页面；
/// 而把关键词写在页面里又会让“能被搜到”散落在六个页面。这里集中一处，并用单元测试盯住序号边界。
/// The static index behind the settings-window search box, and the single source of truth for what search can
/// find: every record names a page and a group index that must match the declaration order of the
/// <c>SettingsGroup</c> containers in that page's scroll content.
///
/// Why a static index rather than walking the visual tree: the visual tree only holds the current page, so
/// walking it cannot search the other five, and scattering keywords across the pages would spread
/// "findability" over six places. Keeping them together also lets a unit test guard the group bounds.
/// </summary>
public static class SettingsSearchIndex
{
    /// <summary>全部可搜索的分组。/ Every searchable group.</summary>
    public static IReadOnlyList<SettingsSearchEntry> Entries { get; } = Build();

    /// <summary>导航栏里的页面名称，与 <c>SettingsWindow</c> 的菜单项文案一致。/ Navigation labels, matching the menu items in <c>SettingsWindow</c>.</summary>
    public static string GetPageTitle(SettingsPageKey page) => page switch
    {
        SettingsPageKey.DisplayModes => "显示模式",
        SettingsPageKey.MediaAndNotifications => "媒体与通知",
        SettingsPageKey.Interaction => "交互",
        SettingsPageKey.Lyrics => "歌词",
        SettingsPageKey.Appearance => "外观",
        SettingsPageKey.AppAndAbout => "应用与关于",
        _ => string.Empty,
    };

    private static SettingsSearchEntry Create(
        SettingsPageKey page,
        int groupIndex,
        string title,
        string description,
        params string[] keywords) =>
        new(page, groupIndex, GetPageTitle(page), title, description, keywords);

    private static SettingsSearchEntry Create(
        int groupIndex,
        string title,
        string description,
        params string[] keywords) =>
        Create(SettingsPageKey.DisplayModes, groupIndex, title, description, keywords) with
        {
            Mode = SettingsSearchMode.Taskbar,
        };

    /// <summary>
    /// 模式选择分组：四种模式下它都可见，因此在索引里每种模式各占序号 0。
    /// 分组序号是「该模式下标签条的第几项」，而标签条永远从模式选择开始。
    /// The mode-picker group is visible in all four modes, so the index carries it at index 0 for each of them.
    /// A group index counts tabs within one mode, and the tabs always start with the picker.
    /// </summary>
    private static SettingsSearchEntry CreateModePicker(SettingsSearchMode mode) =>
        Create(SettingsPageKey.DisplayModes, 0, "选择显示模式", "选择任务栏、灵动岛、桌面卡片或悬浮球", ["模式", "mode", "显示模式", "承载模式", "灵动岛", "island", "桌面卡片", "悬浮球", "切换"]) with
        {
            Mode = mode,
        };

    private static SettingsSearchEntry[] Build() =>
    [
        // ---- 显示模式 · 模式选择（四种模式下都可见，因此每种模式各有一条，序号固定为 0）----
        // ---- Display modes, the mode picker, visible in all four modes, so every mode carries it at index 0 ----
        CreateModePicker(SettingsSearchMode.Taskbar),
        CreateModePicker(SettingsSearchMode.DynamicIsland),
        CreateModePicker(SettingsSearchMode.DesktopCard),
        CreateModePicker(SettingsSearchMode.FloatingBall),

        // ---- 显示模式 · 任务栏 / Display modes, taskbar ----
        Create(
            1,
            "屏幕与位置",
            "媒体栏所在的显示器与任务栏位置",
            ["显示器", "屏幕", "monitor", "display", "朝向", "横向", "纵向", "orientation", "避让", "图标", "锁定", "位置", "偏移", "offset", "重置", "承载", "承载显示器", "承载与位置", "边缘偏移", "排列方向"]),
        Create(
            2,
            "媒体栏宽度",
            "宽度跟随内容、固定宽度与组件间距",
            ["长度", "尺寸", "宽度", "间距", "spacing", "length", "固定", "跟随", "组件", "width", "固定长度", "组件间距"]),
        Create(
            3,
            "静置层",
            "始终显示的基础层：信息密度、内容排列、对齐与媒体文字大小",
            ["静置", "常驻", "rest", "信息密度", "density", "精简", "均衡", "排列", "布局", "layout", "对齐", "标题", "歌手", "artist", "内容排列", "字号", "字体大小", "文字大小", "font size"]),
        Create(
            4,
            "悬停层",
            "鼠标移入媒体栏时出现的快捷控制",
            ["悬停", "hover", "鼠标", "按钮", "播放暂停", "上一首", "下一首", "输出设备", "音量", "进度"]),
        Create(
            5,
            "完整层",
            "点击媒体栏展开的面板包含哪些分区",
            ["完整", "面板", "full", "panel", "预设", "preset", "分区", "媒体信息", "播放控制", "音频控制", "性能", "套用预设"]),

        // ---- 显示模式 · 灵动岛 / Display modes, dynamic island ----
        Create(
            SettingsPageKey.DisplayModes,
            1,
            "灵动岛外观",
            "灵动岛的背景样式、不透明度与圆角，当前只读",
            ["灵动岛", "island", "外观", "表面", "surface", "背景样式", "不透明度", "opacity", "圆角", "radius", "未实现", "灵动岛表面", "基础表面风格"]) with
        {
            Mode = SettingsSearchMode.DynamicIsland,
        },

        // ---- 媒体与通知 / Media and notifications ----
        Create(
            SettingsPageKey.MediaAndNotifications,
            0,
            "媒体来源",
            "来源允许列表与已检测来源",
            ["来源", "source", "SMTC", "允许", "过滤", "filter", "白名单", "应用", "播放器", "媒体来源", "已检测来源", "允许列表"]),
        Create(
            SettingsPageKey.MediaAndNotifications,
            1,
            "快速启动",
            "无媒体时从音符启动播放器",
            ["快速启动", "quick", "launch", "启动", "音符", "播放器", "exe", "lnk", "浏览", "快捷方式"]),
        Create(
            SettingsPageKey.MediaAndNotifications,
            2,
            "静置层组件",
            "叠加在静置层右侧的频谱与性能组件",
            ["频谱", "spectrum", "均衡", "柱", "柱数", "刷新率", "灵敏度", "性能", "performance", "内存", "cpu", "gpu", "指标", "任务管理器", "柱子数量", "刷新速度", "跳动幅度", "样式", "波形", "波形图", "像素", "像素柱状图", "点阵", "对称", "上下对称", "采样间隔", "刷新间隔", "秒", "毫秒", "ms"]),
        Create(
            SettingsPageKey.MediaAndNotifications,
            3,
            "曲目切换通知",
            "曲目切换并开始播放时显示当前曲目",
            ["通知", "notification", "切歌", "曲目", "track", "位置", "锚点", "停留", "时长", "duration", "全屏", "显示器", "停留时间", "目标显示器"]),

        // ---- 交互 / Interaction ----
        Create(
            SettingsPageKey.Interaction,
            0,
            "共用的按键",
            "媒体栏与托盘共用的组合滚轮按键",
            ["修饰键", "modifier", "shift", "滚轮", "wheel", "组合", "chord", "左键", "右键", "共用修饰键", "按键"]),
        Create(
            SettingsPageKey.Interaction,
            1,
            "程序内（静置层）",
            "点在静置层上的结果：点封面、点文字、滚轮",
            ["点击", "click", "封面", "artwork", "标题", "歌词", "程序内", "绑定", "上一首", "下一首", "设备", "音量", "媒体源", "普通滚轮", "组合滚轮"]),
        Create(
            SettingsPageKey.Interaction,
            2,
            "托盘图标",
            "托盘图标的点击与滚轮行为",
            ["托盘", "tray", "通知区域", "溢出", "菜单", "音量", "设置", "设备"]),

        // ---- 歌词 / Lyrics ----
        Create(
            SettingsPageKey.Lyrics,
            0,
            "显示",
            "显示歌词、显示两行，以及第二行显示什么",
            ["歌词", "lyrics", "实时", "双行", "第二行", "翻译", "下一句", "translation", "实时歌词", "双行歌词"]),
        Create(
            SettingsPageKey.Lyrics,
            1,
            "对齐",
            "歌词靠左、居中还是靠右",
            ["对齐", "align", "左", "中", "右", "居中", "left", "center", "right"]),

        // ---- 外观 / Appearance ----
        Create(
            SettingsPageKey.Appearance,
            0,
            "字体",
            "英文字体、中文字体与字体粗细",
            ["字体", "font", "字重", "weight", "粗细", "西文", "英文", "中文", "预览", "preview", "segoe", "雅黑", "字体粗细"]),
        Create(
            SettingsPageKey.Appearance,
            1,
            "主题与背景",
            "浅色还是深色，以及窗口背景材质",
            ["主题", "theme", "浅色", "深色", "light", "dark", "材质", "backdrop", "mica", "云母", "acrylic", "亚克力", "动效", "motion", "动画", "背景材质", "交互动效"]),
        Create(
            SettingsPageKey.Appearance,
            2,
            "媒体栏文字",
            "媒体栏上文字的颜色",
            ["文字颜色", "foreground", "文字", "颜色", "自动", "浅色文字", "深色文字", "对比", "可读", "播放器文字"]),

        // ---- 应用与关于 / Application and about ----
        Create(
            SettingsPageKey.AppAndAbout,
            0,
            "应用",
            "版本信息、检查更新与自动更新；开机自动启动与界面语言仍为预留",
            ["更新", "update", "升级", "版本", "version", "检查更新", "自动更新", "自动下载", "下载", "安装", "安装程序", "静默安装", "重启", "加速", "镜像", "跳过此版本", "开机", "启动", "startup", "自启", "语言", "language", "中文", "预留"]),
        Create(
            SettingsPageKey.AppAndAbout,
            1,
            "设置文件",
            "打开设置文件夹与重置全部设置",
            ["设置文件", "settings.json", "文件夹", "folder", "打开", "重置", "reset", "恢复默认", "还原"]),
        Create(
            SettingsPageKey.AppAndAbout,
            2,
            "项目信息",
            "版本信息与反馈入口",
            ["版本", "version", "关于", "about", "github", "反馈", "issue", "star"]),
        Create(
            SettingsPageKey.AppAndAbout,
            3,
            "赞助支持",
            "赞助者名单",
            ["赞助", "sponsor", "支持", "捐赠", "donate"]),
    ];
}
