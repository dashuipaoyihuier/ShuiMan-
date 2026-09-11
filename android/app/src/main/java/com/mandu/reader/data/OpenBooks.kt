package com.mandu.reader.data

import android.content.ContentResolver
import android.content.Context
import android.graphics.Bitmap
import android.graphics.Color
import android.net.Uri
import android.os.ParcelFileDescriptor
import com.mandu.reader.core.BookKind
import com.mandu.reader.core.NavigationItem
import com.mandu.reader.core.OpenBook
import com.mandu.reader.core.PasswordRequiredException
import com.mandu.reader.core.Publication
import com.mandu.reader.core.ReadingUnit
import com.mandu.reader.core.SourceLocator
import io.legere.pdfiumandroid.PdfDocument
import io.legere.pdfiumandroid.PdfPasswordException
import io.legere.pdfiumandroid.PdfiumCore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.io.InputStream
import kotlin.math.max
import kotlin.math.roundToInt

internal data class ImageSource(val uri: Uri, val name: String)

internal class ImageOpenBook(
    id: String,
    title: String,
    revision: String,
    private val resolver: ContentResolver,
    sources: List<ImageSource>,
    warning: String? = null,
    checkActive: () -> Unit = {}
) : OpenBook {
    private val sourceByUri = sources.associateBy { it.uri.toString() }
    override val publication: Publication

    init {
        val units = sources.flatMapIndexed { sourceIndex, source ->
            checkActive()
            val suffix = extension(source.name)
            if (suffix == "tif" || suffix == "tiff") {
                val pages = runCatching { TiffDecoder.pages(readBounded(resolver, source.uri)) }.getOrElse {
                    listOf(TiffDecoder.Page(0, 0, it.message ?: "TIFF 文件无法读取。"))
                }
                pages.mapIndexed { pageIndex, page ->
                    ReadingUnit(
                        locator = SourceLocator(source.uri.toString(), occurrence = sourceIndex, imageIndex = pageIndex),
                        title = source.name + if (pages.size > 1) " · ${pageIndex + 1}" else "",
                        width = page.width,
                        height = page.height,
                        isCover = sourceIndex == 0 && pageIndex == 0,
                        error = page.error
                    )
                }
            } else {
                val info = runCatching { ImageDecoder.info(resolver, source.uri) }.getOrNull()
                listOf(ReadingUnit(
                    locator = SourceLocator(source.uri.toString(), occurrence = sourceIndex),
                    title = source.name,
                    width = info?.width ?: 0,
                    height = info?.height ?: 0,
                    isCover = sourceIndex == 0,
                    error = if (info == null) "无法识别图片。" else null
                ))
            }
        }
        publication = Publication(
            id = id,
            title = title,
            kind = BookKind.IMAGES,
            units = units,
            warnings = listOfNotNull(warning),
            revision = revision
        )
    }

    override suspend fun render(index: Int, maxEdge: Int): Bitmap = withContext(Dispatchers.IO) {
        val unit = publication.units.getOrNull(index) ?: throw IndexOutOfBoundsException("页面不存在。")
        unit.error?.let { throw Exception(it) }
        val source = sourceByUri[unit.locator.resource] ?: throw Exception("图片来源不可用。")
        if (extension(source.name) in setOf("tif", "tiff")) {
            TiffDecoder.render(readBounded(resolver, source.uri), unit.locator.imageIndex, maxEdge)
        } else ImageDecoder.decode("${publication.id}:${publication.revision}:${unit.id}", maxEdge) {
                resolver.openInputStream(source.uri) ?: throw Exception("无法读取图片。")
            }
    }

    override fun resource(path: String): ByteArray? = null
    override fun close() = Unit
}

internal class ZipImageOpenBook(
    id: String,
    title: String,
    kind: BookKind,
    revision: String,
    private val archive: ZipArchive,
    private val epub: EpubData? = null,
    checkActive: () -> Unit = {}
) : OpenBook {
    override val publication: Publication

    init {
        if (epub != null) {
            publication = Publication(
                id = id,
                title = epub.title.ifBlank { title },
                kind = BookKind.EPUB,
                units = epub.units,
                direction = epub.direction,
                navigation = epub.navigation,
                warnings = epub.warnings,
                revision = revision
            )
        } else {
            val paths = archive.paths().filter(::isImageName).filter { path ->
                path.split('/').none { it == "__MACOSX" || it.startsWith('.') }
            }.sortedWith(NaturalOrder)
            if (paths.isEmpty()) throw ArchiveException("CBZ 中没有可阅读的图片。")
            val units = paths.flatMapIndexed { pathIndex, path ->
                checkActive()
                val isTiff = extension(path) in setOf("tif", "tiff")
                if (isTiff) {
                    val pages = runCatching { TiffDecoder.pages(archive.bytes(path)) }.getOrElse {
                        listOf(TiffDecoder.Page(0, 0, it.message ?: "TIFF 文件无法读取。"))
                    }
                    pages.mapIndexed { pageIndex, page -> ReadingUnit(
                        locator = SourceLocator(path, occurrence = pathIndex, imageIndex = pageIndex),
                        title = path.substringAfterLast('/') + if (pages.size > 1) " · ${pageIndex + 1}" else "",
                        imagePath = path,
                        width = page.width,
                        height = page.height,
                        isCover = pathIndex == 0 && pageIndex == 0,
                        error = page.error
                    ) }
                } else {
                    val info = runCatching { ImageDecoder.info(archive.bytes(path)) }.getOrNull()
                    listOf(ReadingUnit(
                        locator = SourceLocator(path, occurrence = pathIndex),
                        title = path.substringAfterLast('/'),
                        imagePath = path,
                        width = info?.width ?: 0,
                        height = info?.height ?: 0,
                        isCover = pathIndex == 0,
                        error = if (info == null) "图片资源损坏或不受支持。" else null
                    ))
                }
            }
            publication = Publication(id, title, kind, units, warnings = emptyList(), revision = revision)
        }
    }

    override suspend fun render(index: Int, maxEdge: Int): Bitmap = withContext(Dispatchers.IO) {
        val unit = publication.units.getOrNull(index) ?: throw IndexOutOfBoundsException("页面不存在。")
        unit.error?.let { throw Exception(it) }
        if (unit.complex || unit.imagePath == null) throw Exception("此页需要使用 EPUB 原版式阅读。")
        val bytes = archive.bytes(unit.imagePath)
        if (extension(unit.imagePath) in setOf("tif", "tiff")) {
            TiffDecoder.render(bytes, unit.locator.imageIndex, maxEdge)
        } else ImageDecoder.decode("${publication.id}:${publication.revision}:${unit.id}", maxEdge) {
            bytes.inputStream()
        }
    }

    override fun resource(path: String): ByteArray? = runCatching {
        archive.bytes(ZipArchive.normalize(path))
    }.getOrNull()

    override fun close() = archive.close()
}

private fun readBounded(resolver: ContentResolver, uri: Uri, limit: Long = ZipArchive.MAX_RESOURCE_BYTES): ByteArray =
    (resolver.openInputStream(uri) ?: throw Exception("无法读取图片。" )).use { input -> input.readBounded(limit) }

private fun InputStream.readBounded(limit: Long): ByteArray {
    val output = java.io.ByteArrayOutputStream()
    val buffer = ByteArray(64 * 1024)
    var total = 0L
    while (true) {
        val read = read(buffer)
        if (read < 0) return output.toByteArray()
        total += read
        if (total > limit) throw Exception("单个图片资源超过 256 MiB 限制。")
        output.write(buffer, 0, read)
    }
}

internal class PdfOpenBook(
    context: Context,
    id: String,
    fallbackTitle: String,
    revision: String,
    source: File,
    password: String?,
    checkActive: () -> Unit = {}
) : OpenBook {
    private val lock = Any()
    private val core = PdfiumCore(context.applicationContext)
    private val document: PdfDocument
    override val publication: Publication

    init {
        val descriptor = ParcelFileDescriptor.open(source, ParcelFileDescriptor.MODE_READ_ONLY)
        document = try {
            core.newDocument(descriptor, password)
        } catch (_: PdfPasswordException) {
            descriptor.close()
            throw PasswordRequiredException()
        } catch (error: Throwable) {
            descriptor.close()
            if (error.message?.contains("password", ignoreCase = true) == true ||
                error.message?.contains("PASSWORD", ignoreCase = true) == true) {
                throw PasswordRequiredException()
            }
            throw error
        }
        val units = try { synchronized(lock) {
            (0 until document.getPageCount()).map { index ->
                checkActive()
                runCatching {
                    document.openPage(index).use { page ->
                        val crop = page.getPageCropBox()
                        var width = if (crop.width() > 0) crop.width().roundToInt() else page.getPageWidthPoint()
                        var height = if (crop.height() > 0) crop.height().roundToInt() else page.getPageHeightPoint()
                        if (page.getPageRotation() == 1 || page.getPageRotation() == 3) {
                            val swap = width; width = height; height = swap
                        }
                        ReadingUnit(SourceLocator("pdf:$id", index), "第 ${index + 1} 页", width = width, height = height)
                    }
                }.getOrElse {
                    ReadingUnit(SourceLocator("pdf:$id", index), "第 ${index + 1} 页", error = "PDF 页面信息无法读取。")
                }
            }
        } } catch (error: Throwable) { document.close(); throw error }
        if (units.isEmpty()) {
            document.close()
            throw Exception("PDF 没有可阅读的页面。")
        }
        val metadataTitle = runCatching { document.getDocumentMeta().title?.trim().orEmpty() }.getOrDefault("")
        val navigation = runCatching { flattenBookmarks(document.getTableOfContents()) }.getOrDefault(emptyList())
        publication = Publication(
            id = id,
            title = metadataTitle.ifBlank { fallbackTitle },
            kind = BookKind.PDF,
            units = units,
            navigation = navigation,
            revision = revision
        )
    }

    override suspend fun render(index: Int, maxEdge: Int): Bitmap = withContext(Dispatchers.IO) {
        synchronized(lock) {
            val unit = publication.units.getOrNull(index) ?: throw IndexOutOfBoundsException("页面不存在。")
            unit.error?.let { throw Exception(it) }
            document.openPage(index).use { page ->
                val sourceWidth = max(1, unit.width)
                val sourceHeight = max(1, unit.height)
                val edge = maxEdge.coerceIn(64, 8192)
                var scale = edge.toDouble() / max(sourceWidth, sourceHeight)
                var width = (sourceWidth * scale).roundToInt().coerceAtLeast(1)
                var height = (sourceHeight * scale).roundToInt().coerceAtLeast(1)
                val pixels = width.toLong() * height
                if (pixels > 48_000_000L) {
                    scale *= kotlin.math.sqrt(48_000_000.0 / pixels)
                    width = (sourceWidth * scale).roundToInt().coerceAtLeast(1)
                    height = (sourceHeight * scale).roundToInt().coerceAtLeast(1)
                }
                Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888).also { bitmap ->
                    bitmap.eraseColor(Color.WHITE)
                    page.renderPageBitmap(
                        bitmap = bitmap,
                        startX = 0,
                        startY = 0,
                        drawSizeX = width,
                        drawSizeY = height,
                        renderAnnot = true,
                        canvasColor = Color.WHITE,
                        pageBackgroundColor = Color.WHITE
                    )
                }
            }
        }
    }

    override fun resource(path: String): ByteArray? = null
    override fun close() = synchronized(lock) { document.close() }

    private fun flattenBookmarks(bookmarks: List<PdfDocument.Bookmark>): List<NavigationItem> {
        val result = mutableListOf<NavigationItem>()
        fun append(items: List<PdfDocument.Bookmark>) {
            for (bookmark in items) {
                val index = bookmark.pageIdx.toInt()
                if (index in publicationRange()) result += NavigationItem(bookmark.title.orEmpty().ifBlank { "第 ${index + 1} 页" }, index)
                append(bookmark.children)
            }
        }
        append(bookmarks)
        return result
    }

    private fun publicationRange(): IntRange = 0 until document.getPageCount()
}
