package com.mandu.reader.data

import org.jsoup.Jsoup
import java.io.Closeable
import java.io.File
import java.io.RandomAccessFile
import java.nio.charset.Charset

/** MOBI 6, including the compatible rendition of hybrid MOBI/KF8 books.
 * Body references, never image record order, define the reading sequence. */
internal class MobiParser(file: File, checkActive: () -> Unit = {}) : Closeable {
    data class Page(val record: Int?, val occurrence: Int, val cover: Boolean)
    private val input = RandomAccessFile(file, "r")
    private var offsets = longArrayOf()
    var title = ""; private set
    var pages = emptyList<Page>(); private set
    val warnings = mutableListOf<String>()

    init {
        try { parse(checkActive) } catch (error: Throwable) { input.close(); throw error }
    }

    private fun parse(checkActive: () -> Unit) {
        requireMobi(input.length() >= 78, "记录头被截断。")
        val pdb = ByteArray(78).also(input::readFully)
        requireMobi(pdb.copyOfRange(60, 68).contentEquals("BOOKMOBI".toByteArray()), "不是 BOOKMOBI 容器。")
        val count = number(pdb, 76, 2)
        requireMobi(count > 1 && 78L + count * 8 <= input.length(), "记录表不完整。")
        offsets = LongArray(count + 1)
        for (index in 0 until count) {
            checkActive()
            val offset = input.readInt().toLong() and 0xffffffffL
            input.skipBytes(4)
            requireMobi(offset >= 78L + count * 8 && offset < input.length() &&
                (index == 0 || offset > offsets[index - 1]), "记录偏移越界或无序。")
            offsets[index] = offset
        }
        offsets[count] = input.length()
        val header = record(0, TEXT_LIMIT)
        val compression = number(header, 0, 2)
        val textLength = number(header, 4, 4)
        val textRecords = number(header, 8, 2)
        require(number(header, 12, 2) == 0) { "此 MOBI 受 DRM 保护，请使用无 DRM 的漫画文件。" }
        requireMobi(header.size >= 116 && header.copyOfRange(16, 20).contentEquals("MOBI".toByteArray()), "缺少 MOBI 头。")
        val length = number(header, 20, 4)
        requireMobi(length >= 100 && length <= header.size - 16, "MOBI 头长度越界。")
        require(number(header, 36, 4) in 1..6) { "当前支持 MOBI 6 及含 MOBI 6 的双格式漫画；纯 KF8/AZW3 请先转换为 EPUB。" }
        require(compression == 1 || compression == 2) { "此 MOBI 使用尚未支持的 HUFF/CDIC 压缩，请先转换为 EPUB。" }
        requireMobi(textLength in 1..TEXT_LIMIT && textRecords in 1 until count, "正文长度或记录数量异常。")
        val imageStart = number(header, 108, 4)
        requireMobi(imageStart > textRecords && imageStart < count, "图片记录起点无效。")
        val encoding = when (number(header, 28, 4)) {
            65001 -> Charsets.UTF_8
            1252 -> Charset.forName("windows-1252")
            else -> throw IllegalArgumentException("暂不支持此 MOBI 的文字编码。")
        }
        val metadata = mutableMapOf<Int, ByteArray>()
        if (length >= 116 && number(header, 128, 4) and 0x40 != 0) {
            val start = 16 + length
            requireMobi(start <= header.size - 12 && header.copyOfRange(start, start + 4).contentEquals("EXTH".toByteArray()), "EXTH 元数据缺失。")
            val size = number(header, start + 4, 4)
            val entries = number(header, start + 8, 4)
            requireMobi(size >= 12 && size <= header.size - start && entries in 0..((size - 12) / 8), "EXTH 长度无效。")
            var cursor = start + 12
            repeat(entries) {
                requireMobi(cursor <= start + size - 8, "EXTH 记录越界。")
                val type = number(header, cursor, 4)
                val bytes = number(header, cursor + 4, 4)
                requireMobi(bytes >= 8 && bytes <= start + size - cursor, "EXTH 记录长度无效。")
                metadata[type] = header.copyOfRange(cursor + 8, cursor + bytes)
                cursor += bytes
            }
        }
        val nameOffset = number(header, 84, 4)
        val nameLength = number(header, 88, 4)
        if (nameOffset >= 0 && nameOffset <= header.size && nameLength in 0..(header.size - nameOffset)) {
            title = String(header, nameOffset, nameLength, encoding)
        }
        metadata[503]?.let { if (it.isNotEmpty()) title = String(it, encoding) }
        metadata[121]?.let {
            if (it.size == 4 && number(it, 0, 4) != -1) warnings += "双格式 MOBI：当前读取兼容正文，未应用 KF8 专属布局。"
        }
        val extra = if (length >= 228) number(header, 240, 4) else 0
        val text = java.io.ByteArrayOutputStream()
        for (index in 1..textRecords) {
            checkActive()
            val bytes = stripTrailer(record(index, TEXT_LIMIT), extra)
            val decoded = if (compression == 2) decompress(bytes) else bytes
            requireMobi(decoded.size <= TEXT_LIMIT - text.size(), "正文解压超过上限。")
            text.write(decoded)
        }
        requireMobi(text.size() >= textLength, "正文解压长度不足。")
        val body = Jsoup.parse(String(text.toByteArray(), 0, textLength, encoding)).body()
        require(body.text().isBlank() && body.select("svg, script, iframe, object").isEmpty()) {
            "此 MOBI 含文字或复杂排版，当前漫画模式不能完整呈现，请转换为 EPUB 阅读。"
        }
        val images = body.select("img")
        requireMobi(images.size in 1..100_000, "没有可阅读的图片正文。")
        val refs = images.map { element ->
            element.attr("recindex").toIntOrNull()?.takeIf { it in 1..count }?.let { imageStart + it - 1 }
        }
        val cover = metadata[201]?.takeIf { it.size == 4 }?.let { number(it, 0, 4) }
            ?.takeIf { it in 0 until count - imageStart }?.let { imageStart + it }
        pages = buildList {
            if (cover != null && cover !in refs) add(Page(cover, -1, true))
            refs.forEachIndexed { index, ref -> add(Page(ref, index, ref != null && ref == cover)) }
        }
        warnings += "MOBI 图片模式按正文图片顺序阅读，不还原 HTML/CSS 拼版。"
    }

    @Synchronized fun record(index: Int, limit: Int = 256 * 1024 * 1024): ByteArray {
        requireMobi(index >= 0 && index < offsets.size - 1, "图片引用越界。")
        val size = offsets[index + 1] - offsets[index]
        requireMobi(size in 1..limit.toLong(), "资源大小超过上限。")
        input.seek(offsets[index])
        return ByteArray(size.toInt()).also(input::readFully)
    }
    @Synchronized override fun close() = input.close()

    companion object {
        private const val TEXT_LIMIT = 32 * 1024 * 1024
        private fun requireMobi(condition: Boolean, message: String) = require(condition) { "MOBI 文件无效：$message" }
        private fun number(bytes: ByteArray, offset: Int, width: Int): Int {
            requireMobi(offset >= 0 && offset <= bytes.size - width, "记录被截断。")
            var value = 0
            repeat(width) { value = (value shl 8) or (bytes[offset + it].toInt() and 255) }
            return value
        }
        private fun stripTrailer(input: ByteArray, flags: Int): ByteArray {
            var end = input.size
            var bits = flags ushr 1
            while (bits != 0) {
                if (bits and 1 != 0) {
                    var size = 0; var shift = 0; var terminated = false
                    for (index in end - 1 downTo maxOf(0, end - 4)) {
                        val byte = input[index].toInt() and 255
                        size = size or ((byte and 127) shl shift); shift += 7
                        if (byte and 128 != 0) { terminated = true; break }
                    }
                    requireMobi(terminated && size in 1..end, "正文尾部索引损坏。")
                    end -= size
                }
                bits = bits ushr 1
            }
            if (flags and 1 != 0) {
                requireMobi(end > 0, "多字节尾部被截断。")
                val size = (input[end - 1].toInt() and 3) + 1
                requireMobi(size <= end, "多字节尾部越界。")
                end -= size
            }
            return input.copyOf(end)
        }
        internal fun decompress(input: ByteArray): ByteArray {
            val output = ByteArray(65536)
            var size = 0; var index = 0
            fun append(value: Byte) {
                requireMobi(size < output.size, "PalmDOC 记录解压超过上限。")
                output[size++] = value
            }
            while (index < input.size) {
                val byte = input[index++].toInt() and 255
                when {
                    byte in 1..8 -> {
                        requireMobi(byte <= input.size - index, "PalmDOC 字面量被截断。")
                        repeat(byte) { append(input[index++]) }
                    }
                    byte < 128 -> append(byte.toByte())
                    byte >= 192 -> { append(32); append((byte xor 128).toByte()) }
                    else -> {
                        requireMobi(index < input.size, "PalmDOC 回引用被截断。")
                        val value = (byte shl 8) or (input[index++].toInt() and 255)
                        val distance = (value ushr 3) and 2047
                        requireMobi(distance in 1..size, "PalmDOC 回引用越界。")
                        repeat((value and 7) + 3) { append(output[size - distance]) }
                    }
                }
            }
            return output.copyOf(size)
        }
    }
}
