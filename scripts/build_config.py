# -*- coding: utf-8 -*-
"""
构建配置 - 所有路径和工具链设置
"""
import os
from pathlib import Path

# ── 根目录 ──
ROOT = Path(__file__).resolve().parent.parent

# ════════════════════════════════════════════
#  工具链配置 — 按需修改这里的路径
# ════════════════════════════════════════════

VS_INSTALL_DIR = Path("D:/Program Files/Microsoft Visual Studio/18/Community")
MINGW_DIR = Path("D:/mingw64")
CMAKE_DIR = Path("")
GO_DIR = Path("")
NODE_DIR = Path("")


def setup_toolchain():
    """自动将工具链目录加入 PATH 并设置环境变量。"""
    paths_to_add = []

    def _add(p: Path):
        s = str(p)
        if s not in os.environ.get("PATH", ""):
            paths_to_add.append(s)

    # Go
    for p in [GO_DIR / "bin", Path("C:/Program Files/Go/bin"),
              Path("D:/Program Files/Go/bin"), Path("C:/Go/bin")]:
        if p.joinpath("go.exe").exists():
            _add(p); break

    # Clang/LLVM
    llvm_bin = VS_INSTALL_DIR / "VC" / "Tools" / "Llvm" / "bin"
    if llvm_bin.joinpath("clang.exe").exists():
        _add(llvm_bin)
        os.environ["CC"] = "clang -fuse-ld=lld"

    # Mingw (fallback)
    if "CC" not in os.environ:
        mingw_bin = MINGW_DIR / "bin"
        if mingw_bin.joinpath("gcc.exe").exists():
            _add(mingw_bin)
            os.environ["CC"] = "gcc"

    # CMake
    for p in [CMAKE_DIR / "bin",
              VS_INSTALL_DIR / "Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin",
              Path("C:/Program Files/CMake/bin")]:
        if p.joinpath("cmake.exe").exists():
            _add(p); break

    # Node.js
    for p in [NODE_DIR, Path("C:/Program Files/nodejs"),
              Path("D:/Program Files/nodejs")]:
        if p.joinpath("node.exe").exists():
            _add(p); break

    if paths_to_add:
        os.environ["PATH"] = ";".join(paths_to_add) + ";" + os.environ.get("PATH", "")


# ════════════════════════════════════════════
#  目录路径常量
# ════════════════════════════════════════════

MOD_DIR = ROOT / "mods" / "chillPatcher" / "src"
MOD_RELEASE = ROOT / "release" / "ChillPatcher"
MOD_SDK_PROJ = MOD_DIR / "ChillPatcher.SDK" / "ChillPatcher.SDK.csproj"
MOD_MAIN_PROJ = MOD_DIR / "ChillPatcher.csproj"
MOD_ONEJS_PROJ = MOD_DIR / "ChillPatcher.OneJS" / "ChillPatcher.OneJS.csproj"
MOD_UI_DIRS = [MOD_DIR / "ui" / "default", MOD_DIR / "ui" / "window-manager"]

PLAYER_DIR = ROOT / "OmniMixPlayer"
PLAYER_BUILD = ROOT / "playerbuild"
PLAYER_SDK_PROJ = PLAYER_DIR / "OmniMixPlayer.SDK" / "OmniMixPlayer.SDK.csproj"
PLAYER_BACKEND_PROJ = PLAYER_DIR / "OmniMixPlayer.Backend" / "OmniMixPlayer.Backend.csproj"
PLAYER_FRONTEND_PROJ = PLAYER_DIR / "gui_vbnet" / "OmniMixFrontend" / "OmniMixFrontend.vbproj"
PLAYER_BACKEND_PUBLISH = PLAYER_DIR / "bin" / "BackendPublish"
PLAYER_FRONTEND_PUBLISH = PLAYER_DIR / "bin" / "GuiVbnetSingle"
PLAYER_MODULES_BUILD = PLAYER_DIR / "bin" / "Modules"
PLAYER_ASSETS_DIR = PLAYER_DIR / "assets"
VGMSTREAM_VERSION = "r2117"
VGMSTREAM_ASSET_DIR = PLAYER_ASSETS_DIR / "tools" / "vgmstream"
VGMSTREAM_ARCHIVE = VGMSTREAM_ASSET_DIR / f"vgmstream-win64-{VGMSTREAM_VERSION}.zip"
VGMSTREAM_NOTICE = VGMSTREAM_ASSET_DIR / "VGMSTREAM_UPSTREAM.txt"
VGMSTREAM_ARCHIVE_SHA256 = "6c4a8a3813864fefed081bbd337dbc0ad93bf88e0b92f5db98d7ab258b22dc6c"
VGMSTREAM_RUNTIME_DIR = PLAYER_BUILD / "tools" / "vgmstream"
VGMSTREAM_RUNTIME_FILES = (
    "vgmstream-cli.exe",
    "avcodec-vgmstream-59.dll",
    "avformat-vgmstream-59.dll",
    "avutil-vgmstream-57.dll",
    "swresample-vgmstream-4.dll",
    "libatrac9.dll",
    "libcelt-0061.dll",
    "libcelt-0110.dll",
    "libg719_decode.dll",
    "libmpg123-0.dll",
    "libspeex-1.dll",
    "libvorbis.dll",
    "COPYING",
    "README.md",
    "USAGE.md",
)
PLAYER_WEB_DIR = PLAYER_DIR / "gui_web"
PLAYER_WEB_BUILD = PLAYER_WEB_DIR / "dist"
PLAYER_WWWROOT = PLAYER_DIR / "OmniMixPlayer.Backend" / "wwwroot"
PLAYER_FRONTEND_EXE = "OmniMixPlayer.Gui.Vbnet.exe"
PLAYER_BACKEND_EXE = "OmniMixPlayer.Backend.exe"

MEDIA_GEN_PROJ = ROOT / "ChillPatcher.MediaGenerator" / "ChillPatcher.MediaGenerator.csproj"
MEDIA_GEN_PUBLISH = ROOT / "ChillPatcher.MediaGenerator" / "bin" / "publish"

NATIVE_PLUGINS_DIR = ROOT / "NativePlugins"
NATIVE_PROJECTS_ALWAYS = [
    "OmniAudioDecoder", "OmniPcmShared", "SpotifyLibrespotBridge",
    "EsbuildBridge", "SmtcBridge",
]
NATIVE_PROJECTS_FULL_ONLY = ["netease_bridge", "qqmusic_bridge"]

PLAYER_MODULE_MAP = [
    ("LocalFolder", "com.chillpatcher.localfolder"),
    ("Netease", "com.chillpatcher.netease"),
    ("Bilibili", "com.chillpatcher.bilibili"),
    ("QQMusic", "com.chillpatcher.qqmusic"),
    ("Kugou", "com.chillpatcher.kugou"),
    ("Kuwo", "com.chillpatcher.kuwo"),
    ("Spotify", "com.chillpatcher.spotify"),
    ("YouTubeMusic", "com.chillpatcher.youtubemusic"),
]

FH6_DIR = ROOT / "mods" / "ForzaHorizon6OmniBridge"
FH6_BIN = FH6_DIR / "bin" / "version.dll"
FH6_STAGE = ROOT / "release" / "FH6OmniBridge"
FH6_ZIP = ROOT / "release" / "FH6OmniBridge.zip"
FH6_PLAYER_ASSET = PLAYER_ASSETS_DIR / "FH6OmniBridge.zip"

MOD_RELEASE_ZIP = ROOT / "release" / "ChillPatcher.zip"
MOD_PLAYER_ASSET = PLAYER_ASSETS_DIR / "ChillPatcher.zip"

OMNI_PCM_DLL = ROOT / "bin" / "native" / "x64" / "OmniPcmShared.dll"
OMNI_PCM_SDK_VERSION = "2.0.0"
OMNI_PCM_SDK_ZIP = ROOT / "release" / f"OmniPcmSharedSDK-{OMNI_PCM_SDK_VERSION}.zip"
OMNI_PCM_SDK_ASSET = PLAYER_ASSETS_DIR / OMNI_PCM_SDK_ZIP.name
