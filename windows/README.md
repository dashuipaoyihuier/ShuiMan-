# 水漫 Windows 版

Windows 原生 WPF 客户端，和仓库中的 macOS、Android 客户端共享阅读规则设计。源代码位于 `windows/`，原有平台的构建入口保持独立。

窗口标题、任务栏、程序与安装器统一使用 macOS 原版书页与水波图标；Windows 所需的各尺寸从同一原图提取或缩小。

## 运行

从 [Windows 0.7.0 Release](https://github.com/dashuipaoyihuier/ShuiMan-/releases/tag/windows-v0.7.0) 下载 **ShuiMan-Setup-0.7.0-x64.exe**。双击后按中文向导安装。该页面同时收录最新已发布的 Android、macOS 程序包。“Source code” 是开发源码；Windows 便携 ZIP 与校验文件可通过下方构建脚本生成，或从 Windows 工作流产物获取。

需要 Windows 10 2004（内部版本 19041）或更新版本，x64 架构。程序内含 .NET 运行时。安装包默认安装到 `%LOCALAPPDATA%\Programs\ShuiMan`，无需管理员权限；创建开始菜单快捷方式，可选桌面快捷方式和 ZIP/CBZ/EPUB/PDF/MOBI 的“打开方式”入口，不修改这些格式的默认应用。

升级时关闭水漫并运行新版安装包，原安装位置、书库和进度会保留；支持从 0.6.0 脚本安装目录升级。在 Windows“设置 → 应用 → 已安装的应用”中卸载水漫，原漫画、`%LOCALAPPDATA%\ShuiMan` 中的书库均保留。

EPUB 按出版物顺序读取漫画图片，使用与 ZIP/PDF 相同的原生画布；无需 WebView2 或浏览器运行时。纯文字页与无法提取漫画图片的页面会保留位置并显示提示。便携包完整解压后双击 `ShuiMan/ShuiMan.exe` 即可；正式安装推荐 Setup.exe。

## 阅读和书库

- 在书库点“导入漫画”，一次添加多个文件；也可拖入文件或目录。在“漫画来源”中添加、刷新或停止扫描目录。
- 来源目录每分钟扫描一次，也可手动刷新。新文件自动进入书库；重扫保留自定义书名、标签、收藏和进度。来源中已移除的条目会在下次扫描重新发现；要停止发现，可先停止扫描该目录。
- 默认首页只展示系列封面，点击系列进入分卷。优先从原文件名提取“卷 / vol / volume”卷号，再按显示书名自然排序（1、2、10）；修改显示书名不扰乱已有卷号。分卷封面网格与详细列表可切换；每页最多 48 本，封面按需生成并在本地缓存，最多两个解码任务同时运行。
- 按书名、系列、标签搜索，按阅读状态筛选，按最近阅读、名称、系列或添加时间排序。“系列”展示分组，点击进入卷列表。
- 点击星标收藏；“···”菜单可编辑书名、系列、标签、阅读状态，或移除书库记录。系列初始取目录名，同名系列合并显示。
- 点击封面或“继续阅读”进入阅读视图；标题栏“书库”或 `Ctrl+L` 返回书库，保留当前打开的书和进度。“阅读”可直接回到原页。
- 页面、目录、书签侧栏用于跳转；底部页码输入后按 Enter。支持单页/双页、左右方向、整页/宽度/原始尺寸、缩放、全屏。
- 打开书籍后从当前阅读位置向后逐页识别，再补齐前面的页面，底部显示进度。每页方向与相邻接缝完成后立即生效，当前画面自动更新；如果结果在绘制期间到达，会在随后补绘，无需点击“应用识别”。扫描结果缓存到 `analysis/`，再次打开复用并续扫未完成页面；阅读设置中可重新分析整本。
- 与 macOS 相同，默认启用积极识别相邻跨页，可关闭以使用更严格的配对判断；普通“双页”布局与智能双图配对分别控制。
- “修正”提供旋转、与下一页配对、交换左右、解除配对、接缝滑块和恢复自动识别。手工确认的双图物理左右顺序不随阅读方向改变。

书库、稳定页面定位、书签、收藏、标签和阅读设置保存到 `%LOCALAPPDATA%\ShuiMan\library.json`；来源目录保存在 `folders.json`，封面在 `covers/` 缓存。写入采用临时文件原子替换并保留 `.bak`；主文件损坏时尝试恢复。进度与书库元数据分别合并，避免另一窗口的旧记录覆盖新编辑。程序不修改原漫画。

## 格式

| 格式 | Windows 行为 |
| --- | --- |
| ZIP、CBZ | 直接读取包内图片，无需解压；支持嵌套目录、UTF-8 / 常见 GB18030 中文文件名、自然数字排序（1、2、10），忽略隐藏元数据。 |
| 图片、图片文件夹 | 支持 PNG、JPEG、GIF、BMP、TIFF、WebP 等图像格式；TIFF 每帧作为独立页面；动画图片按静态页面读取。 |
| EPUB | 按 spine 顺序阅读，保留重复引用和缺失页面位置；图片统一使用原生画布，可旋转和配对；目录映射到阅读位置，不启用网页排版。 |
| PDF | 使用随程序分发的 PDFium 显示，支持密码提示；页面尺寸和顺序保持原文，关闭书籍后释放源文件。 |
| MOBI | 支持无 DRM 的 MOBI 6 图片漫画，以及混合格式中的 MOBI 6 正文；支持未压缩/PalmDOC，保留封面、正文图片顺序和重复引用。 |

加密 ZIP、RAR/CBR、受 DRM 保护的电子书、MOBI HUFF/CDIC、纯 KF8/AZW3，以及 MOBI 文字/混合排版不支持；程序会提示转换为 EPUB 等可读格式。ZIP 不自动展开包内嵌套的 ZIP。危险路径、损坏结构或超过资源限制的文件会被拒绝。图片损坏会保留其页面位置，避免后续进度错位。

智能跨页采用接缝证据，并支持有界的偏移和缩放校正。图像缩小使用面积抗混叠，接缝轮廓使用轻量平滑，减少漫画网点干扰；严格与积极模式保留各自原有的接受阈值。自动方向识别依赖 Windows 可用的 OCR 语言及页面文字线索；竖排文字和低置信度内容不强行旋转，可手动修正。

方向优先级为手动修正、当前页明确的 EPUB 旋转标记、无标记时的自动识别。显式 0° 也视为标记；关闭自动识别不会忽略出版物标记。无标记时以 macOS 的中文字形笔画验证为基础，辅以四方向拉丁文字证据；字少、模糊、特殊字体或没有可用 OCR 语言包时保留原方向，支持手动修正。大图解码最长边限制为 8192 像素，缩放时会按需提高解码尺寸；更大的原图不会被修改。EPUB 面向图片漫画，HTML/CSS 文字排版和脚本不执行。

## 开发和构建

安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，重新打开 PowerShell，在仓库根目录运行：

```powershell
.\scripts\windows-test.ps1
.\scripts\windows-ui-test.ps1
.\scripts\windows-build.ps1
.\scripts\windows-setup-compiler.ps1
.\scripts\windows-installer.ps1
```

构建脚本先运行回归检查，再发布完整自带运行时的 `win-x64` 程序、压缩包和 SHA-256 校验文件：

```text
build/windows/ShuiMan/ShuiMan.exe
build/windows/ShuiMan-Windows-x64.zip
build/windows/ShuiMan-Windows-x64.zip.sha256
build/windows/ShuiMan-Setup-0.7.0-x64.exe
build/windows/ShuiMan-Setup-0.7.0-x64.exe.sha256
```

安装器用 Inno Setup 6.7+ 编译；`windows-setup-compiler.ps1` 下载官方版本并验证签名。可用 `-CompilerPath` 指定已有编译器。`scripts/windows-installer-test.ps1` 在独立 AppId、目录和文件关联下执行安装/升级/卸载验证。

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

`windows-ui-test.ps1` 使用真实 WPF 控件和独立临时书库检查系列与分卷、封面、搜索、元数据、持久化，以及 EPUB 原生页面、修正、页码同步和关闭窗口。运行前关闭相同构建配置的开发程序，避免程序文件被占用；也可用 `-Configuration Debug` 隔离开发检查。

[验证记录](../docs/windows-validation.md)列出本次实际检查与限制。GitHub Actions 的 Windows 工作流执行核心与原生界面检查、构建并验证安装器，上传安装版和便携包；它不自动创建 GitHub Release。

首次使用 Git 可参考 [Windows 提交代码入门](../docs/git-windows.md)，了解本地提交、推送分支和发布程序包的区别。


生成用于界面展示的 10 本原创 ZIP 漫画：

```powershell
.\scripts\windows-showcase.ps1 -Output .\build\windows-showcase
```

截图示例：

![Windows 书库](../docs/images/windows-library-070.png)
