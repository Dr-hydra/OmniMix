# -*- coding: utf-8 -*-
"""Command-line wrapper for the OmniMix build task tree.

This keeps the historical `build.cmd player --full` style entrypoint while all
actual build steps live in `build_tree.py`, shared with the GUI build manager.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from build_config import setup_toolchain
from build_tree import build_tree
from tasks.base import TaskNode, TaskStatus


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
    parser.add_argument("--dry-run", action="store_true", help="Print the task tree without running it.")
    args = parser.parse_args()

    setup_toolchain()
    roots = build_tree(args.command, args.full)
    if args.dry_run:
        for root in roots:
            _print_tree(root)
        return 0

    ok = True
    for root in roots:
        ok = _run_node(root) and ok
        if not ok:
            break

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

if __name__ == "__main__":
    raise SystemExit(main())
