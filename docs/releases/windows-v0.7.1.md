# 水漫 Windows 0.7.1

本次修复打开书籍和跳页时的识别时序：先完成当前页及影响其布局的邻域分析，再首次绘制正文。跳到新位置时，优先处理新位置，无需等旧扫描顺序遍历整本后再返回。

## 阅读变化

- **首次显示使用已识别布局**：当前页的方向与相关接缝结果就绪后再显示正文，首次画面直接采用识别结果；有效缓存直接复用。
- **跳页优先**：跳页会动态提升目标位置及相关邻页的分析优先级。后台继续补齐其余页面，不要求等待全书识别完成。
- **接缝自动调节默认关闭**：智能跨页识别继续开启；自动调整两页之间的偏移和缩放是独立选项，仅在阅读设置中主动开启“接缝自动调节”后生效。手工设置的接缝微调保留，不受该开关影响。
- **保留既有阅读规则**：手动修正优先于明确的 EPUB 旋转标记，缺少标记时才进行自动方向判断。沿用 v4 面积抗混叠接缝分析、系列书库、ZIP/CBZ、原生 EPUB 图片阅读和与 macOS 一致的应用图标。

## 下载与升级

- [Windows 0.7.1 安装包：ShuiMan-Setup-0.7.1-x64.exe](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/windows-v0.7.1/ShuiMan-Setup-0.7.1-x64.exe)
- [Android 1.0.5：Shuiman-1.0.5-android-release.apk](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/windows-v0.7.1/Shuiman-1.0.5-android-release.apk)
- [macOS 0.6.0 Apple Silicon：Shuiman-0.6.0-macOS-arm64.zip](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/windows-v0.7.1/Shuiman-0.6.0-macOS-arm64.zip)

Windows 要求 Windows 10 2004（19041）及更新版本或 Windows 11，x64。关闭水漫后运行新版安装包，覆盖当前用户的原安装位置；书库、进度、手动修正与原漫画保留。安装包内含 .NET 和 PDF 阅读组件，EPUB 漫画使用原生画布，无需浏览器运行时。

Android 与 macOS 复用各自既有版本的原包，本次不宣称这两个平台完成了新构建。

## 验证状态

本轮核心 Release 回归 **75 项通过、0 项失败**。真实 WPF 窗口对一册 226 页匿名样本完成 3 组首次绘制检查：分别仅分析 6、8、15 页时，目标跨页已经完整且正确显示；整本任务当时均未结束，远距离跳页得到优先处理。三组均保持偏移 0、缩放 1，源文件 SHA-256 未改变。

原生界面 Release 检查 **78 项通过、0 项失败**，新增 13 项覆盖首次画面、远跳、取消、接缝默认关闭及菜单开关持久化。使用本轮 0.7.1 自包含程序重新执行的安装器检查 **23 项通过、0 项失败**，确认独立安装、覆盖升级、卸载与原书及书库保留。.NET、PDFium、ImageMagick 与原图标齐全；正式安装包由同一源码打包并在 Release 提供。0.7.0 阶段的检查与取样保留为历史。完整范围见[Windows 验证记录](../windows-validation.md)。

本文件只作为仓库更新记录。GitHub Release 正文留空，附件仅包含上述三个平台的程序包，不附 Windows 便携 ZIP 或独立 SHA 文件；0.7.0 更新记录保留不变。
