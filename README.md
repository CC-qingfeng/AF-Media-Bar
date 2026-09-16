# AF Media Bar

<div align="center">

  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/releases">
    <img src="https://img.shields.io/github/v/release/Fervent-Tempo/AF-Media-Bar?style=flat-square" alt="Latest release">
  </a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/releases">
    <img src="https://img.shields.io/github/downloads/Fervent-Tempo/AF-Media-Bar/total?style=flat-square" alt="Downloads">
  </a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/stargazers">
    <img src="https://img.shields.io/github/stars/Fervent-Tempo/AF-Media-Bar?style=flat-square" alt="Stars">
  </a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues">
    <img src="https://img.shields.io/github/issues/Fervent-Tempo/AF-Media-Bar?style=flat-square" alt="Issues">
  </a>
  <a href="LICENSE">
    <img src="https://img.shields.io/github/license/Fervent-Tempo/AF-Media-Bar?style=flat-square" alt="MIT License">
  </a>

  <br><br>

  <img src="docs/assets/af-media-bar.png" alt="AF Media Bar" width="160" height="160">

  <h1>AF Media Bar</h1>

  <p>Windows 10/11 任务栏上的媒体控制、音频设备切换与轻量系统指标。</p>

  <p>
    简体中文
    ·
    <a href="README.en-US.md">English</a>
    <br>
    <a href="#安装">快速开始</a>
    ·
    <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=bug_report.yml">报告问题</a>
    ·
    <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=feature_request.yml">功能建议</a>
  </p>

</div>

## 展示

### 运行展示


<div align="center">

![AF Media Bar 运行展示](./docs/assets/运行展示.gif)

</div>


### 介绍视频


### - [在 Bilibili 观看 AF Media Bar 介绍视频](https://www.bilibili.com/video/BV1Bjuq6bErr)




## 目录


- [AF Media Bar](#af-media-bar)
  - [展示](#展示)
    - [运行展示](#运行展示)
    - [介绍视频](#介绍视频)
    - [- 在 Bilibili 观看 AF Media Bar 介绍视频](#--在-bilibili-观看-af-media-bar-介绍视频)
  - [目录](#目录)
  - [简介](#简介)
  - [功能](#功能)
  - [工作方式](#工作方式)
  - [安装](#安装)
    - [系统要求](#系统要求)
    - [推荐方式](#推荐方式)
  - [使用](#使用)
  - [更新与卸载](#更新与卸载)
    - [更新](#更新)
    - [卸载](#卸载)
  - [隐私与安全](#隐私与安全)
  - [从源码构建](#从源码构建)
  - [项目结构](#项目结构)
  - [参与贡献](#参与贡献)
  - [License](#license)


## 简介

AF Media Bar 是一款便携式 Windows 10/11 媒体控制器。它读取 Windows 全局系统媒体会话（GSMTC），显示当前媒体的封面、标题和作者，并提供上一首、播放/暂停、下一首和来源切换。

程序以独立进程运行，将 WPF 播放器窗口挂载为任务栏子窗口；设置页中的灵动岛、桌面卡片和悬浮球当前仅为未实现的前端选项，不会切换运行模式。程序不修改、不向 `explorer.exe` 注入代码。网易云音乐、QQ 音乐、Spotify、浏览器等应用只要向 Windows 发布媒体会话，就可以被发现和控制。
## 功能
<div align="center">


| 类别 | 功能 |
| --- | --- |
| 媒体控制 | 上一首、播放/暂停、下一首、循环、点击轨道直接跳转或拖动进度；不可用按钮保持透明并降低图标不透明度；完整层始终保留显式按钮 |
| 来源交互 | 封面与标题/歌词点击结果可分别绑定为播放暂停或切回媒体应用；右键媒体栏切换来源；可在“媒体与通知”按应用来源启用 SMTC 允许列表 |
| 曲目切换通知 | 可选显示刚切换并开始播放的当前曲目，支持六个位置、1–10 秒、全屏抑制，以及固定显示器或前台窗口所在显示器 |
| 实时歌词 | 歌词只存在于任务栏静置层；可用时自动替换标题与歌手/来源，并在独立歌词页设置双行内容和对齐 |
| 任务栏适配 | 横向任务栏保留原播放器静置外观；控制层直接模糊/淡化原文字区并保持按钮清晰，完整层位于任务栏外；可独立选择固定屏幕 |
| 窗口模式 | 设置页顶部以四张卡片展示任务栏、灵动岛、桌面卡片和悬浮球；只有任务栏是当前运行模式，后三种标记为未实现，选中只切换本页显示的区域并给出占位提示，不会切换正在运行的窗口 |
| 外观设置 | 字体、字体粗细、媒体栏文字、浅色/深色主题与窗口背景材质全局统一；自动文字色按任务栏实际底色或透明区域后的壁纸切换深浅，复杂背景增加轻微反色阴影；任务栏主体当前固定透明，灵动岛外观（背景样式、不透明度、圆角）在其模式分区内只读展示 |
| 轻度自定义 | 静置层始终开启，可设置密度、布局、标题/歌手对齐及频谱/性能显隐；悬停层和完整层可开关并分别选择控制项和内容组；固定长度受当前悬停层最小值和任务栏可用区间限制 |
| 显示多少内容 | 极简、均衡、信息优先会统一改变按钮、进度与封面—文字—频谱间隔；悬停最小宽度只约束中间文字区，无媒体时不保留该空白区域 |
| 自动隐藏 | 可在所有媒体会话均停止播放时隐藏；折叠容器使用锚点容器和公共边展开，四向折叠仍需真实 Windows 验收 |
| 托盘音频控制 | 左键单击可绑定音频面板、设置或右键菜单；普通/组合滚轮分别绑定输出设备或当前媒体音量，并与媒体栏内滚轮共用 Shift、鼠标左键或右键按键 |
| 快速启动 | 无可接收媒体时单击音符选择用户维护的播放器；滚轮循环预览并在停止约 1.2 秒后只启动最后一项，支持检测来源、EXE、LNK 与 Store 应用 |
| 音频设备 | 在稳定排序的设备列表中切换默认输出设备；横向任务栏、音频控制窗与完整层均可滚轮预览，并在停止滚动后应用 |
| 应用音量 | 面板跨活动输出端点聚合应用图标和音频会话；横向任务栏、音频控制窗、完整层及托盘滚轮均按 2% 调节当前媒体应用音量 |
| 音频可视化 | 基于 WASAPI 回环采样的频谱在媒体连接期间保留固定槽位，播放时刷新、暂停时清零；可在“媒体与通知 → 静置层组件”中显示频谱并调整 1–9 根柱子、5–30 Hz 刷新速度与 1–400% 跳动幅度 |
| 系统指标 | 横向任务栏尾部可显示紧凑性能组件，轮换系统内存、CPU、GPU 与本程序内存；完整面板仍显示四项，统一协调器在无消费者时停止采样 |
| 低性能自动降级 | 检测到 WPF 软件渲染或低性能路径时，自动关闭装饰性连续动效、滚动文字、频谱缓动、模糊和窗口背景效果 |

</div>


## 工作方式
<div align="center">

```mermaid
flowchart LR
    A[媒体应用] -->|GSMTC 会话| B[AF Media Bar]
    C[Windows Core Audio] -->|设备、音量、回环采样| B
    D[Windows 10/11 任务栏] -->|位置与自动隐藏状态| B
    B --> E[WPF 任务栏子窗口]
```

</div>

Windows 10/11 控制中心里的媒体卡片是 Explorer/Shell 的内部界面，不是公开可嵌入的控件。AF Media Bar 复用其背后的公开 GSMTC 接口，并自行渲染界面，从而避免注入 Explorer 带来的稳定性和安全风险。

## 安装

### 系统要求

- Windows 10 版本 1809（内部版本 17763）或更高版本，x64
- 安装程序与便携版都自带 .NET 运行时，无需另行安装

### 方式一：安装程序（推荐）

1. 打开 [Releases](https://github.com/Fervent-Tempo/AF-Media-Bar/releases)，下载 `AFMediaBar-Setup-vX.Y.Z-win-x64.exe`，不要下载 GitHub 自动生成的 Source code 压缩包。
2. 运行安装程序。向导先让你选择**简体中文或英文**，然后可以查看许可协议、**选择安装位置**，并决定只为当前用户安装还是为所有用户安装；默认落在 `%LOCALAPPDATA%\Programs\AFMediaBar`，不需要管理员权限。
3. 桌面快捷方式是可选项，开始菜单项始终创建。安装完成后即可启动。

安装版可以在程序运行时检查更新、在后台下载并在退出时静默安装新版本，详见「更新与卸载」。

### 方式二：便携版

1. 在同一个 Releases 页面下载 `AFMediaBar-vX.Y.Z-win-x64.zip`。
2. 解压后会得到单个自包含的 `AFMediaBar.exe`，不再附带数百个 .NET 运行时文件。
3. 将它放到一个长期保留且可写的目录，例如 `D:\AFMediaBar`，然后运行。便携版不写注册表，升级时需要手动替换文件。

两种方式都只发布在 GitHub Releases。国内网络访问 GitHub 不稳定时，同一页面可以用 GH-Proxy 加速地址下载（`https://<加速站点>/https://github.com/...`）；程序内的更新检查与自动下载在直连失败时也会自动改用加速地址。

启动后右键播放器或托盘图标打开设置，在“显示模式、媒体与通知、交互、歌词、外观、应用与关于”中完成自定义。

AF Media Bar 暂未进行商业代码签名，因此 Windows SmartScreen 可能在首次运行或安装时显示未知发布者提示。

## 使用
<div align="center">

| 操作 | 结果 |
| --- | --- |
| 点击封面 | 默认播放/暂停，也可在“交互”页改为打开播放中的程序 |
| 无媒体时点击或滚动音符 | 点击打开快速启动列表；滚轮循环预览，停止约 1.2 秒后启动最后一项；列表在“媒体与通知 → 快速启动”中维护 |
| 点击标题或歌词 | 默认切回当前媒体应用，也可改为播放/暂停 |
| 在媒体区域向上/向下滚轮 | 普通和“按住键 + 滚轮”可分别绑定上下首、切换播放器、输出设备或当前媒体音量；默认直接滚轮切歌、Shift + 滚轮切换播放器 |
| 右键媒体栏并选择“切换媒体源” | 在多个媒体会话之间切换 |
| 在“媒体与通知 → 切歌提示”中开启提示 | 曲目真正切换并开始播放时显示当前曲目；可选择位置、显示时长、全屏策略及固定/跟随屏幕 |
| 在“显示模式”中选择显示屏幕 | 关闭已打开的完整面板，并通过安全重载链把任务栏媒体栏重建到目标屏；目标暂时断开时回退主屏且保留原偏好 |
| 在“显示模式 → 媒体栏宽度”中关闭宽度跟随 | 在当前所需最小宽度与任务栏可用宽度之间选择固定值；超出的标题、歌手和歌词在裁剪区域内往返滚动 |
| 单击 AF Media Bar 托盘图标 | 打开输出设备、空间音效状态和应用音量面板 |
| 在音频控制窗或完整层的输出设备行滚轮 | 立即预览设备，停止滚动约 1.2 秒后切换；完整层设备框为长名称提供更大的显示区域和完整提示 |
| 点击任务栏输出设备或音量图标 | 打开紧凑设备列表或当前媒体应用音量滑块；图标与菜单均支持滚轮 |
| 悬浮 AF Media Bar 托盘图标 | 使用 Windows 原生提示显示默认滚轮操作及当前值 |
| 在托盘图标上滚轮 | 默认普通滚轮切换输出设备、Shift + 滚轮按 2% 调节当前媒体音量；设备候选立即更新原生提示，停止滚动约 1.2 秒后应用 |
| 悬停任务栏媒体栏 | 有媒体时封面、文字和播放中的频谱分别显示原控件 hover；控制层打开时直接模糊/淡化原文字，按钮保持清晰；关闭悬停层后可用文字区顶部细杠打开完整层 |
| 点击空间音效行 | 显示当前空间音效并打开“系统 > 声音 > 所有声音设备”；第三方应用不能可靠代替系统切换 Dolby/DTS 模式 |
| 拖动长条空白区域 | 调整手动位置，锁定后禁止拖动 |
| 点击灵动岛、桌面卡片或悬浮球模式 | 只切换设置页选中状态并显示“未实现”，不会改变任务栏运行模式；选中期间显示模式页的任务栏设置会变为只读，页面顶部有提示条，点回“任务栏”即可继续编辑 |
| 将边缘折叠容器放到桌面边缘 | 鼠标移到触发区域时展开；移开后隐藏组件内容 |
| 右键播放器或托盘图标 | 打开详细设置、媒体操作与退出菜单；左键点击菜单外部即关闭 |


</div>

媒体应用必须向 Windows 发布 GSMTC 会话。部分播放器需要在自身设置中启用“系统媒体控制”“媒体键”或“SMTC”。

曲目切换通知不读取播放队列，也不是“下一首预览”；Windows SMTC 不提供待播列表。它只根据“来源标识 + 标准化标题”识别当前曲目变化，因此同一来源中连续播放两首同名曲目无法可靠区分。真正的待播列表需要播放器专用 API 或插件。

## 更新与卸载

### 更新

程序启动约 20 秒后读取公开版本清单（`docs/latest.json`），成功后 24 小时内不再自动检查，失败 1 小时后重试；也可以在“应用与关于”页立即检查。发现新版本时：

- 托盘图标弹出一次系统通知，点击它直接打开“应用与关于”页；托盘右键菜单与任务栏媒体栏右键菜单里也会出现一行随状态变化的更新入口（检查更新 / 正在下载 n% / 更新已就绪）；
- 该页显示版本亮点、下载进度与全部更新操作，**下载由你显式点击“下载并安装”开始**（约 70 MB，边下边校验 SHA-256），程序不会自行消耗这部分流量；
- 校验通过后显示“更新已就绪”，此时**下一次启动程序时会先完成安装，再启动新版本**——不会在你刚退出程序之后弹出安装窗口；也可以在设置页点“立即重启并安装”立刻完成；
- 安装包的完整性只由 SHA-256 判定（清单里的 `size` 只是供人工核对的参考值，写错或省略都不影响更新）。

下载只走直链：先直连 GitHub，失败后按清单里的加速地址（GH-Proxy）顺序重试；所有渠道都不可用时状态会显示失败原因，并提供“GitHub”和“加速镜像”两个下载页入口。安装包的 SHA-256 来自清单，校验不通过的文件会被删除且永远不会被安装。便携版没有安装记录，因此只下载不安装，需要手动替换程序文件。

安装日志写在 `%LOCALAPPDATA%\AFMediaBar\updates\install-<版本>.log`，可用于排查静默安装失败；已下载的安装包也放在同一目录，会在下次启动时按版本清理。

用户偏好和窗口状态统一保存在 `%LOCALAPPDATA%\AFMediaBar\settings.json`，采用版本化 JSON、原子写入和备份恢复。布局档案及组件属性仍保存在 `%LOCALAPPDATA%\AFMediaBar\profiles\layout.json`。替换程序文件不会丢失设置；可在设置的“应用与关于”页打开设置文件夹。

### 卸载

- 安装版：在“设置 > 应用 > 已安装的应用”中卸载 AF Media Bar，或使用开始菜单里的卸载项；卸载只删除程序目录与快捷方式，**不**删除 `%LOCALAPPDATA%\AFMediaBar`。
- 便携版：直接删除程序目录。

如需同时清除设置与已下载的安装包，可在 PowerShell 中执行：

```powershell
Remove-Item "$env:LOCALAPPDATA\AFMediaBar" -Recurse -Force
```

## 隐私与安全

- 不包含遥测、广告、账号系统或联网分析代码。
- 更新检查只请求两个公开的版本清单端点（`raw.githubusercontent.com` 与 `jsdelivr` 上的 `docs/latest.json`），不经任何第三方代理；安装包下载只在开启“自动下载安装”或手动点击下载时发生，直连失败时可能经清单里配置的 GH-Proxy 加速地址获取，而安装包的 SHA-256 始终来自非代理端点获取的清单。歌词和远程封面功能还会按当前媒体信息请求已配置的歌词/图片服务，不上传设备信息或用户设置。
- 媒体信息、系统指标和音量操作均在本机处理。
- 程序以当前用户权限运行，不请求管理员权限，也不向 Explorer 注入代码。
- 安全问题请按 [SECURITY.md](SECURITY.md) 私下报告，不要在公开 Issue 中披露利用细节。

## 从源码构建

需要 Windows 10 版本 1809 或更高版本、[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 和 PowerShell。仓库通过 `global.json` 固定受支持的 SDK 特性带（feature band）。

```powershell
git clone https://github.com/Fervent-Tempo/AF-Media-Bar.git
cd AF-Media-Bar
dotnet restore .\src\AFMediaBar.slnx
dotnet build .\src\AFMediaBar.slnx -c Release --no-restore
dotnet test .\src\AFMediaBar.slnx -c Release --no-build
dotnet run --project .\src\AFMediaBar\AFMediaBar.csproj
```

生成供普通用户使用的自包含单文件：

```powershell
dotnet publish .\src\AFMediaBar\AFMediaBar.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\AFMediaBar-win-x64
```

## 项目结构


```text
AF-Media-Bar/
├── .github/
│   ├── ISSUE_TEMPLATE/            # Issue 表单
│   └── workflows/                 # 构建与发布工作流
├── src/
│   └── AFMediaBar/                # WPF 应用主项目
│       ├── Classes/               # 核心业务逻辑和服务
│       │   ├── Abstractions/      # 抽象接口定义
│       │   ├── Converters/        # WPF 值转换器
│       │   ├── Interop/           # Windows API 互操作
│       │   ├── Models/            # 数据模型
│       │   │   └── Layout/        # 布局数据模型（LayoutSchema、ComponentConfig 等）
│       │   ├── Services/          # 业务服务
│       │   │   ├── Layout/        # 布局预设和渲染引擎
│       │   │   ├── Lyrics/        # 歌词服务（网易云音乐歌词获取与解析）
│       │   │   ├── Players/       # 媒体播放器服务
│       │   │   └── Win32/         # Windows 系统服务
│       │   ├── Settings/          # 设置管理
│       │   └── Utils/             # 工具类
│       ├── Components/            # 可复用 UI 组件（TaskBarMediaControl 等）
│       ├── ViewModels/            # MVVM ViewModels
│       │   ├── Pages/             # 页面 ViewModel
│       │   └── Windows/           # 窗口 ViewModel
│       └── Views/                 # XAML 视图
│           ├── Pages/             # 设置页面视图
│           └── Windows/           # 窗口视图
├── docs/                          # 项目文档与资源
├── AFMediaBar.slnx                # 解决方案文件
└── README.md                      # 项目说明
```


## 参与贡献

提交问题或代码前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。错误报告请附 Windows 版本、AF Media Bar 版本、媒体播放器和完整复现步骤。

版本变化记录见 [CHANGELOG.md](CHANGELOG.md)。

## License

AF Media Bar 使用 [MIT License](LICENSE) 开源。

<div align="center">

如果 AF Media Bar 对你有帮助，可以给项目一个 Star❤️。

</div>
