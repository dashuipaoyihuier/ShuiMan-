package com.mandu.reader.data

import android.graphics.Bitmap
import com.mandu.reader.core.BookKind
import com.mandu.reader.core.OpenBook
import com.mandu.reader.core.Publication
import com.mandu.reader.core.ReadingUnit
import com.mandu.reader.core.SourceLocator
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

internal class MobiOpenBook(
    id: String, fallbackTitle: String, revision: String, source: File, checkActive: () -> Unit
) : OpenBook {
    private val parser = MobiParser(source, checkActive)
    override val publication: Publication
    init {
        try {
            val units = parser.pages.map { page ->
                checkActive()
                val info = page.record?.let { runCatching { ImageDecoder.info(parser.record(it)) }.getOrNull() }
                ReadingUnit(
                    locator = SourceLocator(page.record?.let { "mobi:record:$it" } ?: "mobi:missing", page.occurrence),
                    title = if (page.cover) "封面" else "第 ${page.occurrence + 1} 页",
                    width = info?.width ?: 0, height = info?.height ?: 0, isCover = page.cover,
                    error = if (info == null || info.width <= 0 || info.height <= 0) "MOBI 图片记录缺失或不支持。" else null
                )
            }
            publication = Publication(id, parser.title.ifBlank { fallbackTitle }, BookKind.MOBI, units,
                warnings = parser.warnings + if (units.any { it.error != null }) listOf("部分图片缺失或无法解码，已保留原阅读位置。") else emptyList(),
                revision = revision)
        } catch (error: Throwable) { parser.close(); throw error }
    }
    override suspend fun render(index: Int, maxEdge: Int): Bitmap = withContext(Dispatchers.IO) {
        val unit = publication.units.getOrNull(index) ?: throw IndexOutOfBoundsException("页面不存在。")
        unit.error?.let { throw IllegalArgumentException(it) }
        val bytes = parser.record(requireNotNull(parser.pages[index].record))
        ImageDecoder.decode("${publication.id}:${publication.revision}:${unit.id}", maxEdge) { bytes.inputStream() }
    }
    override fun resource(path: String): ByteArray? = null
    override fun close() = parser.close()
}
