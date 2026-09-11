package com.mandu.reader.data

import com.mandu.reader.core.NavigationItem
import com.mandu.reader.core.ReadingDirection
import com.mandu.reader.core.ReadingUnit
import com.mandu.reader.core.SourceLocator
import org.jsoup.Jsoup
import org.jsoup.nodes.Document
import org.jsoup.nodes.Element
import org.jsoup.parser.Parser

internal data class EpubData(
    val title: String,
    val units: List<ReadingUnit>,
    val direction: ReadingDirection?,
    val navigation: List<NavigationItem>,
    val warnings: List<String>
)

internal object EpubParser {
    private const val XML_LIMIT = 16L * 1024 * 1024
    private const val STYLESHEET_TOTAL_LIMIT = 64L * 1024 * 1024
    private data class ManifestItem(
        val id: String,
        val href: String,
        val mediaType: String,
        val properties: Set<String>
    )

    fun parse(archive: ZipArchive, checkActive: () -> Unit = {}): EpubData {
        val container = xml(archive.bytes("META-INF/container.xml", 1024L * 1024))
        val packageReference = elements(container, "rootfile").firstOrNull()?.attr("full-path")
            ?.takeIf(String::isNotBlank) ?: throw ArchiveException("EPUB 缺少包文档入口。")
        val packagePath = ZipArchive.normalize(packageReference)
        val opf = xml(archive.bytes(packagePath, XML_LIMIT))
        val manifestItems = elements(opf, "item").mapNotNull { element ->
            val id = element.attr("id")
            val href = element.attr("href")
            if (id.isBlank() || href.isBlank()) null else ManifestItem(
                id,
                href,
                element.attr("media-type").lowercase(),
                element.attr("properties").split(Regex("\\s+")).filter(String::isNotBlank).toSet()
            )
        }
        if (manifestItems.map { it.id }.toSet().size != manifestItems.size) {
            throw ArchiveException("EPUB manifest 包含重复 id。")
        }
        val manifest = manifestItems.associateBy { it.id }
        val title = elements(opf, "title").firstOrNull()?.text()?.trim().orEmpty()
        val spineElement = elements(opf, "spine").firstOrNull()
        val direction = when (spineElement?.attr("page-progression-direction")?.lowercase()) {
            "rtl" -> ReadingDirection.RTL
            "ltr" -> ReadingDirection.LTR
            else -> null
        }
        val epub2CoverId = elements(opf, "meta").firstOrNull {
            it.attr("name").equals("cover", true)
        }?.attr("content")
        val coverItem = epub2CoverId?.let(manifest::get)
            ?: manifest.values.firstOrNull { "cover-image" in it.properties }
        val coverImagePath = coverItem?.let { runCatching { ZipArchive.resolve(it.href, packagePath) }.getOrNull() }

        val stylesheetText = mutableMapOf<String, String>()
        var stylesheetBytes = 0L
        for (item in manifest.values.filter { it.mediaType == "text/css" }) {
            checkActive()
            val path = runCatching { ZipArchive.resolve(item.href, packagePath) }.getOrNull() ?: continue
            val bytes = runCatching { archive.bytes(path, 2L * 1024 * 1024) }.getOrNull() ?: continue
            stylesheetBytes += bytes.size
            if (stylesheetBytes > STYLESHEET_TOTAL_LIMIT) throw ArchiveException("EPUB 样式资源超过 64 MiB 解析预算。")
            stylesheetText[path] = bytes.toString(Charsets.UTF_8)
        }
        fun stylesheet(path: String): String? {
            stylesheetText[path]?.let { return it }
            val bytes = try { archive.bytes(path, 2L * 1024 * 1024) } catch (_: Exception) { return null }
            stylesheetBytes += bytes.size
            if (stylesheetBytes > STYLESHEET_TOTAL_LIMIT) throw ArchiveException("EPUB 样式资源超过 64 MiB 解析预算。")
            return bytes.toString(Charsets.UTF_8).also { stylesheetText[path] = it }
        }

        val units = mutableListOf<ReadingUnit>()
        elements(opf, "itemref").forEachIndexed { occurrence, itemRef ->
            checkActive()
            if (itemRef.attr("linear").equals("no", true)) return@forEachIndexed
            val idref = itemRef.attr("idref")
            val item = manifest[idref]
            if (item == null) {
                units += ReadingUnit(
                    SourceLocator("missing-spine-$occurrence", occurrence),
                    "缺失页面",
                    error = "spine 引用不存在：${idref.ifBlank { "(空)" }}"
                )
                return@forEachIndexed
            }
            val contentPath = runCatching { ZipArchive.resolve(item.href, packagePath) }.getOrElse {
                units += ReadingUnit(
                    SourceLocator("invalid-spine-$occurrence", occurrence),
                    "缺失页面",
                    error = "正文资源路径无效。"
                )
                return@forEachIndexed
            }
            val locator = SourceLocator(contentPath, occurrence)
            val bytes = runCatching { archive.bytes(contentPath, XML_LIMIT) }.getOrElse {
                units += ReadingUnit(locator, "第 ${units.size + 1} 页", error = "内容文档无法读取。")
                return@forEachIndexed
            }
            val document = html(bytes)
            val css = buildString {
                for (node in document.select("style, link[rel~=(?i)stylesheet]")) {
                    val media = node.attr("media").trim()
                    val sheet = if (node.normalName() == "style") node.data().ifBlank { node.html() }
                    else runCatching { ZipArchive.resolve(node.attr("href"), contentPath) }.getOrNull()?.let(::stylesheet)
                    if (sheet != null) append('\n').append(if (media.isBlank()) sheet else "@media $media {$sheet}")
                }
            }
            val images = document.select("body img[src]")
            val image = images.singleOrNull()
            val imagePath = image?.let { runCatching { ZipArchive.resolve(it.attr("src"), contentPath) }.getOrNull() }
            val hint = image?.let { RotationStyles.hint(css, it) }
            val bodyText = document.body()?.text()?.trim().orEmpty()
            val complexNodes = document.select("svg, canvas, video, audio, iframe, script, object, table, picture").isNotEmpty()
            val inlineStyles = document.select("[style]").joinToString("\n") { it.attr("style") }
            val layoutStyles = "$css\n$inlineStyles"
            val riskyLayout = Regex(
                "position\\s*:\\s*(absolute|fixed)|clip(?:-path)?\\s*:|background(?:-image)?\\s*:.*url\\(|@import",
                RegexOption.IGNORE_CASE
            ).containsMatchIn(layoutStyles)
            val complex = imagePath == null || images.size != 1 || bodyText.isNotEmpty() || complexNodes || riskyLayout
            val isCover = imagePath != null && imagePath == coverImagePath
            val imageBytes = imagePath?.let { path -> runCatching { archive.bytes(path) }.getOrNull() }
            val isTiff = imagePath != null && extension(imagePath) in setOf("tif", "tiff")
            val tiffResult = if (isTiff && imageBytes != null) runCatching {
                TiffDecoder.pages(imageBytes).firstOrNull()
            } else null
            val tiffPage = tiffResult?.getOrNull()
            val dimensions = if (tiffPage != null) ImageInfo(tiffPage.width, tiffPage.height, 1)
            else imageBytes?.let { runCatching { ImageDecoder.info(it) }.getOrNull() }
            val pageTitle = if (isCover) "封面" else document.title().trim().ifBlank { "第 ${units.size + 1} 页" }
            units += ReadingUnit(
                locator = locator,
                title = pageTitle,
                imagePath = imagePath,
                width = dimensions?.width ?: 0,
                height = dimensions?.height ?: 0,
                isCover = isCover,
                rotationHint = hint,
                complex = complex,
                error = when {
                    image != null && imagePath == null -> "页面图片路径无效。"
                    imagePath != null && imageBytes == null -> "页面图片无法读取。"
                    isTiff && tiffPage == null -> tiffResult?.exceptionOrNull()?.message ?: "TIFF 页面无法读取。"
                    tiffPage?.error != null -> tiffPage.error
                    else -> null
                }
            )
        }
        if (units.isEmpty()) throw ArchiveException("这本 EPUB 没有可阅读的正文内容。")

        val navigation = mutableListOf<NavigationItem>()
        val tocItems = manifest.values.filter {
            it.mediaType == "application/x-dtbncx+xml" || "nav" in it.properties
        }
        for (item in tocItems) {
            val tocPath = runCatching { ZipArchive.resolve(item.href, packagePath) }.getOrNull() ?: continue
            val tocBytes = runCatching { archive.bytes(tocPath, 4L * 1024 * 1024) }.getOrNull() ?: continue
            val document = if (item.mediaType == "application/x-dtbncx+xml") xml(tocBytes) else html(tocBytes)
            if (item.mediaType == "application/x-dtbncx+xml") {
                for (point in elements(document, "navpoint")) {
                    val target = elements(point, "content").firstOrNull()?.attr("src").orEmpty()
                    addNavigation(navigation, units, target, tocPath, elements(point, "text").firstOrNull()?.text().orEmpty())
                }
            } else {
                val nav = document.select("nav").firstOrNull {
                    it.attr("epub:type").split(Regex("\\s+")).any { token -> token == "toc" }
                } ?: document.selectFirst("nav")
                nav?.select("a[href]")?.forEach { link ->
                    addNavigation(navigation, units, link.attr("href"), tocPath, link.text())
                }
            }
        }
        val warnings = buildList {
            if (archive.contains("META-INF/encryption.xml")) {
                add("出版物含加密或字体混淆声明；受保护的资源可能无法显示。")
            }
        }
        return EpubData(title, units, direction, navigation, warnings)
    }

    private fun addNavigation(
        output: MutableList<NavigationItem>,
        units: List<ReadingUnit>,
        target: String,
        relativeTo: String,
        title: String
    ) {
        val path = runCatching { ZipArchive.resolve(target, relativeTo) }.getOrNull() ?: return
        val index = units.indexOfFirst { it.locator.resource == path }
        if (index >= 0) output += NavigationItem(title.trim().ifBlank { "第 ${index + 1} 页" }, index, ZipArchive.fragment(target))
    }

    private fun xml(bytes: ByteArray): Document =
        Jsoup.parse(bytes.toString(Charsets.UTF_8), "", Parser.xmlParser())

    private fun html(bytes: ByteArray): Document = Jsoup.parse(bytes.toString(Charsets.UTF_8))

    private fun elements(root: Element, localName: String): List<Element> =
        root.getAllElements().filter { it.normalName().substringAfterLast(':').equals(localName, true) }
}
