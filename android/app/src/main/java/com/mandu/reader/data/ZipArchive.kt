package com.mandu.reader.data

import java.io.ByteArrayOutputStream
import java.io.Closeable
import java.io.File
import java.net.URLDecoder
import java.nio.charset.StandardCharsets
import java.util.zip.ZipEntry
import java.util.zip.ZipFile

internal class ArchiveException(message: String) : Exception(message)

/** A bounded, exact-name view of an EPUB/CBZ. It never expands the archive to disk. */
internal class ZipArchive(file: File) : Closeable {
    companion object {
        const val MAX_ENTRIES = 100_000
        const val MAX_RESOURCE_BYTES = 256L * 1024 * 1024
        const val MAX_TOTAL_BYTES = 8L * 1024 * 1024 * 1024
        private const val MAX_COMPRESSION_RATIO = 1_000L

        fun normalize(path: String): String {
            if (path.startsWith('/') || '\\' in path || '\u0000' in path) {
                throw ArchiveException("无效归档路径。")
            }
            val stack = ArrayDeque<String>()
            for (component in path.split('/')) {
                when (component) {
                    "", "." -> Unit
                    ".." -> if (stack.isEmpty()) {
                        throw ArchiveException("归档路径超出出版物范围。")
                    } else stack.removeLast()
                    else -> stack.addLast(component)
                }
            }
            if (stack.isEmpty()) throw ArchiveException("空资源路径。")
            return stack.joinToString("/")
        }

        fun resolve(reference: String, relativeTo: String): String {
            val withoutFragment = reference.substringBefore('#').substringBefore('?')
            val decoded = try {
                // URLDecoder treats '+' as a form-space, which is wrong for URI paths.
                URLDecoder.decode(withoutFragment.replace("+", "%2B"), StandardCharsets.UTF_8.name())
            } catch (_: IllegalArgumentException) {
                throw ArchiveException("资源路径编码无效。")
            }
            if (decoded.contains(':') || decoded.startsWith('/')) {
                throw ArchiveException("不允许外部资源路径。")
            }
            if (decoded.isEmpty()) return normalize(relativeTo)
            val parent = relativeTo.substringBeforeLast('/', missingDelimiterValue = "")
            return normalize(if (parent.isEmpty()) decoded else "$parent/$decoded")
        }

        fun fragment(reference: String): String? = reference.substringAfter('#', "")
            .takeIf { it.isNotEmpty() }
            ?.let {
                try { URLDecoder.decode(it.replace("+", "%2B"), StandardCharsets.UTF_8.name()) }
                catch (_: IllegalArgumentException) { it }
            }
    }

    private val zip = ZipFile(file)
    private val entries: Map<String, ZipEntry>

    init {
        val found = LinkedHashMap<String, ZipEntry>()
        var total = 0L
        val iterator = zip.entries()
        try {
            while (iterator.hasMoreElements()) {
                val entry = iterator.nextElement()
                if (entry.isDirectory) continue
                if (found.size >= MAX_ENTRIES) throw ArchiveException("压缩包包含过多资源。")
                val path = normalize(entry.name)
                if (found.put(path, entry) != null) throw ArchiveException("压缩包存在重名资源：$path")
                val size = entry.size
                if (size < 0) throw ArchiveException("压缩包资源大小未知：$path")
                total = Math.addExact(total, size)
                if (total > MAX_TOTAL_BYTES) throw ArchiveException("出版物展开体积超过 8 GiB 限制。")
                val compressed = entry.compressedSize
                if (size > 16L * 1024 * 1024 && compressed in 1 until (size / MAX_COMPRESSION_RATIO)) {
                    throw ArchiveException("压缩包资源压缩比异常：$path")
                }
            }
        } catch (error: Throwable) {
            zip.close()
            throw error
        }
        entries = found
    }

    fun contains(path: String): Boolean = entries.containsKey(path)
    fun paths(): List<String> = entries.keys.toList()

    fun bytes(path: String, limit: Long = MAX_RESOURCE_BYTES): ByteArray {
        val normalized = normalize(path)
        val entry = entries[normalized] ?: throw ArchiveException("缺少资源：$normalized")
        if (entry.size > limit) throw ArchiveException("单个资源过大：$normalized")
        zip.getInputStream(entry).use { input ->
            val initial = minOf(entry.size.coerceAtLeast(0), 1024L * 1024).toInt()
            val output = ByteArrayOutputStream(initial)
            val buffer = ByteArray(64 * 1024)
            var count = 0L
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                count += read
                if (count > limit || count > entry.size) {
                    throw ArchiveException("资源展开超出限制：$normalized")
                }
                output.write(buffer, 0, read)
            }
            if (count != entry.size) throw ArchiveException("资源长度校验失败：$normalized")
            return output.toByteArray()
        }
    }

    override fun close() = zip.close()
}
