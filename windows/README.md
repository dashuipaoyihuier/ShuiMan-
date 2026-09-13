# 水漫 Windows 版

Windows 原生 WPF 客户端，和仓库中的 macOS、Android 客户端共享阅读规则设计。源代码位于 `windows/`，原有平台的构建入口保持独立。

## 运行

从 [Windows 0.6.0 Release](https://github.com/dashuipaoyihuier/ShuiMan-/releases/tag/windows-v0.6.0) 下载 `ShuiMan-Windows-x64.zip`。同一页面提供完整更新说明和 SHA-256 校验文件；GitHub 自动附加的 “Source code” 是开发源码，不是可运行的 Windows 安装包。

需要 Windows 10 2004（内部版本 19041）或更新版本，x64 架构。发行包自带 .NET 运行时：完整解压 `ShuiMan-Windows-x64.zip`，进入 `ShuiMan` 目录并双击 `ShuiMan.exe`。请保留整个程序目录。

复杂 EPUB 的 HTML/CSS 页面由 Microsoft Edge WebView2 显示，需要 [WebView2 Evergreen 运行时](https://developer.microsoft.com/microsoft-edge/webview2/)。多数 Windows 10/11 电脑已包含该组件；普通图片漫画和 PDF 不依赖它。运行时不包含在便携 ZIP 中。

可选安装到当前用户：双击解压目录中的 `Install.cmd`，或执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -DesktopShortcut
```

脚本检查 WebView2，缺少时通过 Windows 包管理器 `winget` 安装微软运行时，然后将程序复制到 `%LOCALAPPDATA%\Programs\ShuiMan`，并创建开始菜单和桌面快捷方式。水漫本身无需管理员权限；WebView2 补装可能出现 Windows 权限提示，遵循[微软运行时分发规则](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)。首次补装 WebView2 需要网络和 winget；没有 winget 的电脑可先用微软网站的官方安装器。更新前关闭程序，从新版解压目录运行同一命令。关闭程序后运行安装目录的 `uninstall.ps1` 可卸载水漫；原漫画、书库、阅读记录和共享 WebView2 运行时会保留。

## 阅读和书库

- “打开文件”或 `Ctrl+O` 选择漫画；支持拖入文件。
- “图片文件夹”直接阅读文件夹及其子目录中的图片。“导入书库”扫描文件夹中的书籍。
- 书库可搜索、收藏、标记已读、改名或移除记录；移除记录保留原始文件。
- 页面、目录、书签侧栏用于跳转；底部页码输入后按 Enter 跳转。
- 支持单页/双页、左右阅读方向、整页/宽度/原始尺寸、缩放、全屏。
- “页面修正”提供旋转、与下一页配对、交换双图左右、解除配对、接缝微调和恢复自动识别。
- 手工确认的双图保存物理左右顺序，切换阅读方向后仍保留；手工修正优先于自动结果。

书库、进度、稳定页面定位、书签、收藏和每本书的阅读设置保存到 `%LOCALAPPDATA%\ShuiMan\library.json`。写入采用临时文件替换并保留上一份 `.bak`；主文件损坏时尝试从备份恢复并显示提示。程序不修改原漫画。

## 格式

| 格式 | Windows 行为 |
| --- | --- |
| ZIP、CBZ | 直接读取包内图片，无需解压；支持嵌套目录、UTF-8 / 常见 GB18030 中文文件名、自然数字排序（1、2、10），忽略隐藏元数据。 |
| 图片、图片文件夹 | 支持 PNG、JPEG、GIF、BMP、TIFF、WebP 等图像格式；TIFF 每帧作为独立页面；动画图片按静态页面读取。 |
| EPUB | 按 spine 顺序阅读，保留重复引用和缺失页面位置；图片页使用原生画布，复杂 HTML/CSS 页使用 WebView2；目录映射到阅读位置。 |
| PDF | 使用 Windows PDF 引擎显示，支持密码提示；页面尺寸和顺序保持原文。 |
| MOBI | 支持无 DRM 的 MOBI 6 图片漫画，以及混合格式中的 MOBI 6 正文；支持未压缩/PalmDOC，保留封面、正文图片顺序和重复引用。 |

加密 ZIP、RAR/CBR、受 DRM 保护的电子书、MOBI HUFF/CDIC、纯 KF8/AZW3，以及 MOBI 文字/混合排版不支持；程序会提示转换为 EPUB 等可读格式。ZIP 不自动展开包内嵌套的 ZIP。危险路径、损坏结构或超过资源限制的文件会被拒绝。图片损坏会保留其页面位置，避免后续进度错位。

智能跨页采用保守的接缝证据，并支持有界的偏移和缩放校正。自动方向识别依赖 Windows 可用的 OCR 语言及页面文字线索；竖排文字和低置信度内容不强行旋转，可手动修正。

Windows 自动文字转向目前采用多角度拉丁文字证据；尚未移植 macOS 的全部 CJK 字形分析，中文/日文页面可使用出版物旋转线索和手动修正。大图解码最长边限制为 8192 像素，缩放时会按需提高解码尺寸；更大的原图不会被修改。复杂 EPUB 页面保留出版物的 HTML/CSS 排版，支持缩放和滚动，页面旋转及双图修正只用于图片画布。

## 开发和构建

安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，重新打开 PowerShell，在仓库根目录运行：

```powershell
.\scripts\windows-test.ps1
.\scripts\windows-ui-test.ps1
.\scripts\windows-build.ps1
```

构建脚本先运行回归检查，再发布完整自带运行时的 `win-x64` 程序、压缩包和 SHA-256 校验文件：

```text
build/windows/ShuiMan/ShuiMan.exe
build/windows/ShuiMan-Windows-x64.zip
build/windows/ShuiMan-Windows-x64.zip.sha256
```

已在同一版本完成检查后，可用 `-SkipTests` 只重新发布。首次构建需要网络恢复 NuGet 包；依赖版本和锁文件随源代码提交。不要求 Visual Studio、Xcode 或单独安装 ImageMagick。

直接运行开发版本：

```powershell
dotnet run --project .\windows\ShuiMan.Windows\ShuiMan.Windows.csproj
```

生成原创演示漫画和格式测试文件：

```powershell
.\scripts\windows-test.ps1 -Fixtures .\build\windows-fixtures
```

`水漫 演示.zip` 用于正常阅读演示；以 `invalid-`、`broken`、`empty`、`unsafe` 命名的文件用于错误处理检查。自动回归使用独立临时目录并在结束后删除，不读取用户漫画。测试源码和生成器跟踪在 Git 中，生成媒体不提交。

`windows-ui-test.ps1` 使用真实 WPF/WebView2 控件和独立临时书库检查 EPUB 资源加载、离线限制、页码同步及关闭窗口。缺少 WebView2 时会明确输出 `SKIP`；该结果不代表 UI 检查通过。运行前关闭相同构建配置的开发程序，避免程序文件被占用；也可用 `-Configuration Debug` 隔离开发检查。

[验证记录](../docs/windows-validation.md)列出本次实际检查与限制。GitHub Actions 的 Windows 工作流执行同一构建脚本并上传便携包；它不自动创建 GitHub Release。
