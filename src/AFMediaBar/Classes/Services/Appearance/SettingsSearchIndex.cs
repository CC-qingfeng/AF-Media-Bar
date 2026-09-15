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

    private static SettingsSearchEntry[] Build() =>
    [
        // ---- 显示模式 / Display modes ----
        Create(
            SettingsPageKey.DisplayModes,
            0,
            "承载与位置",
            "显示器、排列方向、任务栏避让、边缘偏移与媒体栏尺寸",
            ["显示器", "屏幕", "monitor", "display", "朝向", "横向", "纵向", "orientation", "避让", "图标", "锁定", "位置", "偏移", "offset", "长度", "尺寸", "宽度", "间距", "spacing", "length"]),
        Create(
            SettingsPageKey.DisplayModes,
            1,
            "静置层",
            "常驻层的信息密度、内容排列与标题歌手对齐",
            ["静置", "常驻", "rest", "信息密度", "density", "精简", "均衡", "排列", "布局", "layout", "对齐", "标题", "歌手", "artist"]),
        Create(
            SettingsPageKey.DisplayModes,
            2,
            "悬停层",
            "鼠标移入时展开的快捷控制按钮",
            ["悬停", "hover", "鼠标", "按钮", "播放暂停", "上一首", "下一首", "输出设备", "音量", "进度"]),
        Create(
            SettingsPageKey.DisplayModes,
            3,
            "完整层",
            "展开面板包含哪些分区，以及紧凑与完整预设",
            ["完整", "面板", "full", "panel", "预设", "preset", "分区", "媒体信息", "播放控制", "音频控制", "性能"]),
        Create(
            SettingsPageKey.DisplayModes,
            4,
            "其他承载模式",
            "灵动岛、桌面卡片与悬浮球的入口，均尚未实现",
            ["灵动岛", "island", "桌面卡片", "card", "悬浮球", "ball", "模式", "mode", "未实现"]),

        // ---- 媒体与通知 / Media and notifications ----
        Create(
            SettingsPageKey.MediaAndNotifications,
            0,
            "媒体来源",
            "SMTC 来源过滤与已检测来源的允许列表",
            ["来源", "source", "SMTC", "允许", "过滤", "filter", "白名单", "应用", "播放器"]),
        Create(
            SettingsPageKey.MediaAndNotifications,
            1,
            "快速启动",
            "没有媒体时从音符启动播放器",
            ["快速启动", "quick", "launch", "启动", "音符", "播放器", "exe", "lnk", "浏览"]),
        Create(
            SettingsPageKey.MediaAndNotifications,
            2,
            "静置层组件",
            "频谱与性能组件的开关和参数",
            ["频谱", "spectrum", "均衡", "柱", "柱数", "刷新率", "灵敏度", "性能", "performance", "内存", "cpu", "gpu", "指标", "任务管理器"]),
        Create(
            SettingsPageKey.MediaAndNotifications,
            3,
            "曲目切换通知",
            "切歌通知的开关、位置、停留时间与目标显示器",
            ["通知", "notification", "切歌", "曲目", "track", "位置", "锚点", "停留", "时长", "duration", "全屏", "显示器"]),

        // ---- 交互 / Interaction ----
        Create(
            SettingsPageKey.Interaction,
            0,
            "共用修饰键",
            "程序内与托盘共用的组合滚轮修饰键",
            ["修饰键", "modifier", "shift", "滚轮", "wheel", "组合", "chord", "左键", "右键"]),
        Create(
            SettingsPageKey.Interaction,
            1,
            "程序内（任务栏静置层）",
            "点击封面或文字、普通滚轮与组合滚轮绑定",
            ["点击", "click", "封面", "artwork", "标题", "歌词", "程序内", "绑定", "上一首", "下一首", "设备", "音量", "媒体源"]),
        Create(
            SettingsPageKey.Interaction,
            2,
            "托盘图标",
            "托盘图标的点击与滚轮行为",
            ["托盘", "tray", "通知区域", "溢出", "菜单", "音频控制", "设置", "设备", "音量"]),

        // ---- 歌词 / Lyrics ----
        Create(
            SettingsPageKey.Lyrics,
            0,
            "显示",
            "实时歌词、双行歌词与第二行内容",
            ["歌词", "lyrics", "实时", "双行", "第二行", "翻译", "下一句", "translation"]),
        Create(
            SettingsPageKey.Lyrics,
            1,
            "对齐",
            "歌词在静置层的对齐方式",
            ["对齐", "align", "左", "中", "右", "居中", "left", "center", "right"]),

        // ---- 外观 / Appearance ----
        Create(
            SettingsPageKey.Appearance,
            0,
            "字体",
            "西文与中文字体、字重和字号预览",
            ["字体", "font", "字重", "weight", "粗细", "西文", "中文", "预览", "preview", "segoe", "雅黑"]),
        Create(
            SettingsPageKey.Appearance,
            1,
            "主题与材质",
            "应用明暗主题、窗口背景材质与动效状态",
            ["主题", "theme", "浅色", "深色", "light", "dark", "材质", "backdrop", "mica", "云母", "acrylic", "亚克力", "动效", "motion", "动画"]),
        Create(
            SettingsPageKey.Appearance,
            2,
            "播放器文字",
            "任务栏媒体文字的自动深浅与强制模式",
            ["文字颜色", "foreground", "文字", "颜色", "自动", "浅色文字", "深色文字", "对比", "可读"]),
        Create(
            SettingsPageKey.Appearance,
            3,
            "灵动岛表面（未实现）",
            "灵动岛模式的基础表面风格、不透明度与圆角",
            ["灵动岛", "island", "表面", "surface", "不透明度", "opacity", "圆角", "radius", "未实现"]),

        // ---- 应用与关于 / Application and about ----
        Create(
            SettingsPageKey.AppAndAbout,
            0,
            "应用",
            "开机自动启动、界面语言与检查更新，均尚未接入",
            ["开机", "启动", "startup", "自启", "语言", "language", "中文", "更新", "update", "预留"]),
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
