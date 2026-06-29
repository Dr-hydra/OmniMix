# OmniMix

OmniMix 是一个面向 Windows 的桌面音乐与游戏集成工具，由 Dr.Hydra 长期维护。项目包含 OmniMix 后端、模块系统、原生音频组件、游戏集成桥，以及以 VB.NET/WPF 编写的桌面前端。

当前仓库以 VB.NET/WPF 前端分支作为主维护分支，并保留 `main` 分支用于历史基线和未来必要的拉取/比对。原 ChillPatcher 项目是本项目的历史来源之一，相关信息仅在致谢和兼容说明中保留。

English version: [README_EN.md](README_EN.md)

## 项目状态

当前版本：`3.3.1`

主要本地产物：

```text
OmniMixPlayer.Gui.Vbnet.exe
```

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
- 提供个性化界面设置，包括背景、不透明度、主题色和 HSL 自定义。

VB.NET 界面层与 [QING.UIKIT](https://github.com/Dr-hydra/QING.UIKIT) 同步维护；QING.UIKIT 是从本前端工作中整理出的可复用 WPF UI Kit。

## 使用说明

### 使用发布包

推荐普通用户下载完整发布包或安装器，而不是单独下载 exe。

便携包使用方式：

1. 解压完整包到一个可写目录。
2. 运行 `OmniMixPlayer.Gui.Vbnet.exe`。
3. 如提示选择后端路径，选择同目录下的 `OmniMixPlayer.Backend.exe`。
4. 在设置页确认后端状态、模块状态和音乐库路径。

安装器使用方式：

1. 运行 `OmniMixPlayer_V{version}_VBNet_installer.exe`。
2. 按向导完成安装。
3. 从开始菜单或安装目录启动 OmniMix。

### 游戏集成

进入“插件 - 游戏集成”页面后：

1. 选择支持的游戏。
2. 点击“选择游戏目录”。
3. 安装对应游戏集成桥。
4. 启动游戏前，建议先启动 OmniMix 前端，让前端刷新端口文件和实例绑定。

FH6 目录识别规则：

- Steam 版：`fh6/forzahorizon6.exe` 与 `fh6/media`
- Xbox 版：`fh6/Content/forzahorizon6.exe` 与 `fh6/media`

FH6 的 `version.dll`、`OmniPcmShared.dll`、`.omnimix_instance_id` 和 `omnimix_port.txt` 会落到实际运行目录。Steam 版为游戏根目录，Xbox 版为 `Content` 目录。自定义电台 UI 与媒体生成文件会写入真实的 `media` 目录。

### 自定义电台 UI

FH6 集成页面提供“替换电台 UI”功能：

1. 选择有效 FH6 目录。
2. 点击“替换电台 UI”。
3. 选择自定义 PNG。
4. 前端会调用媒体生成器生成文件、备份原始文件并写入游戏 `media` 目录。

如需恢复，点击“还原原始电台 UI”。

## 构建

安装项目所需 .NET SDK 后运行：

```powershell
dotnet build "OmniMixPlayer/gui_vbnet/OmniMixFrontend.sln" -c Debug -v minimal
```

本地发布单文件 exe：

```powershell
dotnet publish "OmniMixPlayer/gui_vbnet/OmniMixFrontend/OmniMixFrontend.vbproj" `
  -c Debug `
  -o "OmniMixPlayer/bin/GuiVbnetSingle" `
  /p:PublishSingleFile=true `
  /p:SelfContained=true `
  /p:RuntimeIdentifier=win-x64 `
  /p:EnableCompressionInSingleFile=true `
  /p:PublishReadyToRun=false `
  -v minimal
```

输出文件：

```text
OmniMixPlayer/bin/GuiVbnetSingle/OmniMixPlayer.Gui.Vbnet.exe
```

构建完整包：

```powershell
python scripts/build_all.py player --skip-flutter
```

`--skip-flutter` 只跳过 Flutter 桌面 GUI 复制步骤，不会移除后端 Web 资源。

## 分支说明

- 当前 VB.NET/WPF 分支是主维护分支。
- `main` 分支保留用于历史基线和未来必要的拉取/比对。
- 如需参考原项目更新，应使用临时同步分支，仅迁移确实需要的后端、SDK、模块或构建脚本变化。

## 关于原项目

OmniMix 的部分历史实现来自 BeyondtheApex 的 ChillPatcher 项目。感谢原作者的早期工作。本仓库现在作为 Dr.Hydra 长期维护的分支版本继续演进。

原项目仓库：

```text
https://github.com/BeyondtheApex/ChillPatcher
```

## 开源协议

本项目按 GNU General Public License v3.0 开源，详见 [LICENSE](LICENSE)。

第三方组件保留其各自目录中的原始协议。
