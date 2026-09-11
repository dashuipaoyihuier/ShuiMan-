# ShuiMan · Offline Comic Reader

[简体中文](README.md) · [Open-source notes](docs/OPEN_SOURCE.en.md)

ShuiMan is a privacy-first offline reader for locally owned comics and books. It currently has macOS and Android implementations. Windows is planned, with no implementation choice or release date yet.

The project supports local image folders, PDF, EPUB, and additional local formats as they mature. It does not provide, distribute, index, or fetch comic content. Reading progress, bookmarks, and display preferences stay on the device; source files are never modified.

## Highlight: intelligent spread reading

ShuiMan treats two-page spreads as a core reading experience. Confirmed landscape spreads are displayed on their own and fitted without cropping. For a spread split across two adjacent images, the reader conservatively analyzes the seam, physical left/right placement, and source order before offering automatic composition. It can also suggest a rotation for sideways content using publication-style and text-orientation hints.

Every automatic decision is reversible. Readers can rotate a page, pair or unpair pages, swap physical sides, or restore automatic analysis. Manual corrections always take precedence. Pages with little text, ambiguous text, or an indistinct seam may still need confirmation.

## Platforms

- **macOS:** Swift, SwiftUI, AppKit, and SwiftPM. macOS 14+ is the current target.
- **Android:** Kotlin and Jetpack Compose.
- **Windows:** planned.

The project is developed under developer direction with assistance from **OpenAI Codex** for implementation, testing, and iteration.

## Build and test

```bash
cd <repository-directory>
scripts/build.sh          # Release app: build/水漫.app
scripts/build.sh debug    # Debug app
scripts/test.sh           # Rebuild original fixtures and run native checks
```

The macOS project uses SwiftPM. The Android project lives in `android/`. Public regression tests use only original, rebuildable fixtures in `Tests/Fixtures/`; do not add copyrighted books or personal reading data to the repository.

## Current capabilities

- Natural ordering for image folders; static image support through system decoders.
- Per-page PDF loading and password input; EPUB reading in spine order, with WebKit fallback for complex layouts.
- Single-page and two-page views, LTR/RTL reading direction, thumbnails, bookmarks, zoom, and fullscreen.
- Spread detection, page rotation hints, manual spread corrections, and persistent local reading preferences.

## Limitations

This is still a prototype with deliberate manual correction controls. Automatic rotation and paired-spread analysis are heuristic rather than a claim of universal accuracy. Complex EPUB layout, cloud sync, text search, and continuous scrolling remain incomplete or planned.

See the [open-source notes](docs/OPEN_SOURCE.en.md) for repository scope, privacy rules, and release expectations.

Licensed under [Apache License 2.0](LICENSE).
