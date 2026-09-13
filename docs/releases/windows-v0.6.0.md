# 水漫 Windows 0.6.0

水漫首次提供原生 Windows 桌面版。可直接阅读 ZIP/CBZ 图片漫画，并在本机保存书库、进度、书签和页面修正。

## 下载与安装

- 下载本页附件 **ShuiMan-Windows-x64.zip**，完整解压后进入 `ShuiMan` 目录，双击 **ShuiMan.exe**。
- 需要安装到当前用户并创建开始菜单、桌面快捷方式时，运行 **Install.cmd**。
- 支持 Windows 10 2004+ / Windows 11 **x64**。便携包自带 .NET，无需另外安装 SDK。
- 复杂 EPUB 页面需要 Microsoft Edge WebView2。安装脚本会检查并在缺少时通过 winget 补装；首次补装需要网络。
- **ShuiMan-Windows-x64.zip.sha256** 用于核对下载完整性。GitHub 的 “Source code” 附件是源码，不能直接作为应用运行。

## 本次新增

- **ZIP/CBZ 直接阅读**：支持多层目录、UTF-8/常见 GB18030 中文文件名、自然数字排序，忽略隐藏元数据；不把漫画解压到用户目录，不修改原文件。
- **Windows 阅读界面**：书库导入与搜索、收藏、已读标记、显示名称修改、缩略图、目录、书签、跳页、全屏、单/双页、阅读方向及缩放。
- **本地格式支持**：图片文件夹与单图、多页 TIFF、PDF、EPUB，以及无 DRM 的 MOBI 6 图片漫画。EPUB 遵循 spine，保留重复引用和缺失页面位置，复杂排版以离线 HTML/CSS 显示。
- **智能大跨页与手动修正**：保守识别横向页和相邻双图，提供旋转、配对、取消配对、交换左右、接缝偏移与缩放修正。确认后的双图位置不因阅读方向变化而颠倒。
- **本地记录保护**：进度、书签、显示设置和修正采用原子保存与备份恢复；多个应用窗口保存不同书籍时不互相覆盖。向图片包插入更早排序的图片后，原有图片定位保持稳定。
- **可复现构建入口**：新增 Windows 构建脚本、生成式测试素材、GitHub Actions 工作流及自带运行时的便携打包。

## 验证

本机实际通过 **38 项核心回归和 12 项 WPF/WebView2 界面集成检查**，覆盖 ZIP/CBZ 顺序与中文路径、多页 TIFF、EPUB 重复/缺失位置、PDF 渲染、MOBI 6、跨页布局、阅读记录、HTML/CSS/图片加载、章节跳转与关闭窗口。另实际安装并运行了便携发布包。

测试使用代码生成的原创素材，不包含用户漫画；通过结果不代表所有真实出版物、OCR 语言或显示缩放比例均已覆盖。[完整验证记录](https://github.com/dashuipaoyihuier/ShuiMan-/blob/windows-v0.6.0/docs/windows-validation.md)

## 已知范围

- 不支持加密 ZIP、分卷 ZIP、RAR/CBR、受 DRM 保护的书籍、纯 KF8/AZW3、HUFF/CDIC，以及 MOBI 文字/混合排版；这些格式需先转换为支持的格式。
- 动画图片按静态首帧阅读；TIFF 多页按独立页面阅读。图片解码最长边限制为 8192 像素，原图保持不变。
- Windows 自动文字转向采用保守的多角度拉丁文字证据；尚未移植 macOS 全部 CJK 字形分析，中文/日文页面可使用出版物线索或手动修正。
- 复杂 EPUB 保留原出版物排版，可缩放和滚动；旋转、双图修正用于原生图片画布。

本次发布的是 Windows 二进制包。macOS、Android 代码保留独立构建入口，已有平台包可在 [此前的 Release](https://github.com/dashuipaoyihuier/ShuiMan-/releases/tag/Comic) 获取。[Windows 使用说明](https://github.com/dashuipaoyihuier/ShuiMan-/blob/windows-v0.6.0/windows/README.md)
