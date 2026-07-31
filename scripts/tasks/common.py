# -*- coding: utf-8 -*-
"""
共享工具函数 — 所有 task 模块共用
"""
import json
import hashlib
import os
import re
import shutil
import subprocess
import sys
import threading
import zipfile
from datetime import datetime
from pathlib import Path
from .base import TaskStatus

# ── 日志文件 (线程本地, runner 在执行每个任务前设置) ──
_log_file: Path | None = None
_log_lock = threading.Lock()


def set_log_file(path: Path | None):
    global _log_file
    with _log_lock:
        _log_file = path


def _write_log(line: str):
    global _log_file
    with _log_lock:
        if _log_file:
            try:
                _log_file.parent.mkdir(parents=True, exist_ok=True)
                with open(_log_file, "a", encoding="utf-8") as f:
                    f.write(line + "\n")
            except Exception:
                pass


# ════════════════════════════════════════════
#  终端输出
# ════════════════════════════════════════════

def info(msg: str):
    line = f"  {msg}"
    print(line)
    _write_log(line)


def step(label: str, msg: str):
    line = f"\n[{label}] {msg}"
    print(line)
    _write_log(line)


# ════════════════════════════════════════════
#  命令执行
# ════════════════════════════════════════════

def run_cmd(cmd: list[str], cwd: Path | None = None,
            verbose: bool = False) -> int:
    """运行命令，捕获输出到 GUI 和日志文件，返回退出码。"""
    cmd_str = " ".join(str(c) for c in cmd)
    header = f"    > {cmd_str}"
    print(header)
    _write_log(header)

    try:
        proc = subprocess.Popen(
            cmd, cwd=cwd, shell=True,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding="utf-8", errors="replace",
        )
        for line in proc.stdout:
            line = line.rstrip("\n\r")
            if line.strip():
                print(f"      {line}")
                _write_log(f"      {line}")
        proc.wait()
        code = proc.returncode
    except Exception as e:
        err = f"    FAILED: {e}"
        print(err)
        _write_log(err)
        return -1

    if code != 0:
        fail = f"    FAILED (exit={code})"
        print(fail)
        _write_log(fail)
    else:
        ok = f"    OK (exit=0)"
        print(ok)
        _write_log(ok)
    return code


# ════════════════════════════════════════════
#  文件操作
# ════════════════════════════════════════════

def _rmtree_ignore_locked(path: Path):
    """删除目录树，跳过被锁定的文件。"""
    if not path.exists():
        return
    for root_str, dirs, files in os.walk(str(path), topdown=False):
        root = Path(root_str)
        for name in files:
            try:
                (root / name).unlink()
            except PermissionError:
                pass
        for name in dirs:
            try:
                (root / name).rmdir()
            except OSError:
                pass
    try:
        path.rmdir()
    except OSError:
        pass


def copy_file(src: Path, dst_dir: Path):
    """复制文件，跳过被锁定的。"""
    try:
        shutil.copy2(src, dst_dir)
    except PermissionError:
        info(f"  WARNING: Locked, skipped: {src.name}")


def copy_dir_contents(src: Path, dst: Path):
    """复制目录内容（跳过锁定文件）。"""
    for item in src.iterdir():
        if item.is_dir():
            dst_sub = dst / item.name
            dst_sub.mkdir(parents=True, exist_ok=True)
            for f in item.rglob("*"):
                if f.is_dir():
                    continue
                rel = f.relative_to(item)
                target = dst_sub / rel
                target.parent.mkdir(parents=True, exist_ok=True)
                copy_file(f, target.parent)
        else:
            copy_file(item, dst)


def copy_dir_except(src: Path, dst: Path, skip: set[str] | None = None):
    """复制目录内容，跳过指定名称的子目录（如 node_modules）。"""
    if skip is None:
        skip = set()
    if not src.exists():
        return
    dst.mkdir(parents=True, exist_ok=True)
    for item in src.iterdir():
        if item.name in skip:
            continue
        if item.is_dir():
            dst_sub = dst / item.name
            copy_dir_except(item, dst_sub, skip)
        else:
            copy_file(item, dst)


def check_exists(path: Path, desc: str = "") -> bool:
    if not path.exists():
        info(f"  WARNING: {desc or path.name} not found: {path}")
        return False
    return True


# ════════════════════════════════════════════
#  .NET 构建
# ════════════════════════════════════════════

def dotnet_restore(proj: Path) -> int:
    info(f"Restoring {proj.name}...")
    return run_cmd(["dotnet", "restore", str(proj)])


def dotnet_build(proj: Path, config: str = "Release") -> int:
    info(f"Building {proj.name} ({config})...")
    return run_cmd(["dotnet", "build", str(proj), "-c", config])


def dotnet_publish(proj: Path, output: Path, config: str = "Release",
                   single_file: bool = False) -> int:
    """发布 .NET 项目。"""
    info(f"Publishing {proj.name} to {output.name}...")
    if output.exists():
        shutil.rmtree(output)
    cmd = [
        "dotnet", "publish", str(proj),
        "-c", config,
        "-o", str(output),
        "--self-contained",
    ]
    if single_file:
        cmd += [
            "-p:PublishSingleFile=true",
            "-p:PublishTrimmed=false",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
        ]
    return run_cmd(cmd)


# ════════════════════════════════════════════
#  原生插件构建
# ════════════════════════════════════════════

def clean_cmake_cache(src: Path):
    """清理 CMake 缓存（路径变更后旧 cache 会报错）。"""
    cmake_build = src / "build"
    if not cmake_build.exists():
        return
    stale = False
    for cache in cmake_build.rglob("CMakeCache.txt"):
        try:
            text = cache.read_text(encoding="utf-8", errors="ignore")
            for line in text.splitlines():
                if line.startswith("CMAKE_HOME_DIRECTORY"):
                    cached_src = line.split("=", 1)[-1].strip().replace("\\", "/")
                    if src.as_posix() not in cached_src:
                        stale = True
                    break
        except Exception:
            pass
    if stale:
        info(f"  Stale CMake cache detected, clearing build dir...")
        shutil.rmtree(cmake_build, ignore_errors=True)


# ════════════════════════════════════════════
#  版本信息
# ════════════════════════════════════════════

def read_version_info(mod_dir: Path, player_web_dir: Path,
                      modules_root: Path, fh6_file: Path) -> dict:
    """读取所有版本信息。"""
    player_ver = "0.0.0"
    package_json = player_web_dir / "package.json"
    if package_json.exists():
        try:
            player_ver = json.loads(package_json.read_text(encoding="utf-8")).get("version", player_ver)
        except Exception:
            pass

    cs_ver = "0.0.0"
    cs_file = mod_dir / "MyPluginInfo.cs"
    if cs_file.exists():
        m = re.search(r'PLUGIN_VERSION\s*=\s*"([^"]+)"',
                      cs_file.read_text(encoding="utf-8"))
        if m:
            cs_ver = m.group(1)

    fh6_ver = "0.0.0"
    if fh6_file.exists():
        m = re.search(r'FH6_BRIDGE_VERSION\s+"([^"]+)"',
                      fh6_file.read_text(encoding="utf-8"))
        if m:
            fh6_ver = m.group(1)

    music_versions = {}
    for module_dir, key in [
        ("Netease", "netease"),
        ("QQMusic", "qqmusic"),
        ("Kugou", "kugou"),
        ("Kuwo", "kuwo"),
    ]:
        info_file = modules_root / module_dir / "ModuleInfo.cs"
        if not info_file.exists():
            continue
        m = re.search(r'MODULE_VERSION\s*=\s*"([^"]+)"',
                      info_file.read_text(encoding="utf-8"))
        if m:
            music_versions[key] = m.group(1)

    return {
        "player_version": player_ver,
        "webui_version": player_ver,
        "mod_version": cs_ver,
        "fh6_bridge_version": fh6_ver,
        "mod_versions": {
            "chill_patcher": cs_ver,
            "fh6_omni_bridge": fh6_ver,
        },
        "music_module_versions": music_versions,
        "build_time": datetime.now().isoformat(),
    }


def write_version_json(data: dict, *paths: Path):
    """写入 version_info.json 到多个位置。"""
    text = json.dumps(data, indent=2)
    for p in paths:
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text, encoding="utf-8")
        info(f"  version_info.json -> {p}")


def package_zip(src_dir: Path, zip_path: Path,
                player_asset: Path | None = None) -> bool:
    """将目录打包为 zip，可选复制到播放器资产目录。"""
    if not src_dir.exists():
        info(f"  WARNING: {src_dir} not found, skipping zip")
        return False
    if zip_path.exists():
        zip_path.unlink()
    shutil.make_archive(
        str(zip_path.with_suffix("")),  # 去掉 .zip 后缀
        "zip",
        src_dir,
    )
    if player_asset:
        player_asset.parent.mkdir(parents=True, exist_ok=True)
        copy_file(zip_path, player_asset.parent)
        info(f"  {zip_path.name} -> {player_asset}")
    return True


def stage_verified_runtime_archive(
    archive_path: Path,
    destination: Path,
    expected_sha256: str,
    required_files: tuple[str, ...],
    source_notice: Path | None = None,
) -> bool:
    """Verify and stage a pinned third-party runtime archive."""
    if not archive_path.is_file():
        info(f"  ERROR: Runtime archive not found: {archive_path}")
        return False
    if source_notice is not None and not source_notice.is_file():
        info(f"  ERROR: Runtime source notice not found: {source_notice}")
        return False

    digest = hashlib.sha256()
    with archive_path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    actual_sha256 = digest.hexdigest()
    if actual_sha256.lower() != expected_sha256.lower():
        info(
            f"  ERROR: {archive_path.name} SHA256 mismatch: "
            f"expected {expected_sha256}, got {actual_sha256}"
        )
        return False

    try:
        with zipfile.ZipFile(archive_path, "r") as package:
            members = set(package.namelist())
            required = set(required_files)
            missing = sorted(required - members)
            unexpected = sorted(members - required)
            if missing or unexpected:
                if missing:
                    info(f"  ERROR: Runtime archive is missing: {', '.join(missing)}")
                if unexpected:
                    info(f"  ERROR: Runtime archive has unexpected files: {', '.join(unexpected)}")
                return False

            _rmtree_ignore_locked(destination)
            if destination.exists():
                info(f"  ERROR: Could not replace runtime directory: {destination}")
                return False
            destination.mkdir(parents=True, exist_ok=True)

            for member in required_files:
                target = destination / member
                with package.open(member, "r") as source, target.open("wb") as output:
                    shutil.copyfileobj(source, output)
    except (OSError, zipfile.BadZipFile) as exc:
        info(f"  ERROR: Could not stage {archive_path.name}: {exc}")
        return False

    if source_notice is not None:
        copy_file(source_notice, destination)
        if not (destination / source_notice.name).is_file():
            return False
    return True
