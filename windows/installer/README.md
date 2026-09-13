# Windows 安装器

使用 [Inno Setup 6.7+](https://jrsoftware.org/isdl.php) 创建当前用户安装版，包含 `dotnet publish` 的完整离线运行文件。`scripts/windows-setup-compiler.ps1` 从官方 GitHub Release 下载固定的 6.7.3，验证固定 SHA-256 和 Pyrsys B.V. Authenticode 签名后安装到当前用户；CI 可用 `-Destination` 指定工具目录。编译器本机安装位置可以通过 `-CompilerPath` 或 `INNO_SETUP_COMPILER` 指定，脚本也查找标准安装位置。无需 Visual Studio 或付费安装器工具。

先生成版本匹配的自带运行时程序，再打包：

```powershell
.\scripts\windows-setup-compiler.ps1
.\scripts\windows-build.ps1
.\scripts\windows-installer.ps1 -Version 0.7.0
```

产物位于 `build/windows/ShuiMan-Setup-0.7.0-x64.exe` 和同名 `.sha256`。生产打包会拒绝 payload 与安装器版本不一致；`-TestMode` 只生成带“测试”字样、独立身份的测试安装器，不能作为发行文件。

打包直接使用自带运行时的原生程序文件，无需联网下载额外阅读组件。ZIP/CBZ、图片、EPUB 图片漫画、PDF 和支持的旧版 MOBI 使用同一原生阅读画布；EPUB 按出版物顺序提取漫画图片，不嵌入网页排版浏览器。安装器没有外部运行时检测、下载或补装步骤。

稳定 AppId 为 `ShuiMan.Windows.291790F9-329C-44A8-A6C5-83D596049CF4`。默认覆盖原脚本安装位置 `%LOCALAPPDATA%\Programs\ShuiMan`，清理旧版的三个安装脚本。Windows“已安装的应用”提供标准卸载；开始菜单快捷方式始终提供，桌面快捷方式和“打开方式”关联均由用户选择。关联不修改 Windows `UserChoice` 或 `.zip` 默认程序。

升级和卸载都不删除 `%LOCALAPPDATA%\ShuiMan`、原始漫画或安装目录内用户额外添加的文件。卸载仅移除已记录的安装文件、安装器元数据和自己的快捷方式/关联，不卸载电脑上的共享运行环境。

## 隔离验证

```powershell
.\scripts\windows-installer-test.ps1
```

测试使用独立 AppId `ShuiMan.Installer.NativeSmoke.8BB2BDE2`、`build/windows/installer-native-smoke/installed` 目录、`ShuiMan Native Installer Test` 快捷方式和 `.shuiman-native-install-test` 扩展。它实际静默安装 0.7.0、升级测试版至 0.7.1、卸载，并验证已安装应用条目、组件、可选关联和数据保留。0.7.1 仅为升级测试标签，不是应用的发行版本。测试不会启动程序或访问真实用户书库。日志和校验结果保存在同级测试目录；保留的“书籍”是脚本生成的文本占位文件。

若有人使用了隔离目录中的测试应用并更改测试书库，脚本会拒绝覆盖该书库。

2026-09-13 已使用实际的 **0.7.0 原生应用 publish 文件**完成安装器 **23 项检查，0 项失败**，实际执行安装、覆盖升级和卸载。检查包含安装目录无浏览器组件、自带 .NET/PDF 运行文件、Windows 应用登记、可选快捷方式与文件关联、旧脚本安装升级，以及升级和卸载后的原漫画与书库保留。测试包通过 `-TestMode` 使用独立身份，0.7.1 仅是覆盖升级的测试标签；正式安装器从同一原生应用目录打包。

## 第三方来源

Inno Setup 及安装器运行时遵循随目录保存的 `LICENSE-Inno-Setup.txt`，安装时复制至 `licenses`。简体中文翻译原样来自官方项目，版权和维护者信息保留于文件中：

- 源码：[ChineseSimplified.isl](https://github.com/jrsoftware/issrc/blob/6ef32198ef1f7b7b375cd4b6b90896c2a58eb4c2/Files/Languages/ChineseSimplified.isl)
- 版本：提交 `6ef32198ef1f7b7b375cd4b6b90896c2a58eb4c2`
- SHA-256：`e0b0b350e2245f3c5e65586dfe43d574f6e7f06f2261149aba284954b3fc9a8d`

构建默认未配置应用代码签名证书，不能声称 Windows 安装包已由应用发布者签名。
