#!/usr/bin/env python3
"""Validate release version metadata without hard-coding a specific release number."""
from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


@dataclass(frozen=True)
class VersionInfo:
    plugin: str
    assembly: str
    file: str


def _extract(pattern: str, text: str, label: str) -> str:
    match = re.search(pattern, text, re.MULTILINE)
    if not match:
        raise ValueError(f"Could not find {label} version metadata")
    return match.group(1)


def read_versions(root: Path = ROOT) -> VersionInfo:
    plugin_text = (root / "HealthAutoArrange.Plugin/Plugin.cs").read_text(encoding="utf-8")
    assembly_text = (root / "HealthAutoArrange.Plugin/Properties/AssemblyInfo.cs").read_text(encoding="utf-8")

    plugin = _extract(
        r'\[BepInPlugin\(\s*"[^"]+"\s*,\s*"[^"]+"\s*,\s*"([^"]+)"\s*\)\]',
        plugin_text,
        "BepInPlugin",
    )
    assembly = _extract(r'AssemblyVersion\("([^"]+)"\)', assembly_text, "AssemblyVersion")
    file_version = _extract(r'AssemblyFileVersion\("([^"]+)"\)', assembly_text, "AssemblyFileVersion")
    return VersionInfo(plugin=plugin, assembly=assembly, file=file_version)


def validate_versions(root: Path = ROOT, tag: str | None = None) -> tuple[VersionInfo | None, list[str]]:
    errors: list[str] = []
    try:
        info = read_versions(root)
    except (OSError, ValueError) as exc:
        return None, [str(exc)]

    expected_assembly = f"{info.plugin}.0"
    if info.assembly != expected_assembly:
        errors.append(f"AssemblyVersion is {info.assembly}; expected {expected_assembly} from plugin {info.plugin}")
    if info.file != expected_assembly:
        errors.append(f"AssemblyFileVersion is {info.file}; expected {expected_assembly} from plugin {info.plugin}")

    if tag:
        tag_version = tag[1:] if tag.lower().startswith("v") else tag
        if tag_version != info.plugin:
            errors.append(f"release tag {tag!r} does not match plugin version {info.plugin!r}")

    return info, errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--tag", help="Optional release tag (for example v1.2.3) that must match plugin metadata")
    args = parser.parse_args()

    info, errors = validate_versions(ROOT, args.tag)
    if info:
        print(f"Plugin={info.plugin} Assembly={info.assembly} File={info.file}")
    if errors:
        for error in errors:
            print(f"[FAIL] {error}", file=sys.stderr)
        return 1
    print("[PASS] version metadata is consistent" + (" with release tag" if args.tag else ""))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
