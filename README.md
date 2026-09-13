# 水漫 · 本地漫画阅读器

[English](README.en.md) · [开源说明](docs/OPEN_SOURCE.zh-CN.md)

水漫（ShuiMan）是一款以隐私和离线阅读为优先的本地漫画阅读器。目前提供 macOS、Android 与 Windows 版本，支持在设备上直接阅读用户合法拥有的图片、PDF、EPUB、MOBI，以及其他逐步完善中的本地漫画格式；Windows 版还可直接阅读 ZIP/CBZ 图片压缩包。项目不提供、分发或索引任何漫画内容。

它面向那些在意阅读顺序、双页跨页和本地文件掌控感的读者：EPUB 以 spine 顺序为准，阅读方向和双页位置可分别调整；对横置跨页及相邻双图跨页提供保守的自动分析，并始终保留逐页手动纠正。阅读进度、书签和显示偏好只保存在本机，原始文件不会被修改。

## 下载

[Windows 0.7.2 安装包](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/macos-v0.6.1/ShuiMan-Setup-0.7.2-x64.exe) · [Android 1.0.5 APK](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/macos-v0.6.1/Shuiman-1.0.5-android-release.apk) · [macOS 0.6.1 Apple Silicon](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/macos-v0.6.1/Shuiman-0.6.1-macOS-arm64.zip) · [全部安装包](https://github.com/dashuipaoyihuier/ShuiMan-/releases/tag/macos-v0.6.1)

需要 Windows 10 2004+ 或 Windows 11，x64 架构。双击 **ShuiMan-Setup-0.7.2-x64.exe**，按照中文向导安装；之后从开始菜单打开水漫，在 Windows“已安装的应用”中卸载。安装到当前用户，内含 .NET；升级保留书库和进度，卸载保留原漫画与阅读记录。EPUB 漫画图片使用原生阅读画布，不需要浏览器运行时。

0.7.2 新增 ↑ 顺时针旋转快捷键。0.7.1 修复打开书籍和跳页时的识别时序：先等待当前页及影响其布局的邻页分析，再首次绘制正文；跳到新位置会优先处理该处，不必等旧位置扫完整本。已有有效缓存时直接复用。本轮验证进度见[验证记录](docs/windows-validation.md)。

Windows 版功能：

- **全新桌面界面**：浅色侧栏、柔和层次、系列封面首页；点击系列进入按卷号自然排序的分卷，支持网格和详细列表。窗口标题、任务栏、程序和安装器统一使用 macOS 原版书页与水波图标。
- **完整本地书库**：多文件与目录导入、封面缓存、继续阅读、收藏、阅读状态、系列分组与卷列表、书名/系列/标签编辑和搜索。
- **漫画来源管理**：保存来源目录、手动刷新与每分钟扫描新书、停止扫描、文件失联提示；导入保持原文件位置和既有阅读记录。
- **标准 Windows 安装包**：当前用户安装、原地升级、开始菜单、可选桌面快捷方式、标准卸载和可选“打开方式”注册。
- **先识别，再显示**：当前页与相关邻域优先完成方向和接缝分析，首次显示即使用已识别布局，无需点击确认。后台从当前阅读位置向后识别，再补齐其他页面；跳页时会重新调整优先顺序。结果保存在本地缓存，再次打开继续未完成的识别。
- **方向规则与 macOS 阅读习惯对齐**：手动修正优先，其次遵循 EPUB 当前页的明确旋转信息；没有标记时才分析文字和字形。跨页分析改进了缩小图像时的抗混叠与接缝轮廓处理，减少漫画网点对接缝证据的干扰，保留原有接受阈值和手动修正入口。
- 保留 ZIP/CBZ 中文文件名、嵌套目录和自然排序，以及 PDF、EPUB 图片漫画、图片、多帧 TIFF、MOBI 6 图片漫画与跨页修正；接缝可通过可视滑块手动调整，自动偏移与缩放默认关闭，可按书籍主动开启并保存。

详细验证见 [验证记录](docs/windows-validation.md)，使用和格式限制见 [Windows 说明](windows/README.md)。其他平台版本仍可在 [历史 Release](https://github.com/dashuipaoyihuier/ShuiMan-/releases/tag/Comic) 查看。

![Windows 0.7 封面书库：原创演示系列](docs/images/windows-library-070.png)

截图中的封面与页面均为代码绘制的原创演示内容，不包含用户漫画。

## 特色：智能大跨页

水漫将大跨页作为核心阅读体验：遇到已确认的横向跨页时，画面会独占显示并完整适配；对两张相邻图片构成的跨页，会结合接缝、物理左右位置与阅读顺序做保守分析、自动组合。对于竖着存储的横向内容，应用还会结合出版物样式和文字方向线索建议转正。自动判断始终可以被手动旋转、配对、交换左右、取消或恢复，且手动修正优先于后续分析；无字、少字或接缝不明显的页面仍可能需要人工确认。

macOS 端基于 Swift、SwiftUI 与 AppKit，Android 端基于 Kotlin 与 Jetpack Compose，Windows 端基于 C#、.NET 10 与 WPF。项目在开发者主导下，借助 **OpenAI Codex** 协作实现、测试和迭代。

macOS 版本 **0.6.1** 面向 Apple Silicon，部署目标 macOS 14+。阅读漫画时按 **↑** 顺时针旋转当前页 90°，连续按四次恢复原方向；修正会保存。保留 R / Shift+R 旋转快捷键，输入框和 EPUB 原版式阅读不拦截 ↑。

Windows **0.7.2** 提供标准安装包；从源码构建还可生成免安装 ZIP。C# / WPF 阅读核心继续使用，书库与界面已重做。完整功能、格式限制、构建步骤见 [Windows 说明](windows/README.md)。

在 Windows 上从源码构建：

```powershell
.\scripts\windows-build.ps1
.\scripts\windows-installer.ps1
```

安装包构建需 Inno Setup 6.7+，可先运行 `scripts/windows-setup-compiler.ps1` 安装编译器。输出：`build/windows/ShuiMan/ShuiMan.exe`、便携 ZIP 和 `ShuiMan-Setup-0.7.2-x64.exe`。支持 ZIP/CBZ 中文文件名、嵌套目录、自然数字排序，以及书库、进度、书签、缩放、双页和手动跨页修正。

开发新手可参考 [Windows 上提交代码](docs/git-windows.md)，了解检查改动、提交、推送和创建功能分支。

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

