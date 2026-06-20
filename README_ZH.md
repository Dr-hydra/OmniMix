# OmniMix VB.NET 兼容前端

这是一个面向现有 OmniMix 后端的 Windows 桌面前端。项目目标是用 VB.NET/WPF 提供接近 PCL 风格的桌面体验，同时保持对原作者后端 API、模块 UI、曲库、播放队列和游戏集成流程的兼容。

本仓库现在定位为“兼容前端”，不是后端分支。遇到兼容问题时，优先在前端做适配，不要求修改上游后端实现。

本项目的 VB.NET 界面层与 [QING.UIKIT](https://github.com/Dr-hydra/QING.UIKIT) 同步维护，后者是从本前端工作中整理出的可复用 WPF UI Kit。

## 当前状态

版本：`3.2.3`

3.2.3 发布包从当前源码树完整构建，包含更新后的后端、模块、原生资源和 VB.NET 桌面前端。

主要产物：

```text
OmniMixPlayer.Gui.Vbnet.exe
```

该 EXE 仍是主要的本地构建产物，并会被装入完整 OmniMix 发行包。从 3.0.7 开始，GitHub Release 不再单独发布框架依赖版和自包含版前端 EXE。

正式发布保留以下三个包：

- `OmniMixPlayer_V{version}_VBNet_portable.zip`：完整自包含便携包。
- `OmniMixPlayer_V{version}_VBNet_full-framework-dependent.zip`：完整框架依赖包。
- `OmniMixPlayer_V{version}_VBNet_installer.exe`：完整 Windows 安装器。

## 主要功能

- 启动或发现现有 OmniMix 后端。
- 在标题区域显示后端连接状态。
- 提供播放控制、队列/历史管理、封面加载、循环/随机模式、可拖动进度条。
- 读取后端曲库，并把曲库歌曲直接加入播放队列。
- 在 VB.NET 界面中承载后端模块 UI。
- 从现有资源包部署支持的游戏集成桥。
- 自动同步游戏集成实例 ID 和端口文件。
- 清理明显错绑的旧游戏集成实例。
- 提供后端路径、关闭前端时是否关闭后端、个性化、服务控制、均衡器等设置。
- 个性化面板已对齐 QING.UIKIT，支持不透明度、默认/自定义/纯色背景，以及 HSL 自定义主题控制。

## 兼容说明

正常使用时，VB.NET 前端不要求修改后端 API。前端会维护它能控制的兼容文件，例如：

- `.omnimix_instance_id`
- `omnimix_port.txt`
- 游戏集成桥 DLL

对于 FH6 这类根目录桥接集成，前端现在会把 `version.dll` 和 `OmniPcmShared.dll` 作为实体文件复制到游戏目录，不再使用符号链接，减少游戏侧 DLL 加载的不确定性。

启动顺序：

- 推荐先启动前端。前端会启动或发现后端，并在游戏启动前刷新端口文件和实例文件。
- 如果先启动游戏，桥可能会自己尝试发现或启动后端；这种情况下旧端口文件、旧后端进程或旧实例可能导致错绑。
- 当前前端在每次连接后端时都会自动修复已安装游戏集成的绑定文件，并清理明显无效的旧实例。

## 构建

安装项目所需 .NET SDK 后运行：

```powershell
dotnet build "OmniMixPlayer/OmniMixPlayer.sln" -c Debug -v minimal
```

本地发布单文件 EXE：

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

## 部署

本地开发或兼容性测试时，可以把生成的 EXE 放入已有 OmniMix 分发目录，例如：

```text
E:\FH6\ChillPatcher\OmniMixPlayer.Gui.Vbnet.exe
```

该目录仍需要保留原有后端 exe、模块、资源和配置文件。

## 开源协议

本项目按 GNU General Public License v3.0 开源，详见 `LICENSE`。

第三方组件保留其各自目录中的原始协议。
