# ShuiMan · Offline Comic Reader

[简体中文](README.md) · [Open-source notes](docs/OPEN_SOURCE.en.md)

ShuiMan is a privacy-first offline reader for locally owned comics and books. It has macOS, Android, and Windows implementations. The Windows client uses C# / .NET 10 / WPF and directly reads ZIP/CBZ image archives, including nested folders, Chinese filenames, and natural numeric page ordering.

The project supports local image folders, PDF, EPUB, and additional local formats as they mature. It does not provide, distribute, index, or fetch comic content. Reading progress, bookmarks, and display preferences stay on the device; source files are never modified.

## Windows 0.7.1

[Windows 0.7.1 installer](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/windows-v0.7.1/ShuiMan-Setup-0.7.1-x64.exe) · [Android 1.0.5 APK](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/windows-v0.7.1/Shuiman-1.0.5-android-release.apk) · [macOS 0.6.0 Apple Silicon](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/windows-v0.7.1/Shuiman-0.6.0-macOS-arm64.zip) · [All installers](https://github.com/dashuipaoyihuier/ShuiMan-/releases/tag/windows-v0.7.1)

Requires Windows 10 version 2004+ or Windows 11, x64. The Chinese installer includes .NET, installs for the current user, supports upgrades and standard Windows uninstallation, and preserves the library and source books. Desktop shortcuts and Open With integration are optional. EPUB comic images use the native reader canvas without a browser runtime.

Version 0.7.1 fixes analysis timing when opening a book or jumping to another page. The reader waits for the current page and the neighboring analysis needed for its layout before drawing the page for the first time. A jump promotes the new position immediately instead of waiting for the scan from the old position to finish. Valid cached results are reused. See the [validation record](docs/windows-validation.md) for this release's verification status.

The Windows library and shell use a quiet, light visual style: a series cover homepage opening naturally ordered volumes, grid and list views, resume reading, favorites, read-state filters, series browsing, editable titles/series/tags, and search. The window header, taskbar, application, and installer reuse the original macOS book-and-wave icon. Source folders are remembered and scanned every minute or on demand; removing a source stops scanning while preserving books and progress. Cover decoding runs in the background with a bounded cache. Reader saves preserve library metadata changed in another window.

ZIP/CBZ reading, natural ordering, Chinese archive paths, PDF, multi-frame TIFF, spine-preserving EPUB image reading, MOBI 6 image comics, and manual spread corrections remain available. Manual rotation wins first, explicit EPUB rotation metadata second; only pages without hints use OCR and physical glyph analysis.

Analysis prioritizes the current page and its relevant neighbors before the first drawing, then continues toward the end of the book and fills in other pages. Navigation updates that priority. Persistent checkpoints support reuse and unfinished scans; there is no confirmation button. Spread analysis uses antialiased image reduction and lightly smoothed edge profiles to reduce interference from comic screen tones, while keeping the existing acceptance thresholds. Automatic seam alignment is a separate, per-book setting and defaults to off; recognized spreads still join, and manual seam corrections remain available through sliders. C# / .NET 10 / WPF remains the native Windows stack; macOS and Android build entry points stay independent.

![Windows cover library with original demonstration books](docs/images/windows-library-070.png)

All illustrated covers in this screenshot are original, generated in code for the demonstration.

With the .NET 10 SDK installed, run `./scripts/windows-build.ps1`. To produce the installer, install Inno Setup 6.7+ with `./scripts/windows-setup-compiler.ps1`, then run `./scripts/windows-installer.ps1`. See [Windows documentation](windows/README.md) and [validation results](docs/windows-validation.md) for details and limitations. The release also includes the latest previously published Android and macOS packages under their original version names.

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
- **Windows:** C#, .NET 10, and WPF; Windows 10 version 2004+ and Windows 11, x64.

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
- Per-page PDF loading and password input; EPUB reading in spine order. Windows uses native comic-image rendering; the macOS implementation also has a WebKit path for complex layouts.
- Single-page and two-page views, LTR/RTL reading direction, thumbnails, bookmarks, zoom, and fullscreen.
- Spread detection, page rotation hints, manual spread corrections, and persistent local reading preferences.

## Limitations

This is still a prototype with deliberate manual correction controls. Automatic rotation and paired-spread analysis are heuristic rather than a claim of universal accuracy. Complex EPUB layout, cloud sync, text search, and continuous scrolling remain incomplete or planned.

See the [open-source notes](docs/OPEN_SOURCE.en.md) for repository scope, privacy rules, and release expectations.

Licensed under [Apache License 2.0](LICENSE).
