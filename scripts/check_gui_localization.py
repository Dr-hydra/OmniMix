#!/usr/bin/env python3
"""Report user-visible Chinese XAML strings missing from the English dictionary."""

from __future__ import annotations

import re
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
GUI_ROOT = REPO_ROOT / "OmniMixPlayer" / "gui_vbnet" / "OmniMixFrontend"
ENGLISH_DICTIONARY = GUI_ROOT / "Localization" / "Strings.en-US.xaml"
VISIBLE_ATTRIBUTE = re.compile(
    r'(?:Text|Title|ToolTip|HintText|Info)="([^"\r\n]*[\u3400-\u9fff][^"\r\n]*)"'
)
SOURCE_KEY = re.compile(r'x:Key="Loc\.Source\.([^"]+)"')


def main() -> int:
    dictionary_text = ENGLISH_DICTIONARY.read_text(encoding="utf-8")
    translated = set(SOURCE_KEY.findall(dictionary_text))
    missing: list[tuple[Path, str]] = []

    for xaml_path in sorted(GUI_ROOT.rglob("*.xaml")):
        if xaml_path.parent.name == "Localization":
            continue
        for source_text in sorted(set(VISIBLE_ATTRIBUTE.findall(xaml_path.read_text(encoding="utf-8")))):
            if source_text not in translated:
                missing.append((xaml_path.relative_to(REPO_ROOT), source_text))

    if not missing:
        print("GUI localization check passed: all visible Chinese XAML strings have English entries.")
        return 0

    print("Missing English GUI localization entries:")
    for path, source_text in missing:
        print(f"  {path}: {source_text}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
