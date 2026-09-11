# ShuiMan Open-source Notes

[简体中文](OPEN_SOURCE.zh-CN.md)

## Scope

ShuiMan is an offline, local-first comic reader. The public repository contains macOS and Android source code, build scripts, dependency locks, original test fixtures, and necessary product and technical documentation. Windows is planned; no implementation choice or release date is promised.

The code is licensed under [Apache License 2.0](../LICENSE). See [ThirdPartyNotices.txt](../Resources/ThirdPartyNotices.txt) for third-party notices.

## Content allowed in the public repository

- Source code, resources, build scripts, and dependency lock files.
- Design and implementation documentation that is safe to publish.
- Original, reproducible test fixtures, labels, and test notes.
- Build and test instructions without personal data.

## Content that must not be committed

- Comics, ebooks, covers, scans, or any other copyrighted user content.
- Local library paths, book lists, reading history, bookmarks, databases, logs, screenshots, or analysis results.
- Signing certificates, keystores, tokens, accounts, device identifiers, personal email addresses, or absolute local paths.
- APK/AAB files, app bundles, and other reproducible build artifacts.

`.gitignore` covers common book formats, application data, signing material, build directories, and private validation records. Always review `git status --ignored` before committing.

## Build entry points

```bash
scripts/build.sh
scripts/build.sh debug
scripts/test.sh
```

The Android project is in `android/`. Public tests use only original, rebuildable fixtures in `Tests/Fixtures/`; personal corpora must not be used as committed or published validation evidence.

## Release principles

- Release artifacts must not include user content, reading databases, or signing private keys.
- Before publishing a macOS build, validate the target systems and complete Developer ID signing and notarization.
- Before publishing an Android build, use a controlled release-signing process and keep all signing material outside the repository.
- Spread, rotation, and pairing automation are heuristic. Release notes must state their limits and must not claim accuracy that is not backed by public, reproducible evaluation.

The project is developed under developer direction with assistance from OpenAI Codex for implementation, testing, and iteration.
