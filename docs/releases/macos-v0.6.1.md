# 水漫 macOS 0.6.1

阅读漫画时按 **↑**，当前来源页顺时针旋转 90°并保存为手动修正。每次按下只旋转一次，长按不连续旋转，连续按四次恢复原方向。保留 R / Shift+R，并在修正菜单显示 ↑ 提示。

快捷键在阅读窗口生效，隐藏控件时同样可用；输入框、附加对话框、修饰键组合、书库及 EPUB 原版式视图保留原有按键行为。沿用已有手动旋转逻辑：手动修正优先，旋转当前页会解除其双图配对并独页展示，不改写原漫画。

## 安装包

- macOS 0.6.1 Apple Silicon：`Shuiman-0.6.1-macOS-arm64.zip`（macOS 14+）
- Windows 0.7.2：`ShuiMan-Setup-0.7.2-x64.exe`
- Android 1.0.5：`Shuiman-1.0.5-android-release.apk`

Windows 与 Android 复用 windows-v0.7.2 Release 的正式原包，SHA-256 已核对一致；本轮仅更新 macOS。

## 验证

- 原生 AppKit 事件检查 8 项通过：按一次旋转、长按抑制、四次归零、修饰键保护、文本输入保护、书库保护、EPUB 原版式保护、隐藏控件时旋转。入口：`scripts/test-shortcuts.sh`，编译实际窗口事件桥，使用隔离的会话替身，不写用户书库。这不是完整应用端到端测试或 XCUITest。
- `scripts/build.sh release` 构建成功；`scripts/test.sh` 与 Release 原生回归均为 106 项通过，包含旋转图像尺寸和 SQLite 重开后保留旋转修正。
- macOS ZIP 解压后通过 `codesign --verify --deep --strict`，版本 0.6.1 / build 10，arm64。沿用 ad-hoc 签名，未进行 Developer ID 公证、Intel 或 macOS 14 实机验证。

GitHub Release 沿用“水漫 · 最新安装包”标题、空正文，仅附三个平台安装包。
