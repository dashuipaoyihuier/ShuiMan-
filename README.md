# 水漫 · 本地漫画阅读器

[English](README.en.md) · [开源说明](docs/OPEN_SOURCE.zh-CN.md)

水漫（ShuiMan）是一款以隐私和离线阅读为优先的本地漫画阅读器。目前提供 macOS、Android 与 Windows 版本，支持在设备上直接阅读用户合法拥有的图片、PDF、EPUB、MOBI，以及其他逐步完善中的本地漫画格式；Windows 版还可直接阅读 ZIP/CBZ 图片压缩包。项目不提供、分发或索引任何漫画内容。

它面向那些在意阅读顺序、双页跨页和本地文件掌控感的读者：EPUB 以 spine 顺序为准，阅读方向和双页位置可分别调整；对横置跨页及相邻双图跨页提供保守的自动分析，并始终保留逐页手动纠正。阅读进度、书签和显示偏好只保存在本机，原始文件不会被修改。

## Windows 版下载 · 0.6.0

[下载 Windows x64 便携包](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/windows-v0.6.0/ShuiMan-Windows-x64.zip) · [Release 与完整更新说明](https://github.com/dashuipaoyihuier/ShuiMan-/releases/tag/windows-v0.6.0) · [SHA-256 校验文件](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/windows-v0.6.0/ShuiMan-Windows-x64.zip.sha256)

需要 Windows 10 2004+ 或 Windows 11，x64 架构。完整解压 ZIP 后，在 `ShuiMan` 文件夹中双击 **ShuiMan.exe**；需要开始菜单和桌面快捷方式时运行 **Install.cmd**。发行包自带 .NET，复杂 EPUB 页面使用 WebView2；可选安装器会检查并补装该组件。

本次更新：

- 新增原生 Windows 桌面版，支持本地书库、收藏、阅读进度、目录、书签与全屏。
- 新增 **ZIP/CBZ 图片漫画直接阅读**，支持嵌套目录、中文文件名及 `1、2、10` 自然排序，无需先解压。
- 支持图片及多页 TIFF、PDF、EPUB 和无 DRM 的 MOBI 6 图片漫画；EPUB 保留原始阅读顺序、重复引用和缺失页面位置。
- 提供单/双页、左右阅读方向、缩放、智能跨页与手动旋转、配对、左右交换和接缝校正；已确认双图的物理位置不随阅读方向改变。
- 使用原子保存和备份恢复本地记录，并处理多个窗口同时保存不同书籍的情况。

已通过 **38 项核心回归和 12 项真实界面集成检查**。具体测试范围见 [验证记录](docs/windows-validation.md)，格式与识别限制见 [Windows 使用说明](windows/README.md)。本次发布的二进制包面向 Windows；其他平台版本仍可在 [历史 Release](https://github.com/dashuipaoyihuier/ShuiMan-/releases/tag/Comic) 查看。

## 特色：智能大跨页

水漫将大跨页作为核心阅读体验：遇到已确认的横向跨页时，画面会独占显示并完整适配；对两张相邻图片构成的跨页，会结合接缝、物理左右位置与阅读顺序做保守分析、自动组合。对于竖着存储的横向内容，应用还会结合出版物样式和文字方向线索建议转正。自动判断始终可以被手动旋转、配对、交换左右、取消或恢复，且手动修正优先于后续分析；无字、少字或接缝不明显的页面仍可能需要人工确认。

macOS 端基于 Swift、SwiftUI 与 AppKit，Android 端基于 Kotlin 与 Jetpack Compose，Windows 端基于 C#、.NET 10 与 WPF。项目在开发者主导下，借助 **OpenAI Codex** 协作实现、测试和迭代。

macOS 版本 **0.5.0** 面向 Apple Silicon，部署目标 macOS 14+；原版本在 macOS 15.7.3 验证。

Windows 首版 **0.6.0** 面向 Windows 10 2004+ / Windows 11 x64。完整解压 Windows 发行包后，双击 `ShuiMan.exe`；也可运行 `Install.cmd` 安装到当前用户并创建快捷方式。便携包自带 .NET，复杂 EPUB 页面需要 WebView2。完整功能、格式限制、构建步骤见 [Windows 说明](windows/README.md)，测试结果见 [Windows 验证记录](docs/windows-validation.md)。

在 Windows 上从源码构建：

```powershell
.\scripts\windows-build.ps1
```

输出：`build/windows/ShuiMan/ShuiMan.exe` 和 `build/windows/ShuiMan-Windows-x64.zip`。支持 ZIP/CBZ 中文文件名、嵌套目录、自然数字排序，以及书库、进度、书签、缩放、双页和手动跨页修正。

本项目以 [Apache License 2.0](LICENSE) 发布。

水波主题封面与蓝绿 UI 说明见 [水漫设计与安装](docs/水漫设计与安装.md)。

## 应用截图

以下为完整 macOS 应用窗口截图，阅读内容是代码绘制的原创演示册《Quiet Valley》，不含用户书籍。自动识别标记来自应用实际运行，仅说明这些演示页的结果，不代表通用准确率。

### 1. 单页

竖版页面完整适配，同时显示顶部工具栏、左侧缩略图和底部阅读进度。

![单页阅读：工具栏、缩略图与进度](docs/images/reader-single-page.png)

### 2. 侧转跨页自动转正

侧转存储的横向跨页自动转正，并完整适配窗口；缩略图保留来源页面的方向。本例 EPUB 提供了旋转样式线索。

![侧转跨页自动转正并完整适配](docs/images/reader-rotated-spread.png)

### 3. 相邻双图自动合成大跨页

两个独立图片文件被自动识别为连续画面，组合成完整大跨页；缩略图同时选中两页，底栏显示“完整双图 · 自动识别”。

![两张相邻图片自动合成完整大跨页](docs/images/reader-paired-spread.png)

### 4. 手动修正与恢复识别

右上角菜单提供旋转、与下一页配对、交换左右、接缝校正、取消配对和恢复自动识别。手动修正始终优先。

![展开的页面修正与阅读设置菜单](docs/images/reader-manual-correction.png)

### 5. 书库与卷列表

按系列组织本地书籍，查看卷列表、阅读状态与进度。截图仅展示原创演示系列。

![书库中的原创系列、卷列表与阅读状态](docs/images/library.png)

## 直接运行

双击 `build/水漫.app`。打开后用 **⌘O** 选择漫画或图片文件夹，也可以拖入窗口。书库保存最近阅读，点击书籍可接着读。

