# -*- coding: utf-8 -*-
"""Command-line compatibility wrapper for the v3 build task tree.

The upstream v3 refactor moved build logic into task-tree modules used by the
GUI. This file keeps the historical `build.cmd player --full` style entrypoint
working for release automation and for the VB.NET frontend package flow.
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from build_config import PLAYER_BUILD, ROOT, setup_toolchain
from build_tree import build_tree
from tasks.base import TaskNode, TaskStatus


VB_FRONTEND_PROJ = (
    ROOT / "OmniMixPlayer" / "gui_vbnet" / "OmniMixFrontend" / "OmniMixFrontend.vbproj"
)
VB_FRONTEND_PUBLISH = ROOT / "OmniMixPlayer" / "bin" / "GuiVbnetSingle"
VB_FRONTEND_EXE = "OmniMixPlayer.Gui.Vbnet.exe"


def main() -> int:
    _configure_console_encoding()
    parser = argparse.ArgumentParser(description="Build ChillPatcher/OmniMix targets.")
    parser.add_argument(
        "command",
        nargs="?",
        default="all",
        choices=["all", "player", "mod", "fh6-asset", "fh6"],
        help="Build target.",
    )
    parser.add_argument("--full", action="store_true", help="Run restore/native full build steps.")
    parser.add_argument("--skip-flutter", action="store_true", help="Skip Flutter web/desktop builds.")
    parser.add_argument("--dry-run", action="store_true", help="Print the task tree without running it.")
    args = parser.parse_args()

    setup_toolchain()
    roots = build_tree(args.command, args.full, args.skip_flutter)
    if args.skip_flutter:
        _disable_flutter_desktop_assembly(roots)
    if args.dry_run:
        for root in roots:
            _print_tree(root)
        return 0

    ok = True
    for root in roots:
        ok = _run_node(root) and ok
        if not ok:
            break

    if ok and args.command in ("all", "player"):
        ok = _publish_vbnet_frontend()

    return 0 if ok else 1


def _configure_console_encoding() -> None:
    for stream_name in ("stdout", "stderr"):
        stream = getattr(sys, stream_name)
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")


def _print_tree(node: TaskNode, depth: int = 0) -> None:
    print("  " * depth + f"- {node.name}")
    for child in node.children:
        _print_tree(child, depth + 1)


def _disable_flutter_desktop_assembly(nodes: list[TaskNode]) -> None:
    for node in nodes:
        path = node.full_path
        if "Flutter GUI" in path and "playerbuild" in path:
            node.enabled = False
        _disable_flutter_desktop_assembly(node.children)


def _run_node(node: TaskNode) -> bool:
    if not node.enabled:
        node.status = TaskStatus.DISABLED
        print(f"[disabled] {node.full_path}")
        return True

    if node.is_leaf:
        print(f"\n[run] {node.full_path}")
        status = node.run()
        print(f"[{status.value}] {node.full_path}")
        return status in (TaskStatus.SUCCESS, TaskStatus.DISABLED, TaskStatus.SKIPPED)

    print(f"\n[group] {node.full_path}")
    for child in node.children:
        if not _run_node(child):
            node.status = TaskStatus.FAILED
            return False
    node.status = TaskStatus.SUCCESS
    return True


def _publish_vbnet_frontend() -> bool:
    print("\n[run] VB.NET Frontend/Publish")
    if not VB_FRONTEND_PROJ.exists():
        print(f"Missing project: {VB_FRONTEND_PROJ}")
        return False

    code = subprocess.run(
        [
            "dotnet",
            "publish",
            str(VB_FRONTEND_PROJ),
            "-c",
            "Release",
            "-o",
            str(VB_FRONTEND_PUBLISH),
            "-v",
            "minimal",
        ],
        cwd=ROOT,
        shell=True,
        check=False,
    ).returncode
    if code != 0:
        print(f"VB.NET frontend publish failed: {code}")
        return False

    src = VB_FRONTEND_PUBLISH / VB_FRONTEND_EXE
    if not src.exists():
        print(f"Missing published executable: {src}")
        return False

    PLAYER_BUILD.mkdir(parents=True, exist_ok=True)
    _remove_upstream_desktop_gui()
    shutil.copy2(src, PLAYER_BUILD / VB_FRONTEND_EXE)
    print(f"[success] VB.NET frontend copied to {PLAYER_BUILD / VB_FRONTEND_EXE}")

    from build_config import OMNI_PCM_DLL
    if OMNI_PCM_DLL.exists():
        shutil.copy2(OMNI_PCM_DLL, PLAYER_BUILD / "OmniPcmShared.dll")
        native_dst = PLAYER_BUILD / "native" / "x64"
        native_dst.mkdir(parents=True, exist_ok=True)
        shutil.copy2(OMNI_PCM_DLL, native_dst / "OmniPcmShared.dll")
        print(f"[success] OmniPcmShared.dll copied to {PLAYER_BUILD} and native/x64/")
    else:
        print(f"[warning] OmniPcmShared.dll not found at {OMNI_PCM_DLL}")

    _copy_omnimix_assets()
    return True


def _copy_omnimix_assets() -> None:
    dst_dir = PLAYER_BUILD / "OmniMixAssets"
    dst_dir.mkdir(parents=True, exist_ok=True)

    flutter_assets_dir = ROOT / "OmniMixPlayer" / "gui_flutter" / "assets"
    release_assets_dir = ROOT / "release" / "OmniMixAssets"

    zip_files = ["BepInEx_win_x64_5.4.23.5.zip", "ChillPatcher.zip", "FH6OmniBridge.zip"]
    for name in zip_files:
        src = flutter_assets_dir / name
        if not src.exists():
            src = release_assets_dir / name

        if src.exists():
            shutil.copy2(src, dst_dir / name)
            print(f"[success] Asset {name} copied from {src.parent.name}")
        else:
            print(f"[warning] Asset {name} not found in build or release folders")


def _remove_upstream_desktop_gui() -> None:
    for name in [
        "omnimix_gui.exe",
        "omni_mix_player.exe",
        "flutter_windows.dll",
        "OmniPcmShared.dll",
    ]:
        path = PLAYER_BUILD / name
        if path.exists() and path.name != VB_FRONTEND_EXE:
            path.unlink()

    data_dir = PLAYER_BUILD / "data"
    if data_dir.exists():
        shutil.rmtree(data_dir, ignore_errors=True)

    for dll in PLAYER_BUILD.glob("*_plugin.dll"):
        dll.unlink()


if __name__ == "__main__":
    raise SystemExit(main())
