# 水漫 Windows 0.7.2

阅读时按 **↑** 将当前页顺时针旋转 90°，保存为手动修正。连续按四次回到原方向，长按不连续旋转；输入页码和使用下拉项时不会误触发。修正菜单与帮助中显示快捷键。

自动方向判断继续使用既有 OCR 字形方案，手动修正仍优先于出版物方向标记和自动结果。原漫画不修改。

## 下载

- [Windows 0.7.2 安装包](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/windows-v0.7.2/ShuiMan-Setup-0.7.2-x64.exe)
- [Windows 0.7.2 便携包](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/windows-v0.7.2/ShuiMan-Windows-x64.zip)
- [Android 1.0.5](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/windows-v0.7.2/Shuiman-1.0.5-android-release.apk)
- [macOS 0.6.0 Apple Silicon](https://github.com/dashuipaoyihuier/ShuiMan-/releases/download/windows-v0.7.2/Shuiman-0.6.0-macOS-arm64.zip)

Windows 用户关闭水漫后运行新版安装包即可升级，保留书库、进度和手动修正。Android、macOS 复用原发布包，本轮未修改或重新构建。

## 验证

快捷键实现通过核心回归 75 项及真实 WPF 界面检查 84 项；新增检查覆盖旋转角度、持久化、四次旋转恢复画面和页码输入保护。自包含程序与便携包构建通过。安装器使用 0.7.2 程序文件打包；隔离安装、升级至测试标签 0.7.3、卸载和数据保留检查共 23 项通过。

GitHub Release 延续安装包汇总方式，正文留空；更新记录保存在本文件。
