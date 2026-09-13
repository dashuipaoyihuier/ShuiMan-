# ComicReader

Native macOS comic reader. Read `macOS漫画阅读器-产品与开发文档.md`, `docs/智能大跨页-功能设计.md`, and `docs/任务清单.md` before changing behavior.

- SwiftUI app shell; AppKit canvas; format and layout logic in ComicCore. Keep UI work on the main actor and expensive decoding off it.
- The source of truth for builds is Package.swift. `scripts/build.sh` creates build/漫读.app. `scripts/test.sh` runs the native regression executable. Full Xcode is optional for these commands; do not claim XCUITest coverage.
- EPUB spine order is authoritative. Preserve duplicate references, missing page positions, and stable source locators.
- A confirmed spread preserves physical left/right placement independently of navigation direction. No source page may be consumed twice.
- Manual corrections override analysis. Do not encode sample filenames or page numbers into detection logic.
- Preserve original comic files. User books are excluded from git; use generated fixtures for public tests.
- Verify the changed behavior, report actual checks and limitations, and update task status. Do not substitute fixture labels or the HTML explainer for automatic detection results.
- Keep main runnable. Use feature branches for substantial changes and commit coherent changes after appropriate checks. Generated fixture media is ignored; track the generator and labels. Remote publication requires user authorization.

## Windows

- The independent Windows implementation lives in `windows/` (C# / .NET 10 / WPF); `Package.swift` remains the macOS build definition. Read `windows/README.md` and `docs/windows-validation.md` for Windows behavior and actual validation limits.
- `scripts/windows-test.ps1` runs generated native core regressions. `scripts/windows-ui-test.ps1` runs real WPF library and native-reader integration checks. `scripts/windows-build.ps1` produces a self-contained x64 portable distribution under `build/windows/`; `scripts/windows-installer.ps1` packages the Windows installer.
- Decode and analyze outside the WPF dispatcher; cancel obsolete work and serialize publication access before disposal. EPUB comics use native image rendering without HTML/CSS webpage layout or script execution. Apply manual rotation first, then explicit EPUB rotation including zero, and only infer orientation when no explicit information exists. Start whole-book analysis on open and persist reusable automatic results separately from manual corrections.
- ZIP/CBZ and image-folder page identities use stable paths and frame indexes, independent of sorted ordinals. Preserve EPUB spine occurrences and missing page positions. A confirmed pair's physical placement must remain independent of reading direction.
- Store local state atomically and preserve unrelated records across concurrent application instances. Only generated fixtures belong in tests; original books and machine credentials stay out of Git.
