# 原创回归样本

此目录的图片、PDF 与 EPUB 由 `Sources/ReaderChecks/Fixtures.swift` 生成，执行 `scripts/test.sh` 即可重建。

Git 保存生成器、当前标签和本说明。生成的媒体文件不提交，避免 PDF 创建时间与 ZIP 时间戳使每次测试都产生无关差异。真实漫画仍保留在本机其他位置，不作为公开测试资源。

应用截图使用 `ShowcaseArtwork.swift` 绘制的原创 Quiet Valley 演示册，同样由 `scripts/test.sh` 重建。`Valley/Quiet Valley/Volume 01` 含竖版封面与两张相邻半页；`Volume 02.epub` 含封面及带旋转样式线索的侧转跨页。两张半页交给实际 `PairAnalyzer` 分析，不按文件名或页码强制配对。公开截图保存在 `docs/images/`。

The application screenshots use the original Quiet Valley artwork drawn by `ShowcaseArtwork.swift`, rebuilt with `scripts/test.sh`. `Valley/Quiet Valley/Volume 01` contains a portrait cover and two adjacent half-pages; `Volume 02.epub` contains a cover and a sideways spread with a rotation-style hint. The halves are analyzed by the actual `PairAnalyzer`, without filename- or page-number-based pairing overrides. Public screenshots live in `docs/images/`.

生成内容：六图序列；三页 Sample.pdf；旋转元数据变体 Rotated.pdf；密码为 `comic-test` 的 Protected.pdf；五个 spine 单元的 Sample.epub。labels.json 中的配对与旋转真值仅用于回归和实验评价，不代表跨书泛化能力。

0.2 新增 Styles.epub：14 页原创 EPUB 3 结构/样式用例，覆盖选择器、优先级、important、声明与文档顺序、打印和条件规则，不代表完整 EPUB 3 合规。`SpreadChecks.swift` 另生成内存中的连续纹理与错位真值，用于检查双图识别和画布偏移方向，不作为真实漫画准确率证据。
