> The English release notes are provided in the second half of this document.

# AF Media Bar 1.2.0

AF Media Bar 1.2.0 是重构后的第一个版本：界面与交互全部重做，新增任务栏歌词、安装程序与目标显示器选择，并重新整理了设置结构。

## 下载与安装

本 Release 提供两个文件，都是自包含版本，不需要预先安装 .NET 10 Desktop Runtime：

- `AFMediaBar-Setup-v1.2.0-win-x64.exe`：安装程序。安装版可在程序内检查更新、后台下载并静默安装后续版本。
- `AFMediaBar-v1.2.0-win-x64.zip`：便携版。解压后运行其中唯一的 `AFMediaBar.exe`；便携版不写注册表，升级时手动替换文件。

请勿将 GitHub 自动生成的 Source code 压缩包作为程序使用。可通过同一 Release 中的 `SHA256SUMS.txt` 校验下载文件。

## 国内下载镜像

- 夸克网盘：[下载地址](https://pan.quark.cn/s/6987e4945b16)
- 百度网盘：[下载地址](https://pan.baidu.com/s/1zUQtZ_N1tnRTjJKd9kKREA?pwd=6ddc)，提取码：`6ddc`
- 蓝奏云：[下载地址](https://amorfate.lanzoue.com/b01eupanbg)，密码：`zzzz`

国内镜像中的压缩包应与 GitHub Release 中的文件完全一致，可使用 SHA-256 校验值进行核对。

## 本次亮点

- 全新外观、全新交互
- 任务栏歌词显示，歌词来源匹配（网易云、LRCLIB、QQ、酷狗、汽水）
- 安装程序，程序内后台下载/静默安装
- 目标显示器选择
- 主题、材质、强调色更换
- 当前播放曲目通知
- SMTC 应用允许列表（过滤）
- 快速启动（无媒体播放时点击/滚动音符快速打开应用）
- 托盘图标便捷滚轮交互，音频控制面板
- 新增频谱样式（波形图、像素柱状图）
- 后台内存占用自动压缩（分档回收、启动后剪枝）
- 注意：新版本安装后，竖向任务栏、悬浮模式暂不支持

## 已知限制

- 当前仅发布 `win-x64` 版本，尚未提供 ARM64 构建。
- 发布文件尚未进行商业代码签名，Windows SmartScreen 可能显示“未知发布者”。
- 竖向任务栏与悬浮模式尚未实现，设置中的对应入口目前是占位选项。
- **从 1.1.1 升级后设置回到默认值**：本版本只读取自己的设置编号（2），旧设置文件不会被读取，而是改名留档为 `settings.json.unsupported-<时间戳>`（不会删除，可手工找回），需要重新配置一次。
- 设置默认输出设备依赖未公开的 `PolicyConfig` COM 接口，Windows 更新或设备策略可能影响其可用性。

本版本没有已确认的阻断性问题。完整安装、卸载、常见问题和隐私说明见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。

---

# AF Media Bar 1.2.0

AF Media Bar 1.2.0 is the first release of the rewritten application: the interface and interactions were rebuilt from scratch, taskbar lyrics, an installer and target-display selection were added, and the settings structure was reorganised.

## Download and install

Both files attached to this release are self-contained and need no separate .NET 10 Desktop Runtime:

- `AFMediaBar-Setup-v1.2.0-win-x64.exe` — the installer. Installed copies can check for updates in-app and download and silently install later versions.
- `AFMediaBar-v1.2.0-win-x64.zip` — the portable build. Extract it and run the single `AFMediaBar.exe` inside; it writes no registry entries, so upgrading means replacing the file.

Do not use the Source code archives GitHub generates automatically. Verify your download against the `SHA256SUMS.txt` attached to the same release.

## Mirrors in mainland China

- Quark: [download](https://pan.quark.cn/s/6987e4945b16)
- Baidu: [download](https://pan.baidu.com/s/1zUQtZ_N1tnRTjJKd9kKREA?pwd=6ddc), code `6ddc`
- Lanzou: [download](https://amorfate.lanzoue.com/b01eupanbg), password `zzzz`

The archives on these mirrors are identical to the ones on GitHub and can be cross-checked with their SHA-256 values.

## Highlights

- A completely new look and interaction model.
- Taskbar lyrics with source matching (NetEase Cloud Music, LRCLIB, QQ Music, Kugou, Soda Music).
- An installer, with in-app background download and silent installation.
- Target display selection.
- Theme, material and accent colour switching.
- A now-playing track notification.
- An SMTC app allowlist (filtering).
- Quick launch: click or scroll the note while nothing is playing to open an app.
- Convenient tray-icon wheel interactions and an audio control panel.
- New spectrum styles (waveform and pixel bars).
- Automatic background memory trimming (staged reclamation and post-startup trimming).
- Note: vertical taskbars and floating mode are not supported yet in this version.

## Known limitations

- Only `win-x64` is published; there is no ARM64 build yet.
- The published files are not commercially code-signed, so Windows SmartScreen may report an unknown publisher.
- Vertical taskbars and floating mode are not implemented yet; the matching entries in settings are placeholders for now.
- **Upgrading from 1.1.1 resets your settings**: this version reads only its own schema number (2), so the older settings file is not read at all — it is renamed to `settings.json.unsupported-<timestamp>` and kept (nothing is deleted and it can be recovered by hand) — and the settings have to be configured once more.
- Setting the default output device relies on the undocumented `PolicyConfig` COM interface, so Windows updates or device policy may affect it.

No blocking issues are currently known. See the [English README](https://github.com/Fervent-Tempo/AF-Media-Bar/blob/main/README.en-US.md) for full documentation.
