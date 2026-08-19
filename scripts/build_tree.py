# -*- coding: utf-8 -*-
"""
统一构建任务树
根据用户选择的模式和选项, 返回一棵 TaskNode 树。
正确的依赖关系:
  根: Backend / Desktop Frontend / MediaGenerator / Player Assets / Assemble
  Backend 的 wwwroot/ 内嵌 TypeScript WebUI
  Player Assets 下有两个 zip: ChillPatcher.zip, FH6OmniBridge.zip
"""
from __future__ import annotations
from pathlib import Path

from tasks.base import TaskNode, TaskStatus
from tasks.common import (
    _rmtree_ignore_locked, copy_file, copy_dir_contents, copy_dir_except,
    dotnet_build, dotnet_restore, dotnet_publish,
    info, run_cmd, clean_cmake_cache,
    read_version_info, write_version_json, package_zip,
    stage_verified_runtime_archive,
)
import build_config as C


# ════════════════════════════════════════════
#  构建树入口: 根据模式返回 TaskNode 根列表
# ════════════════════════════════════════════

def build_tree(mode: str, full: bool) -> list[TaskNode]:
    """返回构建树的根节点列表。"""
    _built_natives.clear()  # 每次重建树时重置去重
    roots = []

    if mode in ("all", "player"):
        roots.append(_backend(full))
        roots.append(_desktop_frontend())
        roots.append(_media_generator())
        roots.append(_player_assets(full))
        roots.append(_assemble_playerbuild())

    elif mode == "mod":
        # 仅构建 mod, 输出到 release/ChillPatcher/ + zip
        roots.append(_chillpatcher_asset(full))

    elif mode == "fh6-asset":
        roots.append(_fh6_asset(full))

    elif mode == "fh6":
        roots.append(_fh6_mod(full))

    return roots


# ════════════════════════════════════════════
#  根 1: Backend
# ════════════════════════════════════════════

def _backend(full: bool) -> TaskNode:
    g = TaskNode("🖥 Backend",
        "OmniMixPlayer.Backend.exe (ASP.NET Single-File)\n"
        "  wwwroot/ 内嵌 TypeScript WebUI")

    g.create_leaf("Clean Backend", "", run_fn=_clean_backend)

    if full:
        g.create_leaf("Restore SDK NuGet", "",
                      run_fn=lambda: dotnet_restore(C.PLAYER_SDK_PROJ))

    # WebUI → wwwroot
    fw = g.create_group("WebUI → wwwroot/", "TypeScript/Svelte 编译后放入 Backend wwwroot")
    fw.create_leaf("npm install", "恢复 WebUI 依赖",
                   run_fn=_install_web_ui_dependencies)
    fw.create_leaf("npm run build", "编译 WebUI",
                   run_fn=_build_web_ui_and_copy)

    # SDK
    g.create_leaf("Build SDK", "OmniMixPlayer.SDK",
                  run_fn=lambda: dotnet_build(C.PLAYER_SDK_PROJ))

    # Backend publish
    if full:
        g.create_leaf("Restore Backend (win-x64)", "",
                      run_fn=lambda: run_cmd(
                          ["dotnet", "restore", str(C.PLAYER_BACKEND_PROJ),
                           "--runtime", "win-x64"]))
    g.create_leaf("Publish Backend", "Single-File 发布",
                  run_fn=_publish_backend)

    # Native plugins (通用, 不归属特定模块)
    ng = g.create_group("Native Plugins", "后端需要的原生 DLL")
    for proj in ["OmniAudioDecoder"]:
        ng.create_leaf(f"  {proj}", "", run_fn=_make_native_fn(proj))

    # Modules (各自带 native bridge)
    mg = g.create_group("Modules", "播放器功能模块")

    _module_native = {
        "Netease": "netease_bridge",
        "QQMusic": "qqmusic_bridge",
        "Spotify": "SpotifyLibrespotBridge",
    }

    for src_name, _ in C.PLAYER_MODULE_MAP:
        m = mg.create_group(f"  {src_name}", f"模块: {src_name}")
        m.create_leaf(f"Build C# 模块", "",
                      run_fn=_make_module_fn(src_name, full))
        # 如果此模块有 native bridge, 加入
        if src_name in _module_native:
            m.create_leaf(f"Native: {_module_native[src_name]}", "",
                          run_fn=_make_native_fn(_module_native[src_name]))

    return g


# ════════════════════════════════════════════
#  根 2: Desktop Frontend
# ════════════════════════════════════════════

def _desktop_frontend() -> TaskNode:
    g = TaskNode("🪟 Desktop Frontend", "唯一桌面前端: VB.NET/WPF")
    g.create_leaf("Publish VB.NET frontend", "framework-dependent single-file",
                  run_fn=_publish_frontend)
    return g


# ════════════════════════════════════════════
#  根 3: MediaGenerator
# ════════════════════════════════════════════

def _media_generator() -> TaskNode:
    g = TaskNode("🛠 MediaGenerator CLI", "chill-gen-media.exe")
    g.create_leaf("Publish MediaGenerator", "Single-File 发布",
                  run_fn=lambda: dotnet_publish(C.MEDIA_GEN_PROJ, C.MEDIA_GEN_PUBLISH,
                                                single_file=True))
    return g


# ════════════════════════════════════════════
#  根 4: Player Assets
# ════════════════════════════════════════════

def _player_assets(full: bool) -> TaskNode:
    g = TaskNode("📦 Player Assets",
        "播放器安装/游戏集成资源\n"
        "  assets/: ChillPatcher.zip + FH6OmniBridge.zip + version_info.json")

    g.children.append(_chillpatcher_asset(full))
    g.children.append(_fh6_asset(full))
    from tasks.native import package_omni_pcm_sdk
    g.create_leaf(f"OmniPcmShared SDK {C.OMNI_PCM_SDK_VERSION}",
                  "打包版本化 ABI SDK 与 48 kHz 双声道测试流",
                  run_fn=package_omni_pcm_sdk)
    g.create_leaf("Version Info", "写入 version_info.json → assets/",
                  run_fn=_write_version_assets)
    return g


# ════════════════════════════════════════════
#  Asset: ChillPatcher.zip
# ════════════════════════════════════════════

def _chillpatcher_asset(full: bool) -> TaskNode:
    g = TaskNode("📦 Asset: ChillPatcher.zip", "BepInEx Mod → player assets/")

    g.create_leaf("Clean mod release", "", run_fn=_clean_mod_release)

    if full:
        g.create_leaf("Restore NuGet", "",
                      run_fn=lambda: _restore_mod_nuget())

    g.create_leaf("Build SDK", "ChillPatcher.SDK",
                  run_fn=lambda: dotnet_build(C.MOD_SDK_PROJ))
    g.create_leaf("Build Main Plugin", "ChillPatcher.dll",
                  run_fn=lambda: dotnet_build(C.MOD_MAIN_PROJ))
    g.create_leaf("Build OneJS", "ChillPatcher.OneJS",
                  run_fn=lambda: dotnet_build(C.MOD_ONEJS_PROJ))

    # UI
    ui_g = g.create_group("UI (esbuild)", "Preact 前端打包")
    for ui_dir in C.MOD_UI_DIRS:
        ui_g.create_leaf(f"  {ui_dir.name}", "",
                         run_fn=_make_ui_fn(ui_dir))

    # Native (full only)
    if full:
        ng = g.create_group("Native Plugins", "C++ 原生插件")
        for proj in ["OmniAudioDecoder", "OmniPcmShared",
                     "EsbuildBridge", "SmtcBridge"]:
            ng.create_leaf(f"  {proj}", "", run_fn=_make_native_fn(proj))
        g.create_leaf("Stage OmniPcmShared.dll", run_fn=_stage_omni_pcm)

    g.create_leaf("Assemble → release/ChillPatcher/",
        "组装 BepInEx 插件发布目录:\n"
        "  ├ ChillPatcher.dll + ChillPatcher.dll.config\n"
        "  ├ SDK/ChillPatcher.SDK.dll\n"
        "  ├ native/x64/OmniAudioDecoder.dll, OmniPcmShared.dll, ...\n"
        "  ├ native/x64/puerts.dll\n"
        "  ├ native/x64/VC++ runtime DLLs\n"
        "  ├ rime.dll + rime-data/ (输入法引擎)\n"
        "  └ ui/ (Preact 前端)",
        run_fn=_assemble_mod)
    g.create_leaf("Package ZIP → assets/",
        f"打包 release/ChillPatcher/ → {C.MOD_RELEASE_ZIP.name}\n"
        f"  复制到 {C.MOD_PLAYER_ASSET}",
        run_fn=_package_mod_zip)
    return g


# ════════════════════════════════════════════
#  Asset: FH6OmniBridge.zip
# ════════════════════════════════════════════

def _fh6_asset(full: bool) -> TaskNode:
    g = TaskNode("📦 Asset: FH6OmniBridge.zip", "FH6 桥接 → player assets/")

    if full:
        g.create_leaf("Build OmniPcmShared", "",
                      run_fn=_make_native_fn("OmniPcmShared"))
        g.create_leaf("Build FH6 Bridge", "",
                      run_fn=lambda: _build_fh6_bridge())
    else:
        g.create_leaf("(跳过编译, 使用已有产物)", "",
                      run_fn=lambda: TaskStatus.SUCCESS)

    g.create_leaf("Package ZIP → assets/",
        f"打包 → {C.FH6_ZIP.name}:\n"
        "  ├ version.dll (FH6 桥接)\n"
        "  ├ OmniPcmShared.dll\n"
        "  └ README.md\n"
        f"  复制到 {C.FH6_PLAYER_ASSET}",
        run_fn=_package_fh6_asset)
    return g


# ════════════════════════════════════════════
#  FH6 Mod (独立目标: release/FH6OmniBridge/)
# ════════════════════════════════════════════

def _fh6_mod(full: bool) -> TaskNode:
    g = TaskNode("📁 FH6 Mod", "→ release/FH6OmniBridge/")

    g.create_leaf("Clean FH6 release", "", run_fn=_clean_fh6_release)

    if full:
        g.create_leaf("Build OmniPcmShared", "", run_fn=_make_native_fn("OmniPcmShared"))
        g.create_leaf("Build FH6 Bridge", "", run_fn=_build_fh6_bridge)
    else:
        g.create_leaf("(跳过编译)", "", run_fn=lambda: TaskStatus.SUCCESS)

    g.create_leaf("Assemble → release/FH6OmniBridge/",
        "组装 FH6 原生 Mod:\n"
        "  ├ version.dll (FH6 桥接)\n"
        "  ├ OmniPcmShared.dll\n"
        "  └ README.md",
        run_fn=_assemble_fh6_mod)
    return g


# ════════════════════════════════════════════
#  Assemble playerbuild
# ════════════════════════════════════════════

def _assemble_playerbuild() -> TaskNode:
    g = TaskNode("📋 Assemble → playerbuild/", "合并所有产物到发布目录")

    # Backend (single-file exe + 运行时)
    g.create_leaf(
        "Backend publish → playerbuild/",
        "OmniMixPlayer.Backend.exe (单文件) + 运行时依赖 → playerbuild/ 根目录",
        run_fn=lambda: _copy_backend_to_build())

    # MediaGenerator
    g.create_leaf(
        "MediaGenerator → playerbuild/",
        "chill-gen-media.exe + config.json + *.pdb → playerbuild/ 根目录",
        run_fn=lambda: _copy_mediagen_to_build())

    # Desktop frontend
    g.create_leaf(
        "VB.NET frontend → playerbuild/",
        f"{C.PLAYER_FRONTEND_EXE} → playerbuild/ 根目录",
        run_fn=lambda: _copy_frontend_to_build())

    # Player assets
    g.create_leaf(
        "OmniMix assets → playerbuild/OmniMixAssets/",
        "OmniMixPlayer/assets/* → playerbuild/OmniMixAssets/",
        run_fn=lambda: _copy_player_assets_to_build())

    g.create_leaf(
        "vgmstream CLI → playerbuild/tools/vgmstream/",
        f"Pinned {C.VGMSTREAM_VERSION} Windows x64 decoder + DLLs + license/source notices",
        run_fn=lambda: _stage_vgmstream_runtime())

    # Native decoders (后端用)
    g.create_leaf(
        "Native runtime DLLs → playerbuild/",
        "OmniAudioDecoder.dll / OmniPcmShared.dll → playerbuild/native/x64/",
        run_fn=lambda: _copy_native_decoders())

    # 各模块 DLL
    for src_name, module_id in C.PLAYER_MODULE_MAP:
        g.create_leaf(
            f"Module {src_name} → playerbuild/modules/{module_id}/",
            f"*.dll + *.json + *.png → modules/{module_id}/\n"
            f"  native DLL → modules/{module_id}/native/x64/",
            run_fn=_make_copy_module_fn(src_name, module_id))

    # Cleanup
    g.create_leaf(
        "Cleanup", "删除 runtimes/ *.pdb *.xml *.deps.json",
        run_fn=lambda: _cleanup_build())

    return g


# ── 组装实现 ──

def _copy_backend_to_build() -> bool:
    if not C.PLAYER_BACKEND_PUBLISH.exists():
        info("  WARNING: Backend publish not found")
        return False
    copy_dir_contents(C.PLAYER_BACKEND_PUBLISH, C.PLAYER_BUILD)
    _rmtree_ignore_locked(C.PLAYER_BUILD / "modules")
    info("  Backend → playerbuild/")
    return True


def _copy_mediagen_to_build() -> bool:
    if not C.MEDIA_GEN_PUBLISH.exists():
        info("  WARNING: MediaGenerator publish not found")
        return False
    for f in C.MEDIA_GEN_PUBLISH.glob("chill-gen-media.exe"):
        copy_file(f, C.PLAYER_BUILD)
    for f in C.MEDIA_GEN_PUBLISH.glob("*.pdb"):
        copy_file(f, C.PLAYER_BUILD)
    for cfg in ["config.json"]:
        src = C.MEDIA_GEN_PUBLISH / cfg
        if src.exists():
            copy_file(src, C.PLAYER_BUILD)
    info("  MediaGenerator → playerbuild/")
    return True


def _copy_frontend_to_build() -> bool:
    src = C.PLAYER_FRONTEND_PUBLISH / C.PLAYER_FRONTEND_EXE
    if not src.exists():
        info(f"  WARNING: Frontend publish output not found: {src}")
        return False
    copy_file(src, C.PLAYER_BUILD)
    info(f"  {C.PLAYER_FRONTEND_EXE} → playerbuild/")
    return True


def _copy_player_assets_to_build() -> bool:
    if not C.PLAYER_ASSETS_DIR.exists():
        info("  WARNING: Player assets not found")
        return False
    dst = C.PLAYER_BUILD / "OmniMixAssets"
    dst.mkdir(parents=True, exist_ok=True)
    for item in C.PLAYER_ASSETS_DIR.iterdir():
        if item.is_file():
            copy_file(item, dst)
    info("  OmniMix assets → playerbuild/OmniMixAssets/")
    return True


def _stage_vgmstream_runtime() -> bool:
    staged = stage_verified_runtime_archive(
        C.VGMSTREAM_ARCHIVE,
        C.VGMSTREAM_RUNTIME_DIR,
        C.VGMSTREAM_ARCHIVE_SHA256,
        C.VGMSTREAM_RUNTIME_FILES,
        C.VGMSTREAM_NOTICE,
    )
    if staged:
        info(f"  vgmstream {C.VGMSTREAM_VERSION} → playerbuild/tools/vgmstream/")
    return staged


def _copy_native_decoders() -> bool:
    native_src = C.ROOT / "bin" / "native" / "x64"
    if not native_src.exists():
        info("  WARNING: bin/native/x64 not found")
        return False
    native_dst = C.PLAYER_BUILD / "native" / "x64"
    native_dst.mkdir(parents=True, exist_ok=True)
    copied = False
    for dll in ["OmniAudioDecoder.dll", "OmniPcmShared.dll"]:
        src = native_src / dll
        if src.exists():
            copy_file(src, native_dst)
            copied = True
            if dll == "OmniPcmShared.dll":
                copy_file(src, C.PLAYER_BUILD)
    if not copied:
        info("  WARNING: no native runtime DLLs copied")
        return False
    info("  Native runtime DLLs → playerbuild/")
    return True


def _make_copy_module_fn(src_name: str, module_id: str):
    def _fn():
        src_dir = C.PLAYER_MODULES_BUILD / src_name
        dst_dir = C.PLAYER_BUILD / "modules" / module_id
        if not src_dir.exists():
            info(f"  WARNING: Module {src_name} not built")
            return False
        dst_dir.mkdir(parents=True, exist_ok=True)
        for ext in ("*.dll", "*.json", "*.png"):
            for f in src_dir.glob(ext):
                copy_file(f, dst_dir)
        # Native from build output
        for src_n in [src_dir / "native" / "x64"]:
            if src_n.exists():
                dst_n = dst_dir / "native" / "x64"
                dst_n.mkdir(parents=True, exist_ok=True)
                for f in src_n.iterdir():
                    if f.suffix in (".dll", ".exe"):
                        copy_file(f, dst_n)
        # Native from source tree
        src_mod = C.PLAYER_DIR / "modules" / src_name
        for src_n in [src_mod / "native" / "x64"]:
            if src_n.exists():
                dst_n = dst_dir / "native" / "x64"
                dst_n.mkdir(parents=True, exist_ok=True)
                for f in src_n.iterdir():
                    if f.suffix in (".dll", ".exe"):
                        copy_file(f, dst_n)
        info(f"  {src_name} → modules/{module_id}/")
        return True
    return _fn


def _cleanup_build() -> bool:
    _rmtree_ignore_locked(C.PLAYER_BUILD / "runtimes")
    for pat in ("*.pdb", "*.xml", "*.deps.json"):
        for f in C.PLAYER_BUILD.rglob(pat):
            try:
                f.unlink()
            except Exception:
                pass
    info("  Cleaned unnecessary files")
    return True


# ════════════════════════════════════════════
#  内部实现函数
# ════════════════════════════════════════════

import shutil

def _clean_backend() -> bool:
    _rmtree_ignore_locked(C.PLAYER_BUILD)
    (C.PLAYER_BUILD / "modules").mkdir(parents=True, exist_ok=True)
    (C.PLAYER_BUILD / "native" / "x64").mkdir(parents=True, exist_ok=True)
    info("playerbuild cleaned")
    return True


def _install_web_ui_dependencies() -> int:
    if not C.PLAYER_WEB_DIR.exists():
        info("  WARNING: gui_web not found")
        return 1
    if run_cmd(["where", "npm"]) != 0:
        info("  WARNING: npm not found")
        return 1
    if (C.PLAYER_WEB_DIR / "package-lock.json").exists() and not (C.PLAYER_WEB_DIR / "node_modules").exists():
        return run_cmd(["npm", "ci"], cwd=C.PLAYER_WEB_DIR)
    return run_cmd(["npm", "install", "--no-audit"], cwd=C.PLAYER_WEB_DIR)


def _build_web_ui_and_copy() -> int:
    code = run_cmd(["npm", "run", "build"], cwd=C.PLAYER_WEB_DIR)
    if code != 0:
        info("  WARNING: WebUI build failed")
        return code
    _rmtree_ignore_locked(C.PLAYER_WWWROOT)
    C.PLAYER_WWWROOT.mkdir(parents=True, exist_ok=True)
    copy_dir_contents(C.PLAYER_WEB_BUILD, C.PLAYER_WWWROOT)
    info("  WebUI → wwwroot/")
    return 0


def _publish_backend() -> int:
    if C.PLAYER_BACKEND_PUBLISH.exists():
        shutil.rmtree(C.PLAYER_BACKEND_PUBLISH)
    return dotnet_publish(C.PLAYER_BACKEND_PROJ, C.PLAYER_BACKEND_PUBLISH,
                          single_file=True)


def _publish_frontend() -> int:
    if C.PLAYER_FRONTEND_PUBLISH.exists():
        shutil.rmtree(C.PLAYER_FRONTEND_PUBLISH)
    return run_cmd([
        "dotnet",
        "publish",
        str(C.PLAYER_FRONTEND_PROJ),
        "-c",
        "Release",
        "-r",
        "win-x64",
        "--self-contained",
        "false",
        "-o",
        str(C.PLAYER_FRONTEND_PUBLISH),
        "-p:PublishSingleFile=true",
        "-p:SelfContained=false",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:IncludeAllContentForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=false",
        "-p:PublishTrimmed=false",
        "-v",
        "minimal",
    ])


# ── Session 级去重: 同一个构建中, native 只编译一次 ──
_built_natives: set[str] = set()


def _make_native_fn(proj: str):
    def _build():
        if proj in _built_natives:
            info(f"SKIP {proj}: already built this session (dedup)")
            return TaskStatus.SKIPPED
        _built_natives.add(proj)

        src = C.NATIVE_PLUGINS_DIR / proj
        script = src / "build.bat"
        if not script.exists():
            info(f"SKIP {proj}: no build.bat")
            return TaskStatus.SKIPPED

        clean_cmake_cache(src)
        args = ["build.bat"]
        if proj in ("netease_bridge", "qqmusic_bridge"):
            args.append("--no-pause")
        code = run_cmd(args, cwd=src)
        if code != 0:
            info(f"  WARNING: {proj} build failed (exit={code})")
        return code
    return _build


def _stage_omni_pcm() -> int:
    src = C.NATIVE_PLUGINS_DIR / "OmniPcmShared" / "build" / "x64" / "bin" / "Release" / "OmniPcmShared.dll"
    if src.exists():
        C.OMNI_PCM_DLL.parent.mkdir(parents=True, exist_ok=True)
        copy_file(src, C.OMNI_PCM_DLL.parent)
        info("  OmniPcmShared.dll staged")
        return 0
    info(f"  WARNING: OmniPcmShared.dll not found")
    return 1


def _make_module_fn(src_name: str, full: bool):
    def _build():
        proj = C.PLAYER_DIR / "modules" / src_name / f"ChillPatcher.Module.{src_name}.csproj"
        if not proj.exists():
            info(f"  SKIP: project not found")
            return TaskStatus.SKIPPED
        if full:
            code = dotnet_restore(proj)
            if code != 0:
                return code
        return dotnet_build(proj)
    return _build


def _make_ui_fn(ui_dir: Path):
    def _build():
        name = ui_dir.name
        if not ui_dir.exists():
            info(f"SKIP {name}: not found")
            return TaskStatus.SKIPPED
        code = run_cmd(["where", "npm"])
        if code != 0:
            info("npm not found!")
            return TaskStatus.SKIPPED
        if not (ui_dir / "node_modules").exists():
            info(f"  npm install for {name}...")
            code = run_cmd(["npm", "install"], cwd=ui_dir)
            if code != 0:
                return code
        return run_cmd(["npm", "run", "build"], cwd=ui_dir)
    return _build


def _restore_mod_nuget() -> int:
    for proj in [C.MOD_SDK_PROJ, C.MOD_MAIN_PROJ, C.MOD_ONEJS_PROJ]:
        if proj.exists():
            code = dotnet_restore(proj)
            if code != 0:
                return code
    return 0


def _clean_mod_release() -> bool:
    if C.MOD_RELEASE.exists():
        shutil.rmtree(C.MOD_RELEASE)
    C.MOD_RELEASE.mkdir(parents=True, exist_ok=True)
    (C.MOD_RELEASE / "native" / "x64").mkdir(parents=True, exist_ok=True)
    (C.MOD_RELEASE / "SDK").mkdir(exist_ok=True)
    return True


def _assemble_mod() -> bool:
    mod_bin = C.MOD_DIR / "bin"
    # 主插件
    for f in mod_bin.glob("ChillPatcher.*"):
        if f.suffix in (".dll", ".config") and "SDK" not in f.stem:
            copy_file(f, C.MOD_RELEASE)
    # SDK
    sdk_bin = C.MOD_DIR / "bin" / "SDK"
    if sdk_bin.exists():
        for f in sdk_bin.glob("*.dll"):
            copy_file(f, C.MOD_RELEASE / "SDK")
    # 原生 DLL
    native_src = C.ROOT / "bin" / "native" / "x64"
    native_dst = C.MOD_RELEASE / "native" / "x64"
    exclude = {"ChillNetease.dll", "ChillQQMusic.dll",
               "ChillAudioDecoder.dll", "ChillFlacDecoder.dll"}
    if native_src.exists():
        native_dst.mkdir(parents=True, exist_ok=True)
        for f in native_src.glob("*.dll"):
            if f.name not in exclude:
                copy_file(f, native_dst)
    # VC++ runtime
    lib_dir = C.MOD_DIR / "lib"
    if lib_dir.exists():
        for f in lib_dir.glob("*.dll"):
            copy_file(f, native_dst)
    # puerts.dll
    puerts = C.MOD_DIR / "ChillPatcher.OneJS" / "native" / "x64" / "puerts.dll"
    if puerts.exists():
        copy_file(puerts, native_dst)
    # RIME
    _assemble_rime()

    # UI (复制全部源文件+构建产物, 排除 node_modules/)
    for ui_dir in C.MOD_UI_DIRS:
        dst_ui = C.MOD_RELEASE / "ui" / ui_dir.name
        dst_ui.mkdir(parents=True, exist_ok=True)
        copy_dir_except(ui_dir, dst_ui, skip={"node_modules"})
        info(f"  UI {ui_dir.name} → release/ChillPatcher/ui/{ui_dir.name}/")

    info("Mod files assembled")
    return True


def _assemble_rime():
    rime_dir = C.NATIVE_PLUGINS_DIR / "rime"
    rime_dll = rime_dir / "librime" / "build" / "bin" / "Release" / "rime.dll"
    if rime_dll.exists():
        copy_file(rime_dll, C.MOD_RELEASE)

    rd = C.MOD_RELEASE / "rime-data"
    rs = rd / "shared"
    ro = rs / "opencc"
    for d in [rs, ro, rd / "user"]:
        d.mkdir(parents=True, exist_ok=True)

    def _cp(src_dir, files):
        for fn in files:
            src = src_dir / fn
            if src.exists():
                copy_file(src, rs)

    _cp(rime_dir / "rime-schemas" / "prelude",
        ["symbols.yaml", "punctuation.yaml", "key_bindings.yaml"])
    _cp(rime_dir / "RimeDefaultConfig",
        ["default.yaml", "luna_pinyin.custom.yaml"])
    essay = rime_dir / "rime-schemas" / "essay" / "essay.txt"
    if essay.exists():
        copy_file(essay, rs)
    _cp(rime_dir / "rime-schemas" / "luna-pinyin",
        ["luna_pinyin.schema.yaml", "luna_pinyin.dict.yaml", "pinyin.yaml"])
    _cp(rime_dir / "rime-schemas" / "stroke",
        ["stroke.schema.yaml", "stroke.dict.yaml"])
    _cp(rime_dir / "rime-schemas" / "double-pinyin",
        ["double_pinyin.schema.yaml", "double_pinyin_abc.schema.yaml",
         "double_pinyin_flypy.schema.yaml", "double_pinyin_mspy.schema.yaml"])

    opencc_src = rime_dir / "librime" / "share" / "opencc"
    if opencc_src.exists():
        for f in opencc_src.glob("*.json"):
            copy_file(f, ro)
        for f in opencc_src.glob("*.ocd2"):
            copy_file(f, ro)
    info("  RIME data assembled")


def _package_mod_zip() -> bool:
    return package_zip(C.MOD_RELEASE, C.MOD_RELEASE_ZIP, C.MOD_PLAYER_ASSET)


def _build_fh6_bridge() -> int:
    clean_cmake_cache(C.FH6_DIR)
    return run_cmd(["build.bat"], cwd=C.FH6_DIR)


def _package_fh6_asset() -> bool:
    missing = [
        path for path in (C.FH6_BIN, C.OMNI_PCM_DLL)
        if not path.is_file() or path.stat().st_size <= 0
    ]
    if missing:
        info("  ERROR: FH6 bridge package requires: " + ", ".join(str(path) for path in missing))
        return False

    if C.FH6_STAGE.exists():
        shutil.rmtree(C.FH6_STAGE)
    C.FH6_STAGE.mkdir(parents=True, exist_ok=True)
    copy_file(C.FH6_BIN, C.FH6_STAGE)
    copy_file(C.OMNI_PCM_DLL, C.FH6_STAGE)
    readme = C.FH6_DIR / "README.md"
    if readme.is_file():
        copy_file(readme, C.FH6_STAGE)
    return package_zip(C.FH6_STAGE, C.FH6_ZIP, C.FH6_PLAYER_ASSET)


def _clean_fh6_release() -> bool:
    if C.FH6_STAGE.exists():
        shutil.rmtree(C.FH6_STAGE)
    C.FH6_STAGE.mkdir(parents=True)
    return True


def _assemble_fh6_mod() -> bool:
    if C.FH6_BIN.exists():
        copy_file(C.FH6_BIN, C.FH6_STAGE)
    else:
        info(f"  WARNING: Missing {C.FH6_BIN}")
    if C.OMNI_PCM_DLL.exists():
        copy_file(C.OMNI_PCM_DLL, C.FH6_STAGE)
    readme = C.FH6_DIR / "README.md"
    if readme.exists():
        copy_file(readme, C.FH6_STAGE)
    info("  FH6 mod assembled")
    return True


def _write_version_assets() -> bool:
    fh6_file = C.FH6_DIR / "src" / "bridge.cpp"
    data = read_version_info(C.MOD_DIR, C.PLAYER_WEB_DIR, C.PLAYER_DIR / "modules", fh6_file)
    # 写入 playerbuild/
    dst1 = C.PLAYER_BUILD / "version_info.json"
    # 写入播放器 assets/
    dst2 = C.PLAYER_ASSETS_DIR / "version_info.json"
    write_version_json(data, dst1, dst2)
    return True
