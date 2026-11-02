#!/usr/bin/env bash
#
# Build the plugin and create the Jellyfin release archive.
#
# The source meta.json is never modified. A staged copy is written with the
# requested plugin version and targetAbi, then zipped with the DLL at the
# archive root so Jellyfin can install it directly.
#
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_DIR="$ROOT_DIR/Jellyfin.Plugin.TorBoxRefresh"
SOLUTION="$ROOT_DIR/Jellyfin.Plugin.TorBoxRefresh.sln"
SOURCE_META="$PROJECT_DIR/meta.json"

VERSION=""
TARGET_ABI=""
JELLYFIN_VERSION="${JELLYFIN_VERSION:-12.4.0}"
JELLYFIN_REPO="${JELLYFIN_REPO:-}"
OUTPUT_DIR="${OUTPUT_DIR:-$ROOT_DIR/dist}"
BUILD=1

usage() {
    cat <<'EOF'
Usage: scripts/package.sh [options]

Options:
  --version VERSION           Plugin version in meta.json and the zip name.
                              Defaults to the version in meta.json.
  --target-abi ABI            targetAbi for the staged meta.json.
                              Defaults to the Jellyfin version padded to 4 parts.
  --jellyfin-version VERSION  Jellyfin.Controller/Jellyfin.Model version (default 12.4.0).
  --jellyfin-repo PATH        Build against a Jellyfin source tree instead of NuGet.
  --output DIR                Output directory (default dist/).
  --no-build                  Reuse an existing Release build.
  -h, --help                  Show this help.

Outputs (also written to $GITHUB_OUTPUT when set):
  zip_path, md5
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) VERSION="${2:?--version needs a value}"; shift 2 ;;
        --target-abi) TARGET_ABI="${2:?--target-abi needs a value}"; shift 2 ;;
        --jellyfin-version) JELLYFIN_VERSION="${2:?--jellyfin-version needs a value}"; shift 2 ;;
        --jellyfin-repo) JELLYFIN_REPO="${2:?--jellyfin-repo needs a value}"; shift 2 ;;
        --output) OUTPUT_DIR="${2:?--output needs a value}"; shift 2 ;;
        --no-build) BUILD=0; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

if [[ -z "$VERSION" ]]; then
    VERSION="$(python3 - "$SOURCE_META" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    print(json.load(handle)["version"])
PY
)"
fi

if [[ -z "$TARGET_ABI" ]]; then
    TARGET_ABI="$(python3 - "$JELLYFIN_VERSION" <<'PY'
import sys

parts = sys.argv[1].split(".")
parts = (parts + ["0", "0", "0", "0"])[:4]
print(".".join(parts))
PY
)"
fi

STAGE_DIR="$(mktemp -d "${TMPDIR:-/tmp}/torbox-refresh-package.XXXXXX")"
trap 'rm -rf "$STAGE_DIR"' EXIT

if [[ "$BUILD" -eq 1 ]]; then
    BUILD_ARGS=(-c Release "-p:JellyfinVersion=$JELLYFIN_VERSION" "-p:Version=$VERSION")
    if [[ -n "$JELLYFIN_REPO" ]]; then
        BUILD_ARGS+=("-p:JellyfinRepo=$JELLYFIN_REPO")
    fi

    echo "Building $VERSION against Jellyfin $JELLYFIN_VERSION..."
    dotnet build "$SOLUTION" "${BUILD_ARGS[@]}"
fi

DLL="$PROJECT_DIR/bin/Release/net10.0/Jellyfin.Plugin.TorBoxRefresh.dll"
if [[ ! -f "$DLL" ]]; then
    echo "Missing build output: $DLL" >&2
    echo "Run without --no-build, or build the solution in Release first." >&2
    exit 1
fi

cp "$DLL" "$STAGE_DIR/Jellyfin.Plugin.TorBoxRefresh.dll"
python3 - "$SOURCE_META" "$STAGE_DIR/meta.json" "$VERSION" "$TARGET_ABI" <<'PY'
import json
import sys

source, destination, version, target_abi = sys.argv[1:5]
with open(source, encoding="utf-8") as handle:
    meta = json.load(handle)

meta["version"] = version
meta["targetAbi"] = target_abi

with open(destination, "w", encoding="utf-8") as handle:
    json.dump(meta, handle, indent=2)
    handle.write("\n")
PY

mkdir -p "$OUTPUT_DIR"
OUTPUT_DIR="$(cd "$OUTPUT_DIR" && pwd)"
ZIP_PATH="$OUTPUT_DIR/Jellyfin.Plugin.TorBoxRefresh_${VERSION}.zip"
rm -f "$ZIP_PATH"

# -j drops directory names so the DLL and meta.json are at the archive root.
(cd "$STAGE_DIR" && zip -X -q "$ZIP_PATH" "Jellyfin.Plugin.TorBoxRefresh.dll" "meta.json")

if command -v md5 >/dev/null 2>&1; then
    MD5="$(md5 -q "$ZIP_PATH")"
else
    MD5="$(md5sum "$ZIP_PATH" | awk '{print $1}')"
fi
MD5="$(printf '%s' "$MD5" | tr '[:lower:]' '[:upper:]')"

if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    {
        echo "zip_path=$ZIP_PATH"
        echo "md5=$MD5"
    } >> "$GITHUB_OUTPUT"
fi

echo "Archive: $ZIP_PATH"
echo "MD5: $MD5"
