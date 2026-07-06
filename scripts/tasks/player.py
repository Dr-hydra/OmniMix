# -*- coding: utf-8 -*-
"""
OmniMixPlayer 构建任务树
"""
import shutil

from build_config import (
    PLAYER_DIR, PLAYER_BUILD, PLAYER_SDK_PROJ, PLAYER_BACKEND_PROJ,
    PLAYER_BACKEND_PUBLISH, PLAYER_MODULES_BUILD, PLAYER_MODULE_MAP,
    PLAYER_ASSETS_DIR,
    PLAYER_WEB_DIR, PLAYER_WEB_BUILD, PLAYER_WWWROOT, MEDIA_GEN_PROJ, MEDIA_GEN_PUBLISH,
    OMNI_PCM_DLL, ROOT, FH6_DIR, FH6_BIN, FH6_STAGE, FH6_ZIP,
    FH6_PLAYER_ASSET, MOD_DIR, MOD_PLAYER_ASSET, NATIVE_PLUGINS_DIR,
)
from .base import TaskNode, TaskStatus
from .common import (
    _rmtree_ignore_locked, copy_file, copy_dir_contents,
    dotnet_build, dotnet_restore, dotnet_publish,
    info, run_cmd, read_version_info, write_version_json, package_zip,
)


def create_player_tasks(full: bool = False) -> TaskNode:
    """创建 OmniMixPlayer 完整构建任务树。"""
    root = TaskNode("OmniMixPlayer", "播放器 - 输出到 playerbuild/")

    # ── Clean ──
    root.create_leaf("Clean", "清理 playerbuild/", run_fn=_clean)

    # ── Native Plugins ──
    native_g = root.create_group("Native Plugins", "原生 C++ 插件编译")
    from .native import create_native_tasks, create_stage_omni_pcm
    create_native_tasks(native_g, [
        "OmniAudioDecoder", "OmniPcmShared", "SpotifyLibrespotBridge",
        "EsbuildBridge", "SmtcBridge",
    ])
    create_stage_omni_pcm(root)

    # ── WebUI ──
    fw = root.create_group("WebUI", "构建 TypeScript WebUI → wwwroot/")
    fw.create_leaf("npm install", "WebUI 依赖恢复", run_fn=_install_web_ui_dependencies)
    fw.create_leaf("npm run build", "编译并复制到 wwwroot/", run_fn=_build_web_ui)

    # ── C# SDK ──
    if full:
        root.create_leaf("Restore SDK NuGet", "",
                         run_fn=lambda: dotnet_restore(PLAYER_SDK_PROJ))
    root.create_leaf("OmniMixPlayer.SDK", "构建 SDK",
                     run_fn=lambda: dotnet_build(PLAYER_SDK_PROJ))

    # ── Backend ──
    if full:
        root.create_leaf("Restore Backend NuGet", "",
                         run_fn=lambda: run_cmd(
                             ["dotnet", "restore", str(PLAYER_BACKEND_PROJ),
                              "--runtime", "win-x64"]))
    root.create_leaf("Backend Publish", "发布 Backend (Single-File)",
                     run_fn=_publish_backend)

    # ── Modules ──
    mod_g = root.create_group("Modules", "播放器模块")
    for src_name, module_id in PLAYER_MODULE_MAP:
        mod_g.create_leaf(
            f"Module: {src_name}",
            f"构建 {src_name} → modules/{module_id}/",
            run_fn=_make_module_build_fn(src_name, module_id, full),
        )

    # ── MediaGenerator ──
    root.create_leaf("MediaGenerator", "发布媒体生成器 (Single-File)",
                     run_fn=_publish_media_gen)

    # ── Assemble ──
    root.create_leaf("Assemble", "组装 playerbuild/", run_fn=_assemble)

    # ── FH6 Bridge Asset ──
    fh6_g = root.create_group("FH6 Bridge Asset", "FH6 桥接资源打包")
    fh6_g.create_leaf("Build OmniPcmShared", "",
                      run_fn=lambda: run_cmd(["build.bat"],
                                             cwd=NATIVE_PLUGINS_DIR / "OmniPcmShared"))
    fh6_g.create_leaf("Build FH6 Bridge", "",
                      run_fn=lambda: run_cmd(["build.bat"], cwd=FH6_DIR))
    fh6_g.create_leaf("Package FH6 ZIP", "",
                      run_fn=_package_fh6_asset)

    # ── Version Info ──
    root.create_leaf("Version Info", "写入 version_info.json",
                     run_fn=_write_version)

    return root


# ── 内部执行函数 ──

def _clean() -> bool:
    _rmtree_ignore_locked(PLAYER_BUILD)
    (PLAYER_BUILD / "modules").mkdir(parents=True, exist_ok=True)
    (PLAYER_BUILD / "native" / "x64").mkdir(parents=True, exist_ok=True)
    info("playerbuild cleaned")
    return True


def _install_web_ui_dependencies() -> int:
    if not PLAYER_WEB_DIR.exists():
        info("  WARNING: gui_web not found")
        return 1
    if run_cmd(["where", "npm"]) != 0:
        info("npm not found!")
        return 1
    if (PLAYER_WEB_DIR / "package-lock.json").exists() and not (PLAYER_WEB_DIR / "node_modules").exists():
        return run_cmd(["npm", "ci"], cwd=PLAYER_WEB_DIR)
    return run_cmd(["npm", "install", "--no-audit"], cwd=PLAYER_WEB_DIR)


def _build_web_ui() -> int:
    code = run_cmd(["npm", "run", "build"], cwd=PLAYER_WEB_DIR)
    if code != 0:
        info("  WARNING: WebUI build failed")
        return code
    _rmtree_ignore_locked(PLAYER_WWWROOT)
    PLAYER_WWWROOT.mkdir(parents=True, exist_ok=True)
    copy_dir_contents(PLAYER_WEB_BUILD, PLAYER_WWWROOT)
    info("  WebUI copied to wwwroot/")
    return 0


def _publish_backend() -> int:
    if PLAYER_BACKEND_PUBLISH.exists():
        shutil.rmtree(PLAYER_BACKEND_PUBLISH)
    return dotnet_publish(PLAYER_BACKEND_PROJ, PLAYER_BACKEND_PUBLISH,
                          single_file=True)


def _make_module_build_fn(src_name: str, module_id: str, full: bool):
    def _build():
        proj = PLAYER_DIR / "modules" / src_name / f"ChillPatcher.Module.{src_name}.csproj"
        if not proj.exists():
            info(f"  SKIP: project file not found")
            return TaskStatus.SKIPPED
        if full:
            code = dotnet_restore(proj)
            if code != 0:
                return code
        return dotnet_build(proj)
    return _build


def _publish_media_gen() -> int:
    return dotnet_publish(MEDIA_GEN_PROJ, MEDIA_GEN_PUBLISH, single_file=True)


def _assemble() -> bool:
    # Backend
    info("Backend...")
    if PLAYER_BACKEND_PUBLISH.exists():
        copy_dir_contents(PLAYER_BACKEND_PUBLISH, PLAYER_BUILD)
        _rmtree_ignore_locked(PLAYER_BUILD / "modules")
    else:
        info("  WARNING: Backend publish output not found")

    # Native decoders
    native_src = ROOT / "bin" / "native" / "x64"
    if native_src.exists():
        native_dst = PLAYER_BUILD / "native" / "x64"
        native_dst.mkdir(parents=True, exist_ok=True)
        for dll in ["OmniAudioDecoder.dll", "ChillAudioDecoder.dll",
                     "ChillFlacDecoder.dll", "OmniPcmShared.dll"]:
            src = native_src / dll
            if src.exists():
                copy_file(src, native_dst)
        info("  Native decoders + OmniPcmShared copied")

    # MediaGenerator
    info("MediaGenerator...")
    if MEDIA_GEN_PUBLISH.exists():
        for f in MEDIA_GEN_PUBLISH.glob("chill-gen-media.exe"):
            copy_file(f, PLAYER_BUILD)
        for f in MEDIA_GEN_PUBLISH.glob("*.pdb"):
            copy_file(f, PLAYER_BUILD)
        for cfg in ["config.json"]:
            src = MEDIA_GEN_PUBLISH / cfg
            if src.exists():
                copy_file(src, PLAYER_BUILD)

    # Modules
    info("Modules...")
    for src_name, module_id in PLAYER_MODULE_MAP:
        src_dir = PLAYER_MODULES_BUILD / src_name
        dst_dir = PLAYER_BUILD / "modules" / module_id
        if not src_dir.exists():
            info(f"  WARNING: Module output not found: {src_name}")
            continue
        info(f"  {src_name} -> modules/{module_id}/")
        dst_dir.mkdir(parents=True, exist_ok=True)
        for ext in ("*.dll", "*.json", "*.png"):
            for f in src_dir.glob(ext):
                copy_file(f, dst_dir)
        # Native from build output
        for src_native in [src_dir / "native" / "x64"]:
            if src_native.exists():
                dst_native = dst_dir / "native" / "x64"
                dst_native.mkdir(parents=True, exist_ok=True)
                for f in src_native.iterdir():
                    if f.suffix in (".dll", ".exe"):
                        copy_file(f, dst_native)
        # Native from source tree
        src_module = PLAYER_DIR / "modules" / src_name
        for src_native in [src_module / "native" / "x64"]:
            if src_native.exists():
                dst_native = dst_dir / "native" / "x64"
                dst_native.mkdir(parents=True, exist_ok=True)
                for f in src_native.iterdir():
                    if f.suffix in (".dll", ".exe"):
                        copy_file(f, dst_native)

    # Cleanup
    _rmtree_ignore_locked(PLAYER_BUILD / "runtimes")
    for pattern in ["*.pdb", "*.xml", "*.deps.json"]:
        for f in PLAYER_BUILD.rglob(pattern):
            try:
                f.unlink()
            except Exception:
                pass
    info("  Unnecessary files cleaned")

    _copy_player_assets_to_build()

    info("Player files assembled")
    return True


def _copy_player_assets_to_build():
    """复制播放器安装/集成所需资产到 playerbuild。"""
    if not PLAYER_ASSETS_DIR.exists():
        info("  WARNING: Player assets directory not found")
        return
    dst = PLAYER_BUILD / "OmniMixAssets"
    dst.mkdir(parents=True, exist_ok=True)
    for name in ["BepInEx_win_x64_5.4.23.5.zip", "ChillPatcher.zip", "FH6OmniBridge.zip", "version_info.json"]:
        src = PLAYER_ASSETS_DIR / name
        if src.exists():
            copy_file(src, dst)
    info("  Player assets copied")


def _package_fh6_asset() -> bool:
    if FH6_STAGE.exists():
        shutil.rmtree(FH6_STAGE)
    FH6_STAGE.mkdir(parents=True, exist_ok=True)

    if FH6_BIN.exists():
        copy_file(FH6_BIN, FH6_STAGE)
    else:
        info("  WARNING: FH6 version.dll not found")
    if OMNI_PCM_DLL.exists():
        copy_file(OMNI_PCM_DLL, FH6_STAGE)

    return package_zip(FH6_STAGE, FH6_ZIP, FH6_PLAYER_ASSET)


def _write_version() -> bool:
    fh6_file = FH6_DIR / "src" / "bridge.cpp"
    data = read_version_info(MOD_DIR, PLAYER_WEB_DIR, PLAYER_DIR / "modules", fh6_file)

    # 写入 playerbuild/
    dst1 = PLAYER_BUILD / "version_info.json"
    # 写入播放器资产目录
    dst2 = PLAYER_ASSETS_DIR / "version_info.json"
    write_version_json(data, dst1, dst2)
    return True
