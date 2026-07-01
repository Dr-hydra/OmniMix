# -*- coding: utf-8 -*-
"""Package OmniMix VB.NET release artifacts from an existing playerbuild."""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
RELEASE = ROOT / "release"
ARTIFACTS = ROOT / "artifacts"
PLAYER_BUILD = ROOT / "playerbuild"

BACKEND_PROJ = ROOT / "OmniMixPlayer" / "OmniMixPlayer.Backend" / "OmniMixPlayer.Backend.csproj"
GUI_PROJ = ROOT / "OmniMixPlayer" / "gui_vbnet" / "OmniMixFrontend" / "OmniMixFrontend.vbproj"
MEDIA_GEN_PROJ = ROOT / "ChillPatcher.MediaGenerator" / "ChillPatcher.MediaGenerator.csproj"

GUI_EXE = "OmniMixPlayer.Gui.Vbnet.exe"
BACKEND_EXE = "OmniMixPlayer.Backend.exe"
MEDIA_GEN_EXE = "chill-gen-media.exe"


def main() -> int:
    configure_console()
    parser = argparse.ArgumentParser(description="Package OmniMix release zips.")
    parser.add_argument("version", help="Release version, for example 4.1.1.")
    args = parser.parse_args()

    if not PLAYER_BUILD.exists():
        print(f"Missing playerbuild: {PLAYER_BUILD}")
        return 1

    RELEASE.mkdir(exist_ok=True)
    ARTIFACTS.mkdir(exist_ok=True)

    portable_stage = RELEASE / f"stage_portable_{args.version}"
    framework_stage = RELEASE / f"stage_framework_dependent_{args.version}"
    portable_zip = RELEASE / f"OmniMixPlayer_V{args.version}_VBNet_portable.zip"
    framework_zip = RELEASE / f"OmniMixPlayer_V{args.version}_VBNet_full-framework-dependent.zip"

    self_contained_gui = ARTIFACTS / f"gui-vbnet-selfcontained-{args.version}"
    fd_root = ARTIFACTS / f"framework-dependent-singlefile-{args.version}"

    print("[publish] self-contained VB.NET frontend")
    reset_dir(self_contained_gui)
    run([
        "dotnet", "publish", str(GUI_PROJ),
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-o", str(self_contained_gui),
        "-p:PublishSingleFile=true",
        "-p:SelfContained=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:IncludeAllContentForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:PublishTrimmed=false",
        "-v", "minimal",
    ])

    print("[stage] portable")
    reset_dir(portable_stage)
    copy_playerbuild(portable_stage)
    shutil.copy2(self_contained_gui / GUI_EXE, portable_stage / GUI_EXE)
    archive_stage(portable_stage, portable_zip)

    print("[publish] framework-dependent single-file executables")
    reset_dir(fd_root)
    publish_framework_single_file(BACKEND_PROJ, fd_root / "backend")
    publish_framework_single_file(MEDIA_GEN_PROJ, fd_root / "media-generator")
    publish_framework_single_file(GUI_PROJ, fd_root / "gui")

    print("[stage] framework-dependent")
    reset_dir(framework_stage)
    copy_playerbuild(framework_stage)
    replace_from_publish(fd_root / "backend", framework_stage)
    replace_from_publish(fd_root / "media-generator", framework_stage)
    replace_from_publish(fd_root / "gui", framework_stage)
    cleanup_framework_stage(framework_stage)
    archive_stage(framework_stage, framework_zip)

    print(f"[ok] {portable_zip}")
    print(f"[ok] {framework_zip}")
    return 0


def configure_console() -> None:
    for name in ("stdout", "stderr"):
        stream = getattr(sys, name)
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")


def run(cmd: list[str]) -> None:
    print("  > " + " ".join(str(part) for part in cmd))
    completed = subprocess.run(cmd, cwd=ROOT, shell=True)
    if completed.returncode != 0:
        raise SystemExit(completed.returncode)


def reset_dir(path: Path) -> None:
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True, exist_ok=True)


def copy_playerbuild(dst: Path) -> None:
    skip_names = {"logs", "omni_backend.log", "omnimix_port.txt", ".omnimix_instance_id"}

    def ignore(_dir: str, names: list[str]) -> set[str]:
        return {name for name in names if name in skip_names}

    shutil.copytree(PLAYER_BUILD, dst, dirs_exist_ok=True, ignore=ignore)


def archive_stage(stage: Path, zip_path: Path) -> None:
    if zip_path.exists():
        zip_path.unlink()
    shutil.make_archive(str(zip_path.with_suffix("")), "zip", stage)


def publish_framework_single_file(project: Path, output: Path) -> None:
    run([
        "dotnet", "publish", str(project),
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "false",
        "-o", str(output),
        "-p:PublishSingleFile=true",
        "-p:SelfContained=false",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:IncludeAllContentForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=false",
        "-p:PublishTrimmed=false",
        "-v", "minimal",
    ])


def replace_from_publish(src: Path, dst: Path) -> None:
    for item in src.iterdir():
        if item.is_dir():
            target = dst / item.name
            if target.exists():
                shutil.rmtree(target)
            shutil.copytree(item, target)
            continue
        if item.suffix.lower() == ".pdb":
            continue
        shutil.copy2(item, dst / item.name)


def cleanup_framework_stage(stage: Path) -> None:
    satellite_dirs = {
        "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru",
        "tr", "zh-Hans", "zh-Hant",
    }
    for name in satellite_dirs:
        shutil.rmtree(stage / name, ignore_errors=True)
    shutil.rmtree(stage / "runtimes", ignore_errors=True)

    keep_dll = {"OmniPcmShared.dll"}
    for pattern in ("*.dll", "*.dll.config", "*.deps.json", "*.pdb", "*.xml"):
        for path in stage.glob(pattern):
            if path.name in keep_dll:
                continue
            path.unlink(missing_ok=True)


if __name__ == "__main__":
    raise SystemExit(main())
