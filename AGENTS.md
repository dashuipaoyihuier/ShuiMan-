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
