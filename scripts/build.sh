#!/bin/bash
set -euo pipefail
READER_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$READER_ROOT"
READER_CONFIG="${1:-release}"
case "$READER_CONFIG" in debug|release) ;; *) echo 'Usage: scripts/build.sh [debug|release]' >&2; exit 2;; esac
export CLANG_MODULE_CACHE_PATH="$READER_ROOT/.build/ModuleCache"
export SWIFTPM_MODULECACHE_OVERRIDE="$READER_ROOT/.build/ModuleCache"
mkdir -p "$READER_ROOT/.build" "$READER_ROOT/build"
READER_FLAGS=(--disable-sandbox --cache-path .build/spm-cache --config-path .build/spm-config --security-path .build/spm-security -c "$READER_CONFIG" -j 4)
if ! swift build "${READER_FLAGS[@]}" > "$READER_ROOT/.build/build-$READER_CONFIG.log" 2>&1; then
    tail -100 "$READER_ROOT/.build/build-$READER_CONFIG.log"
    exit 1
fi
READER_BIN="$(swift build "${READER_FLAGS[@]}" --show-bin-path)"
READER_STAGE="$(mktemp -d "$READER_ROOT/build/.bundle-stage.XXXXXX")"
trap 'rm -rf "$READER_STAGE"' EXIT
READER_DESTINATION="$READER_ROOT/build/水漫.app"
READER_APP="$READER_STAGE/水漫.app"
mkdir -p "$READER_APP/Contents/MacOS" "$READER_APP/Contents/Resources"
cp "$READER_BIN/ComicReader" "$READER_APP/Contents/MacOS/ComicReader"
cp "$READER_ROOT/Resources/Info.plist" "$READER_APP/Contents/Info.plist"
cp "$READER_ROOT/Resources/ThirdPartyNotices.txt" "$READER_APP/Contents/Resources/ThirdPartyNotices.txt"
cp "$READER_ROOT/Resources/WaterCover.png" "$READER_APP/Contents/Resources/WaterCover.png"
shopt -s nullglob
for READER_BUNDLE in "$READER_BIN"/*.bundle; do
    ditto "$READER_BUNDLE" "$READER_APP/Contents/Resources/$(basename "$READER_BUNDLE")"
done
if [ -f "$READER_ROOT/Resources/AppIcon.icns" ]; then cp "$READER_ROOT/Resources/AppIcon.icns" "$READER_APP/Contents/Resources/AppIcon.icns"; fi
codesign --force --deep --sign - "$READER_APP"
codesign --verify --deep --strict "$READER_APP"
# Replace the completed bundle without overwriting a running executable's mapped file.
if [ -e "$READER_DESTINATION" ]; then mv "$READER_DESTINATION" "$READER_STAGE/previous.app"; fi
if ! mv "$READER_APP" "$READER_DESTINATION"; then
    if [ -e "$READER_STAGE/previous.app" ]; then mv "$READER_STAGE/previous.app" "$READER_DESTINATION"; fi
    exit 1
fi
printf 'Built: %s\nConfiguration: %s\n' "$READER_DESTINATION" "$READER_CONFIG"
