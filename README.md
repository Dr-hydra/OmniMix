# OmniMix

OmniMix 是一个面向 Windows 的桌面音乐与游戏集成工具，由 Dr.Hydra 长期维护。

English version: [README_EN.md](README_EN.md)

当前版本：`4.4.2`

## 4.4.2 更新说明

- 新增 GUI English 本地化，支持跟随系统、简体中文和 English。
- 设置中的语言切换在重启 GUI 后生效。
- 补充主导航、播放控制、设置页、悬浮窗、托盘菜单和常见动态状态的英文文案。

## 项目状态

发布包通常包含：

- `OmniMixPlayer_V{version}_VBNet_portable.zip`：完整自包含便携包。
- `OmniMixPlayer_V{version}_VBNet_full-framework-dependent.zip`：完整框架依赖包。
- `OmniMixPlayer_V{version}_VBNet_installer.exe`：完整 Windows 安装器。

从 `3.0.7` 开始，Release 不再把 VB.NET 前端 exe 作为独立资产发布；它会被包含在完整 OmniMix 包中。

## 主要功能

- 启动或发现 OmniMix 后端，并显示连接状态。
- 提供播放控制、进度拖动、音量控制、循环/随机播放。
- 管理曲库、播放队列、播放历史和封面显示。
- 在桌面前端中承载模块 UI。
- 支持模块启停、模块设置、模块链接和启动台入口。
- 支持服务安装、启动、停止和自启动控制。
- 支持均衡器和实例配置。
- 支持游戏集成桥安装、实例 ID 修复和端口文件同步。
- 支持 FH6 游戏集成，包括 Steam 与 Xbox 不同目录结构识别。
- 支持通过 Better Endfield 官方 CLI 注册终末地音乐替换后端，不向其安装目录或游戏目录复制文件。
- 提供个性化界面设置，包括背景、不透明度、主题色和 HSL 自定义。
- 桌面 GUI 支持跟随系统语言、简体中文和 English；语言切换在重启 GUI 后生效。

VB.NET 界面层与 [QING.UIKIT](https://github.com/Dr-hydra/QING.UIKIT) 同步维护；QING.UIKIT 是从PCL2中整理出的可复用 WPF UI Kit。

## 使用说明

### 使用发布包

推荐普通用户使用安装器进行下载或者使用 [FH6Tools](https://github.com/Dr-hydra/FH6Tools/releases) 进行下载安装与更新。

便携包使用方式：

1. 解压完整包到一个可写目录。
2. 运行 `OmniMixPlayer.Gui.Vbnet.exe`。

安装器使用方式：

1. 运行 `OmniMixPlayer_V{version}_VBNet_installer.exe`。
2. 按向导完成安装。
3. 从开始菜单或安装目录启动 OmniMix。

### 界面语言

进入“设置 - 个性化 - 界面语言”，可选择“跟随系统”“简体中文”或“English”。
语言设置保存在当前用户配置中，并在重启 OmniMix GUI 后应用。音源模块自行返回的
RawNode 页面文案由对应模块决定，可能不随 GUI 语言变化。

### 游戏集成

进入“插件 - 游戏集成”页面后：

1. 选择支持的游戏。
2. 点击“选择游戏目录”。
3. 安装对应游戏集成桥。
4. 启动游戏前，建议先启动 OmniMix 前端，让前端刷新端口文件和实例绑定。

Better Endfield 使用独立的注册式接入：在对应详情页选择或自动定位包含
`BetterEndfield.exe`、`runtime/BetterEndfield.Host.dll` 和 `modules/` 的安装目录，
然后执行“注册”或“修复注册”。实时状态来自 Better Endfield 的 JSON CLI；登录、
主界面和游戏内音乐替换范围仍在 Better Endfield 中设置。OmniMix 不会向该目录写入
端口文件、Mod 或配置，解除注册也不会删除任何程序和用户文件。

本次 PCM ABI 升级不兼容旧游戏桥。FH6 Bridge `4.0.0` 和 ChillPatcher `2.0.0`
会校验 OmniPcmShared ABI 2；游戏集成页检测到旧版本时会显示更新操作。旧桥加载
不兼容 DLL 时会安全停用自定义音频，不应影响游戏启动，请按提示升级桥。

### FH6游戏内设置

将DJ调整为关，将主播模式调整为开并重启游戏。

### 自定义电台 UI

FH6 集成页面提供“替换电台 UI”功能：

1. 选择有效 FH6 目录。
2. 点击“替换电台 UI”。
3. 选择可读取的 PNG 并输入自定义电台名称。图片会直接缩放到游戏电台徽标尺寸（普通版 196×104，HiRes 版 392×208）；为避免拉伸变形，建议使用 49:26 宽高比、至少 392×208 的 RGB/RGBA PNG。不透明背景的显示效果最可预测。
4. 前端会调用媒体生成器生成文件、备份原始文件并写入游戏 `media` 目录。

如需恢复，点击“还原原始电台 UI”。

### 卸载

1. 先在游戏集成页面卸载游戏集成。
2. 如果你使用的是便携包，直接删除即可，使用安装器安装的在开始菜单->设置->应用中进行卸载。

## 构建

安装项目所需 .NET SDK、Node.js、Go、CMake、Visual Studio C++ 工具链后，可先运行桌面前端快速构建：

```powershell
dotnet build "OmniMixPlayer/gui_vbnet/OmniMixFrontend.sln" -c Debug -v minimal
```

播放器构建统一使用任务树脚本。当前只有一个桌面前端，即 VB.NET/WPF 前端；`player` 构建会依次发布后端、构建内嵌 WebUI、发布 VB.NET 前端、构建媒体生成器、打包游戏集成资源，并组装到 `playerbuild/`：

```powershell
python scripts/build_all.py player
```

完整构建会额外执行 restore 和原生组件构建：

```powershell
python scripts/build_all.py player --full
```

如需先查看任务树：

```powershell
python scripts/build_all.py player --full --dry-run
```

`playerbuild/` 是安装器和发布包的基础目录，关键文件包括：

- `OmniMixPlayer.Gui.Vbnet.exe`
- `OmniMixPlayer.Backend.exe`
- `chill-gen-media.exe`
- `OmniMixAssets/ChillPatcher.zip`
- `OmniMixAssets/FH6OmniBridge.zip`
- `OmniMixAssets/OmniPcmSharedSDK-2.0.0.zip`
- `tools/vgmstream/vgmstream-cli.exe` 及其官方 Windows x64 运行库、许可证和来源说明
- `modules/`
- `native/x64/`
- `wwwroot/`

FH6 DJ 语音的离线准备使用固定版本的 `vgmstream`。构建会校验 `OmniMixPlayer/assets/tools/vgmstream/` 中官方压缩包的 SHA256，再解压到 `playerbuild/tools/vgmstream/`；发布包不包含任何游戏原版语音或本地缓存。

`OmniPcmSharedSDK-2.0.0.zip` 是独立版本的 Windows x64 C ABI 交付包，包含
静态 MSVC 运行时 DLL、头文件、`VERSION`、`SHA256SUMS`、README 和不依赖曲库的
48 kHz 双声道测试流。规范实例映射名为
`Global\OmniMixPlayer_PCM_<instance_id>`；普通用户无权创建全局映射时，后端与 SDK
透明回退到同后缀的 `Local\` 映射。

生成发布 zip：

```powershell
python scripts/package_release.py 4.4.2
```

生成安装器：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build_installer.ps1 -Version 4.4.2
```

当前后端内嵌 WebUI 位于 `OmniMixPlayer/gui_web/`，使用 Vite + Svelte + TypeScript 构建。完整播放器构建会自动执行 WebUI 构建并复制到后端 `wwwroot/`；单独调试时可在该目录运行：

```powershell
npm install
npm run dev
```

## 分支说明

- 当前 VB.NET/WPF 分支是主维护分支。
- `main` 分支保留用于历史基线和未来必要的拉取/比对。

## 社区与交流

- B站主页：[Dr.Hydra](https://space.bilibili.com/441133155)
- 小黑盒主页：[Dr.Hydra](https://www.xiaoheihe.cn/app/user/profile/38080236)
- QQ群：`851586605`

## 关于原项目

OmniMix 的部分历史实现来自 BeyondtheApex 的 ChillPatcher 项目。感谢原作者的早期工作。本仓库现在作为 Dr.Hydra 长期维护的分支版本继续演进。

原项目仓库：

```text
https://github.com/BeyondtheApex/ChillPatcher
```

## 开源协议

本项目按 GNU General Public License v3.0 开源，详见 [LICENSE](LICENSE)。

第三方组件保留其各自目录中的原始协议。
