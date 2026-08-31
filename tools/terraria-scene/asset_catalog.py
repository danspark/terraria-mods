#!/usr/bin/env python3
"""Discover every texture asset in a Terraria Images directory."""

from __future__ import annotations

import argparse
import fnmatch
import json
from dataclasses import asdict, dataclass
from pathlib import Path


@dataclass(frozen=True)
class AssetRecord:
    name: str
    category: str
    format: str
    width: int | None = None
    height: int | None = None


def _category(name: str) -> str:
    if "/" in name:
        return name.split("/", 1)[0]
    prefix = name.split("_", 1)[0]
    return {
        "Background": "Background",
        "Item": "Item",
        "NPC": "NPC",
        "Projectile": "Projectile",
        "Tiles": "Tile",
        "Wall": "Wall",
    }.get(prefix, "Other")


def discover_assets(images: Path, pattern: str = "*") -> tuple[AssetRecord, ...]:
    """Return one deterministic record for each PNG or XNB texture."""
    by_name: dict[str, AssetRecord] = {}
    for path in images.rglob("*"):
        suffix = path.suffix.lower()
        if not path.is_file() or suffix not in {".png", ".xnb"}:
            continue
        name = path.relative_to(images).with_suffix("").as_posix()
        if not fnmatch.fnmatchcase(name.casefold(), pattern.casefold()):
            continue
        record = AssetRecord(name=name, category=_category(name), format=suffix[1:])
        previous = by_name.get(name)
        if previous is None or record.format == "png":
            by_name[name] = record
    return tuple(by_name[name] for name in sorted(by_name, key=str.casefold))


def write_catalog(records: tuple[AssetRecord, ...], output: Path) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "count": len(records),
        "assets": [asdict(record) for record in records],
    }
    output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("images", type=Path, help="Terraria Content/Images directory")
    parser.add_argument("--pattern", default="*", help="case-insensitive glob for asset names")
    parser.add_argument("--output", "-o", type=Path, help="write a JSON catalog")
    return parser


def main() -> int:
    args = _parser().parse_args()
    records = discover_assets(args.images, args.pattern)
    if args.output:
        write_catalog(records, args.output)
        print(f"wrote {args.output} ({len(records)} assets)")
    else:
        for record in records:
            print(f"{record.name}\t{record.category}\t{record.format}")
        print(f"{len(records)} assets")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
