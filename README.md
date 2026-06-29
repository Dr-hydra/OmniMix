# OmniMix

OmniMix 是一个面向 Windows 的桌面音乐与游戏集成工具，由 Dr.Hydra 长期维护。

English version: [README_EN.md](README_EN.md)

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
- 提供个性化界面设置，包括背景、不透明度、主题色和 HSL 自定义。

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

### 游戏集成

进入“插件 - 游戏集成”页面后：

1. 选择支持的游戏。
2. 点击“选择游戏目录”。
3. 安装对应游戏集成桥。
4. 启动游戏前，建议先启动 OmniMix 前端，让前端刷新端口文件和实例绑定。

### FH6游戏内设置

将DJ调整为关，将主播模式调整为开并重启游戏。

### 自定义电台 UI

FH6 集成页面提供“替换电台 UI”功能：

1. 选择有效 FH6 目录。
2. 点击“替换电台 UI”。
3. 选择自定义 PNG并输入自定义电台名称。
4. 前端会调用媒体生成器生成文件、备份原始文件并写入游戏 `media` 目录。

如需恢复，点击“还原原始电台 UI”。

### 卸载

1. 先在游戏集成页面卸载游戏集成。
2. 如果你使用的是便携包，直接删除即可，使用安装器安装的在开始菜单->设置->应用中进行卸载。

## 构建

安装项目所需 .NET SDK 后运行：

```powershell
dotnet build "OmniMixPlayer/gui_vbnet/OmniMixFrontend.sln" -c Debug -v minimal
```

构建完整包：

```powershell
python scripts/build_all.py player --skip-flutter
```

`--skip-flutter` 只跳过 Flutter 桌面 GUI 复制步骤，不会移除后端 Web 资源。

## 分支说明

- 当前 VB.NET/WPF 分支是主维护分支。
- `main` 分支保留用于历史基线和未来必要的拉取/比对。

## 关于原项目

OmniMix 的部分历史实现来自 BeyondtheApex 的 ChillPatcher 项目。感谢原作者的早期工作。本仓库现在作为 Dr.Hydra 长期维护的分支版本继续演进。

原项目仓库：

```text
https://github.com/BeyondtheApex/ChillPatcher
```

## 开源协议

本项目按 GNU General Public License v3.0 开源，详见 [LICENSE](LICENSE)。

第三方组件保留其各自目录中的原始协议。
