package com.mandu.reader.data

import android.content.ContentResolver
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.OpenableColumns
import androidx.documentfile.provider.DocumentFile
import java.io.File
import java.io.FileOutputStream
import java.security.MessageDigest
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive

internal data class SourceMetadata(
    val name: String,
    val size: Long,
    val modified: Long,
    val isDirectory: Boolean
) {
    val revision: String get() = sha256("$size:$modified:$name")
}

internal class SourceCache(private val context: Context) {
    companion object {
        private const val CACHE_BUDGET = 2L * 1024 * 1024 * 1024
        private const val COPY_LIMIT = 2L * 1024 * 1024 * 1024
    }

    private val resolver = context.contentResolver
    private val directory = File(context.cacheDir, "seekable-sources").apply { mkdirs() }

    fun persistReadPermission(uri: Uri) {
        if (uri.scheme != ContentResolver.SCHEME_CONTENT) return
        runCatching {
            resolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
    }

    fun metadata(uri: Uri): SourceMetadata {
        if (uri.scheme == ContentResolver.SCHEME_FILE) {
            val file = File(requireNotNull(uri.path) { "文件 URI 缺少路径。" })
            return SourceMetadata(file.name, file.length(), file.lastModified(), file.isDirectory)
        }
        val resolved = DocumentAccess.documentUri(uri)
        val document = runCatching { DocumentFile.fromSingleUri(context, resolved) }.getOrNull()
            ?: runCatching { DocumentFile.fromTreeUri(context, uri) }.getOrNull()
        var name = document?.name.orEmpty()
        var size = document?.length() ?: 0L
        resolver.query(resolved, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use { cursor ->
            if (cursor.moveToFirst()) {
                cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME).takeIf { it >= 0 }?.let { name = cursor.getString(it) ?: name }
                cursor.getColumnIndex(OpenableColumns.SIZE).takeIf { it >= 0 && !cursor.isNull(it) }?.let { size = cursor.getLong(it) }
            }
        }
        return SourceMetadata(
            name = name.ifBlank { uri.lastPathSegment?.substringAfterLast('/') ?: "未命名" },
            size = size,
            modified = document?.lastModified() ?: 0L,
            isDirectory = document?.isDirectory == true
        )
    }

    suspend fun seekable(uri: Uri): File {
        val job = currentCoroutineContext()
        job.ensureActive()
        if (uri.scheme == ContentResolver.SCHEME_FILE) {
            return File(requireNotNull(uri.path) { "文件 URI 缺少路径。" })
        }
        val metadata = metadata(uri)
        if (metadata.size > COPY_LIMIT) throw Exception("文件超过 2 GiB 本地读取缓存限制。")
        val suffix = extension(metadata.name).takeIf(String::isNotEmpty)?.let { ".$it" }.orEmpty()
        val destination = File(directory, sha256("${uri}|${metadata.revision}") + suffix)
        // Some providers expose neither a trustworthy modification time nor a revision.
        // In that case recopy on open instead of serving a permanently stale seek cache.
        if (destination.isFile && metadata.modified > 0 &&
            (metadata.size <= 0 || destination.length() == metadata.size)) {
            destination.setLastModified(System.currentTimeMillis())
            return destination
        }
        if (destination.exists() && !destination.delete()) {
            throw Exception("无法刷新本地读取缓存。")
        }
        prune(excluding = destination, incoming = metadata.size.coerceAtLeast(0))
        val temporary = File.createTempFile("source-", ".part", directory)
        try {
            resolver.openInputStream(uri).use { input ->
                if (input == null) throw Exception("无法读取来源文件。")
                FileOutputStream(temporary).use { output ->
                    val buffer = ByteArray(128 * 1024)
                    var copied = 0L
                    while (true) {
                        job.ensureActive()
                        val read = input.read(buffer)
                        if (read < 0) break
                        copied += read
                        if (copied > COPY_LIMIT) throw Exception("文件超过 2 GiB 本地读取缓存限制。")
                        output.write(buffer, 0, read)
                    }
                    output.fd.sync()
                }
            }
            if (metadata.size > 0 && temporary.length() != metadata.size) throw Exception("来源文件读取不完整。")
            if (!temporary.renameTo(destination)) throw Exception("无法建立本地读取缓存。")
            prune(excluding = destination, incoming = 0)
            return destination
        } finally {
            if (temporary.exists()) temporary.delete()
        }
    }

    private fun prune(excluding: File, incoming: Long) {
        val files = directory.listFiles()?.filter { it.isFile && it != excluding && !it.name.endsWith(".part") }
            ?.sortedBy { it.lastModified() }.orEmpty()
        var total = files.sumOf(File::length) + if (excluding.exists()) excluding.length() else 0L
        for (file in files) {
            if (total + incoming <= CACHE_BUDGET) break
            val length = file.length()
            if (file.delete()) total -= length
        }
        if (total + incoming > CACHE_BUDGET) throw Exception("缓存空间不足，无法为这本书建立读取缓存。")
    }
}

internal fun sha256(value: String): String = MessageDigest.getInstance("SHA-256")
    .digest(value.toByteArray(Charsets.UTF_8)).joinToString("") { "%02x".format(it) }
