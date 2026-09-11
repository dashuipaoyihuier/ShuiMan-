#!/bin/bash
set -euo pipefail
READER_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$READER_ROOT"
export CLANG_MODULE_CACHE_PATH="$READER_ROOT/.build/ModuleCache"
export SWIFTPM_MODULECACHE_OVERRIDE="$READER_ROOT/.build/ModuleCache"
swift build --disable-sandbox --cache-path .build/spm-cache --config-path .build/spm-config --security-path .build/spm-security --product ReaderChecks -j 4
READER_CHECKS="$(swift build --disable-sandbox --cache-path .build/spm-cache --config-path .build/spm-config --security-path .build/spm-security --show-bin-path)/ReaderChecks"
"$READER_CHECKS" "$@"
