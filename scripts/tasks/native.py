# -*- coding: utf-8 -*-
"""
原生插件构建任务
"""
import hashlib
import math
import shutil
import struct
import wave
import zipfile
from pathlib import Path

from build_config import (
    NATIVE_PLUGINS_DIR, NATIVE_PROJECTS_ALWAYS, NATIVE_PROJECTS_FULL_ONLY,
    OMNI_PCM_DLL, OMNI_PCM_SDK_ASSET, OMNI_PCM_SDK_VERSION, OMNI_PCM_SDK_ZIP,
    ROOT,
)
from .base import TaskNode, TaskStatus
from .common import clean_cmake_cache, copy_file, info, run_cmd


def create_native_tasks(parent: TaskNode, projects: list[str]) -> list[TaskNode]:
    """在 parent 下为每个原生插件创建子任务节点。"""
    leaves = []
    for proj in projects:
        leaf = parent.create_leaf(
            f"Native: {proj}",
            f"构建原生插件 {proj}",
            run_fn=_make_native_build_fn(proj),
        )
        leaves.append(leaf)
    return leaves


def create_native_always_group(parent: TaskNode) -> TaskNode:
    """创建「必须构建」的原生插件组。"""
    g = parent.create_group("Native Plugins (always)", "始终构建的原生插件")
    create_native_tasks(g, NATIVE_PROJECTS_ALWAYS)
    return g


def create_native_full_group(parent: TaskNode) -> TaskNode:
    """创建「完整构建才包含」的原生插件组。"""
    g = parent.create_group("Native Plugins (full only)", "仅 --full 时构建的原生插件")
    create_native_tasks(g, NATIVE_PROJECTS_FULL_ONLY)
    return g


def create_stage_omni_pcm(parent: TaskNode) -> TaskNode:
    """创建 OmniPcmShared.dll 统一存放任务。"""
    return parent.create_leaf(
        "Stage OmniPcmShared.dll",
        "复制 OmniPcmShared.dll 到 bin/native/x64/",
        run_fn=_stage_omni_pcm_dll,
    )


def create_package_omni_pcm_sdk(parent: TaskNode) -> TaskNode:
    """创建版本化 OmniPcmShared 原生 SDK 包。"""
    return parent.create_leaf(
        f"Package OmniPcmShared SDK {OMNI_PCM_SDK_VERSION}",
        "DLL + headers + VERSION + SHA256SUMS + README + 48 kHz stereo test WAV",
        run_fn=package_omni_pcm_sdk,
    )


# ── 内部函数 ──

def _make_native_build_fn(proj: str):
    def _build():
        src = NATIVE_PLUGINS_DIR / proj
        build_script = src / "build.bat"
        if not build_script.exists():
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


def _stage_omni_pcm_dll() -> int:
    src = NATIVE_PLUGINS_DIR / "OmniPcmShared" / "build" / "x64" / "bin" / "Release" / "OmniPcmShared.dll"
    dst = OMNI_PCM_DLL
    if src.exists():
        dst.parent.mkdir(parents=True, exist_ok=True)
        copy_file(src, dst.parent)
        info("  OmniPcmShared.dll staged to bin/native/x64/")
        return 0
    else:
        info(f"  WARNING: OmniPcmShared.dll not found at {src}")
        return 1


def package_omni_pcm_sdk() -> int:
    source_dir = ROOT / "NativePlugins" / "OmniPcmShared"
    dll = source_dir / "build" / "x64" / "bin" / "Release" / "OmniPcmShared.dll"
    if not dll.exists():
        dll = OMNI_PCM_DLL

    required = {
        "OmniPcmShared.dll": dll,
        "include/OmniPcmShared.h": source_dir / "include" / "OmniPcmShared.h",
        "include/omni_pcm_shared.h": source_dir / "include" / "omni_pcm_shared.h",
        "VERSION": source_dir / "VERSION",
        "README.md": source_dir / "SDK_README.md",
    }
    missing = [str(path) for path in required.values() if not path.exists()]
    if missing:
        info("  ERROR: missing OmniPcmShared SDK inputs: " + ", ".join(missing))
        return 1

    version = (source_dir / "VERSION").read_text(encoding="utf-8").strip()
    if version != OMNI_PCM_SDK_VERSION:
        info(f"  ERROR: VERSION is {version}, expected {OMNI_PCM_SDK_VERSION}")
        return 1

    stage = ROOT / "release" / f"OmniPcmSharedSDK-{version}"
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir(parents=True)

    for relative, source in required.items():
        destination = stage / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)

    _write_test_wav(stage / "test_stream_48k_stereo.wav")
    checksum_entries = []
    for file_path in sorted(path for path in stage.rglob("*") if path.is_file()):
        relative = file_path.relative_to(stage).as_posix()
        digest = hashlib.sha256(file_path.read_bytes()).hexdigest()
        checksum_entries.append(f"{digest}  {relative}")
    (stage / "SHA256SUMS").write_text("\n".join(checksum_entries) + "\n", encoding="ascii")

    OMNI_PCM_SDK_ZIP.parent.mkdir(parents=True, exist_ok=True)
    if OMNI_PCM_SDK_ZIP.exists():
        OMNI_PCM_SDK_ZIP.unlink()
    with zipfile.ZipFile(OMNI_PCM_SDK_ZIP, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as package:
        for file_path in sorted(path for path in stage.rglob("*") if path.is_file()):
            package.write(file_path, file_path.relative_to(stage).as_posix())

    OMNI_PCM_SDK_ASSET.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(OMNI_PCM_SDK_ZIP, OMNI_PCM_SDK_ASSET)
    dll_sha256 = hashlib.sha256(dll.read_bytes()).hexdigest()
    info(f"  OmniPcmShared SDK {version} -> {OMNI_PCM_SDK_ZIP}")
    info(f"  OmniPcmShared.dll SHA256: {dll_sha256}")
    return 0


def _write_test_wav(destination: Path) -> None:
    sample_rate = 48_000
    duration_seconds = 2
    amplitude = 0.2
    with wave.open(str(destination), "wb") as output:
        output.setnchannels(2)
        output.setsampwidth(2)
        output.setframerate(sample_rate)
        frames = bytearray()
        for frame in range(sample_rate * duration_seconds):
            left = int(32767 * amplitude * math.sin(2 * math.pi * 440 * frame / sample_rate))
            right = int(32767 * amplitude * math.sin(2 * math.pi * 660 * frame / sample_rate))
            frames.extend(struct.pack("<hh", left, right))
        output.writeframes(frames)
