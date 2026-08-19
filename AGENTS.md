# AGENTS.md

## 项目定位

本仓库是 OmniMix 的长期维护分支版本，由 Dr.Hydra 维护。

- 项目名称统一使用 `OmniMix`。
- VB.NET/WPF 前端所在分支是当前主开发分支。
- `main` 分支保留，用于保留历史基线并方便未来必要时从原项目拉取或比对。
- 原作者和原项目仅作为历史来源、致谢和必要的兼容参考出现，不再把本仓库定位为单纯的上游兼容前端。

## 历史来源

- 原项目仓库：`https://github.com/BeyondtheApex/ChillPatcher`
- 如果需要参考原项目行为，应优先作为历史实现和协议兼容依据，而不是默认把本仓库改回上游从属结构。

## 分支与拉取策略

- 当前 VB.NET 分支是主维护分支，日常开发和发布工作优先在该分支进行。
- 保留 `main` 分支，便于必要时与原项目或历史主线做差异比较。
- 如需从原项目拉取更新，优先使用临时同步分支，不要直接把外部 `main` 合入当前主开发分支。

## 发布打包

- 公开发布优先包含：
  - 完整自包含便携包
  - 完整框架依赖包
  - Windows 安装器
- 完整包应包含后端、模块、原生库、Web 资源、媒体生成器、配置文件、VB.NET 桌面前端，以及 DJ 离线准备所需的固定版本 `vgmstream` 运行时。
- 如果包内仍包含来自原项目的后端/runtime 资产，保留简短来源说明，例如 `BACKEND_UPSTREAM.txt`，写明来源仓库、tag、asset URL 和 SHA256。
- 发布说明使用中文。

## 后端与 SDK 方向

- 前端期望后端可执行文件名为 `OmniMixPlayer.Backend.exe`。
- 桌面端优先使用 `OmniMixPlayer.SDK` 和 `.NET` gRPC-Web 客户端。
- `OmniMixApiClient` 继续作为页面层兼容适配器，尽量保持现有公开方法签名稳定。
- REST 调用只保留给 SDK 暂未覆盖的功能，例如后端配置、停止后端、模块启停、模块 UI/link/settings 等。
- 避免为了前端页面便利而随意改变后端协议；确需改变时，同时更新 SDK、文档和兼容逻辑。

## 代码架构与定位速查

优先按下面路径定位代码，避免一开始全仓库大范围阅读。

### VB.NET/WPF 前端

- 解决方案：`OmniMixPlayer/gui_vbnet/OmniMixFrontend.sln`
- 前端项目：`OmniMixPlayer/gui_vbnet/OmniMixFrontend/`
- 共享 UI/工具库：`OmniMixPlayer/gui_vbnet/OmniMixCore/`
- 主窗口与全局热键：`OmniMixPlayer/gui_vbnet/OmniMixFrontend/FormMain.xaml.vb`
- OmniMix 主要页面：
  - `OmniMixPlayer/gui_vbnet/OmniMixFrontend/Pages/PageOmniMix/PageOmniMixLeft.xaml.vb`
  - `OmniMixPlayer/gui_vbnet/OmniMixFrontend/Pages/PageOmniMix/PageOmniMixRight.xaml.vb`
- 常用控件：`OmniMixPlayer/gui_vbnet/OmniMixFrontend/Controls/`
- 基础网络/下载/通用逻辑：`OmniMixPlayer/gui_vbnet/OmniMixFrontend/Modules/Base/`
- 旧 PCL/QING 风格通用模块：`OmniMixPlayer/gui_vbnet/OmniMixFrontend/Modules/`

### 前端 OmniMix 适配层

- 后端进程发现、启动、生命周期：`OmniMixPlayer/gui_vbnet/OmniMixFrontend/Modules/OmniMix/OmniMixBackendManager.vb`
- API/SDK 兼容适配器：`OmniMixPlayer/gui_vbnet/OmniMixFrontend/Modules/OmniMix/OmniMixApiClient.vb`
- 模块 UI 渲染：`OmniMixPlayer/gui_vbnet/OmniMixFrontend/Modules/OmniMix/OmniMixRawNodeRenderer.vb`
- 桌面 PCM 播放：`OmniMixPlayer/gui_vbnet/OmniMixFrontend/Modules/OmniMix/OmniMixDesktopPcmPlaybackSink.vb`
- Windows 服务控制：`OmniMixPlayer/gui_vbnet/OmniMixFrontend/Modules/OmniMix/OmniMixPlatformService.vb`
- 游戏集成安装/卸载/绑定修复：`OmniMixPlayer/gui_vbnet/OmniMixFrontend/Modules/OmniMix/OmniMixModDeploymentService.vb`

### SDK 与后端

- SDK 项目：`OmniMixPlayer/OmniMixPlayer.SDK/`
- gRPC proto：`OmniMixPlayer/OmniMixPlayer.SDK/Protos/`
- 后端项目：`OmniMixPlayer/OmniMixPlayer.Backend/`
- 后端入口：`OmniMixPlayer/OmniMixPlayer.Backend/Program.cs`
- HTTP/REST 层：`OmniMixPlayer/OmniMixPlayer.Backend/Http/`
- gRPC 服务实现：`OmniMixPlayer/OmniMixPlayer.Backend/Services/`
- 播放、曲库、实例、时间线核心：`OmniMixPlayer/OmniMixPlayer.Backend/Audio/`
- 模块系统：`OmniMixPlayer/OmniMixPlayer.Backend/ModuleSystem/`

### WebUI

- 当前 WebUI：`OmniMixPlayer/gui_web/`
- 技术栈：Vite + Svelte + TypeScript，构建产物复制到 `OmniMixPlayer/OmniMixPlayer.Backend/wwwroot/`。
- WebUI 定位为后端内嵌远程控制台，优先覆盖后端状态、模块管理、模块 RawNode UI、配置摘要、事件推送等能力。
- 播放器安装/游戏集成资产统一放在 `OmniMixPlayer/assets/`。游戏集成压缩包和版本化 `OmniPcmSharedSDK-{version}.zip` 复制到 `playerbuild/OmniMixAssets/`；固定版本的第三方运行时压缩包经哈希和成员校验后解压到对应运行目录，例如 `playerbuild/tools/vgmstream/`。

### 模块与原生组件

- 内置音乐模块：`OmniMixPlayer/modules/`
- Spotify 模块：`OmniMixPlayer/modules/Spotify/`
- QQMusic 模块：`OmniMixPlayer/modules/QQMusic/`
- 原生插件：`NativePlugins/`
- 共享 PCM SDK/原生库：`NativePlugins/OmniPcmShared/`
- 音频解码相关：`NativePlugins/AudioDecoder/`、`NativePlugins/OmniAudioDecoder/`、`NativePlugins/FlacDecoder/`
- 媒体生成器：`ChillPatcher.MediaGenerator/`

### 游戏集成

- FH6 桥接 Mod：`mods/ForzaHorizon6OmniBridge/`
- FH6 桥接核心入口：`mods/ForzaHorizon6OmniBridge/src/bridge.cpp`
- FH6 Omni PCM 源：`mods/ForzaHorizon6OmniBridge/src/sources/omni_pcm_source.cpp`
- FH6 通用电台实验/参考实现：`NativePlugins/fh6-universal-radio/`
- Chill With You / BepInEx Mod：`mods/chillPatcher/`
- Better Endfield 注册桥：`OmniMixPlayer/gui_vbnet/OmniMixFrontend/Modules/OmniMix/OmniMixBetterEndfieldIntegrationService.vb`
- 游戏集成前端入口主要在 `PageOmniMixRight.xaml.vb` 的“游戏集成”区域，安装逻辑在 `OmniMixModDeploymentService.vb`。

### 构建与发布脚本

- 全量构建兼容入口：`scripts/build_all.py`
- 当前构建编排：`scripts/build_tree.py`
- GUI 构建：`scripts/build_gui.py`
- WebUI 构建：`OmniMixPlayer/gui_web/` 中执行 `npm install` / `npm run build`
- 安装器脚本：`scripts/build_installer.ps1`、`scripts/installer/`
- 任务拆分：`scripts/tasks/`

### 文档与设计记录

- 中文主文档：`README.md`
- 英文文档：`README_EN.md`
- 旧中文入口：`README_ZH.md`
- 维护规则：`AGENTS.md`
- 其他设计记录：`docs/`

## 兼容模型

- 桌面 GUI、游戏集成桥、游戏 Mod 都是 OmniMix 后端模型的客户端。
- 它们可以控制相似端点并观察相似状态，主要区别在于用户界面和音频输出角色。
- 保持模块 UI、播放状态、队列控制、游戏桥文件、端口文件、实例 ID 与当前 OmniMix 约定兼容。
- Better Endfield 仅通过其 JSON CLI 注册、查询和解除注册；不得向 Better Endfield 或终末地游戏目录写入端口文件、Mod 或配置。
- OmniPcmShared ABI 2 使用 `Global\OmniMixPlayer_PCM_<instance_id>`，普通用户权限不足时由后端和 SDK 透明回退到同后缀 `Local\` 映射；桥必须在加载时校验 ABI 主版本。
- FH6 集成需要识别不同发行版目录结构：
  - Steam：`fh6/forzahorizon6.exe` 与 `fh6/media`
  - Xbox：`fh6/Content/forzahorizon6.exe` 与 `fh6/media`
- 对 FH6 根桥接集成，`version.dll`、`OmniPcmShared.dll`、`.omnimix_instance_id` 和 `omnimix_port.txt` 应落到实际运行目录；自定义媒体文件应落到真实 `media` 目录。

## 构建说明

- 搜索文件或文本时优先使用 `rg` / `rg --files`。
- 代码改动默认保持在本仓库维护范围内；除非任务明确要求，不要做无关后端重构。
- 完整包构建仍可使用兼容包装脚本：

```powershell
python scripts/build_all.py player
```

- TypeScript WebUI 会构建并刷新后端 `wwwroot/` Web 资源。
- 发布前检查当前 Release 资产、构建产物和必要来源说明。

## 终端

- 终端操作使用 PowerShell 7（`pwsh`）。

## 文档维护

- `README.md` 是中文主文档。
- `README_EN.md` 是英文版本。
- `README_ZH.md` 仅作为中文入口/兼容文件，内容应指向 `README.md` 或保持极简同步。
- 项目 About / 描述应使用双语，中文优先，英文随后。
- 如果发布打包、项目定位或分支策略变化，需同步更新 `AGENTS.md`、`README.md` 和 `README_EN.md`。
