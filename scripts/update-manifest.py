#!/usr/bin/env python3
"""Idempotently add or update a plugin version in docs/manifest.json."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from datetime import datetime, timezone

DEFAULT_GUID = "a7c2f0e4-6b1d-4f3a-9c8e-2d5b7a1e4f60"
DEFAULT_MANIFEST = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "docs",
    "manifest.json",
)

PLUGIN_DEFAULTS = {
    "guid": DEFAULT_GUID,
    "name": "TorBox Refresh",
    "description": "Refreshes the TorBox mount when Jellyfin scans the library.",
    "overview": (
        "Calls the TorBox Media Center control API after a library scan and "
        "queues a follow-up scan when the mount changed."
    ),
    "owner": "Dan Corley",
    "category": "Library",
}

CHECKSUM_RE = re.compile(r"^[0-9A-Fa-f]{32}$")
VERSION_RE = re.compile(r"^\d+(?:\.\d+){1,3}$")


def version_key(version: str) -> tuple[int, ...]:
    # Tolerate prerelease suffixes such as 1.1.0-beta while sorting.
    return tuple(int(part) for part in re.findall(r"\d+", version))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default=DEFAULT_MANIFEST, help="Manifest path.")
    parser.add_argument("--guid", default=DEFAULT_GUID, help="Plugin GUID.")
    parser.add_argument("--version", required=True, help="Plugin version, for example 1.1.0.0.")
    parser.add_argument("--target-abi", required=True, help="Jellyfin targetAbi, for example 12.4.0.0.")
    parser.add_argument("--source-url", required=True, help="Release zip download URL.")
    parser.add_argument("--checksum", required=True, help="Uppercase MD5 checksum of the zip.")
    parser.add_argument("--timestamp", help="Release timestamp (ISO 8601). Defaults to the existing value or now.")
    parser.add_argument("--changelog", help="Release notes. Defaults to the existing value.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()

    if not VERSION_RE.match(args.version):
        print(f"Invalid version: {args.version}", file=sys.stderr)
        return 2
    if not VERSION_RE.match(args.target_abi):
        print(f"Invalid targetAbi: {args.target_abi}", file=sys.stderr)
        return 2
    if not CHECKSUM_RE.match(args.checksum):
        print(f"Invalid MD5 checksum: {args.checksum}", file=sys.stderr)
        return 2

    with open(args.manifest, encoding="utf-8") as handle:
        manifest = json.load(handle)

    plugin = next((item for item in manifest if item.get("guid") == args.guid), None)
    if plugin is None:
        plugin = dict(PLUGIN_DEFAULTS)
        plugin["guid"] = args.guid
        plugin["versions"] = []
        manifest.append(plugin)

    versions = plugin.setdefault("versions", [])
    existing = next((item for item in versions if item.get("version") == args.version), None)

    timestamp = args.timestamp
    if not timestamp:
        timestamp = (existing or {}).get("timestamp")
    if not timestamp:
        timestamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    changelog = args.changelog
    if changelog is None:
        changelog = (existing or {}).get("changelog", f"Release {args.version}.")

    entry = {
        "version": args.version,
        "changelog": changelog,
        "targetAbi": args.target_abi,
        "sourceUrl": args.source_url,
        "checksum": args.checksum.upper(),
        "timestamp": timestamp,
    }

    if existing is None:
        versions.append(entry)
    else:
        versions[versions.index(existing)] = entry

    # Jellyfin reads the newest entry first; keep the list deterministic.
    versions.sort(key=lambda item: version_key(item["version"]), reverse=True)

    rendered = json.dumps(manifest, indent=2) + "\n"
    with open(args.manifest, encoding="utf-8") as handle:
        current = handle.read()

    if rendered == current:
        print(f"Manifest unchanged: {args.manifest}")
        return 0

    with open(args.manifest, "w", encoding="utf-8") as handle:
        handle.write(rendered)

    print(f"Updated {args.manifest}: {args.version} ({args.target_abi})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
