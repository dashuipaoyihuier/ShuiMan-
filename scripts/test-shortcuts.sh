#!/bin/bash
set -euo pipefail
READER_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$READER_ROOT"
mkdir -p .build
swiftc -swift-version 5 -parse-as-library Sources/ComicReader/ReadingWindowBridge.swift Tests/ShortcutChecks/Main.swift -o .build/ReaderShortcutChecks
.build/ReaderShortcutChecks
