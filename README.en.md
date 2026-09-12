# ShuiMan · Offline Comic Reader

[简体中文](README.md) · [Open-source notes](docs/OPEN_SOURCE.en.md)

ShuiMan is a privacy-first offline reader for locally owned comics and books. It has macOS, Android, and Windows implementations. The Windows client uses C# / .NET 10 / WPF and directly reads ZIP/CBZ image archives, including nested folders, Chinese filenames, and natural numeric page ordering.

The project supports local image folders, PDF, EPUB, and additional local formats as they mature. It does not provide, distribute, index, or fetch comic content. Reading progress, bookmarks, and display preferences stay on the device; source files are never modified.

## Windows 0.6.0

Requires Windows 10 version 2004+ or Windows 11, x64. Extract the complete Windows distribution and run `ShuiMan.exe`, or run `Install.cmd` to install for the current user. The portable distribution includes .NET; complex EPUB pages use Microsoft Edge WebView2, checked by the optional installer.

With .NET 10 SDK installed, run `./scripts/windows-build.ps1` from PowerShell. The script runs regression checks and creates `build/windows/ShuiMan/ShuiMan.exe` and `build/windows/ShuiMan-Windows-x64.zip`. The client includes a local library, persistent reading progress, bookmarks, single/double pages, direction, zoom, fullscreen, and manual spread corrections.

See [Windows documentation](windows/README.md) and [actual validation results](docs/windows-validation.md) for supported formats, build details, and known limitations. The macOS and Android build entry points remain independent.

## Highlight: intelligent spread reading

ShuiMan treats two-page spreads as a core reading experience. Confirmed landscape spreads are displayed on their own and fitted without cropping. For a spread split across two adjacent images, the reader conservatively analyzes the seam, physical left/right placement, and source order before offering automatic composition. It can also suggest a rotation for sideways content using publication-style and text-orientation hints.

Every automatic decision is reversible. Readers can rotate a page, pair or unpair pages, swap physical sides, or restore automatic analysis. Manual corrections always take precedence. Pages with little text, ambiguous text, or an indistinct seam may still need confirmation.

## Application screenshots

These are complete macOS application-window screenshots showing Quiet Valley, an original demonstration book drawn in code. No personal books are included. Detection indicators reflect the application's actual results for these pages, not a claim of general accuracy.

### 1. Single page

A portrait page fitted in full, with the toolbar, thumbnail sidebar, and reading progress visible.

![Single-page reader with toolbar, thumbnails, and progress](docs/images/reader-single-page.png)

### 2. Automatic rotation of a sideways spread

A spread stored sideways is automatically rotated and fitted without cropping. Its thumbnail retains the source orientation. This EPUB example supplies a rotation-style hint.

![Automatically rotated and fully fitted spread](docs/images/reader-rotated-spread.png)

### 3. Automatic composition of adjacent pages

Two separate image files are automatically recognized as one continuous spread. Both thumbnails are selected, and the bottom bar indicates automatic paired-spread recognition.

![Two adjacent images automatically composed into a complete spread](docs/images/reader-paired-spread.png)

### 4. Manual corrections

The expanded top-right menu exposes rotation, pairing, swapping physical sides, seam alignment, unpairing, and restoring automatic recognition. Manual corrections take precedence.

![Expanded page-correction and reading-settings menu](docs/images/reader-manual-correction.png)

### 5. Library and volumes

Organize local books by series and view volumes, reading states, and progress. Only the original demonstration series is shown.

![Library with an original series, volumes, and reading states](docs/images/library.png)

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
