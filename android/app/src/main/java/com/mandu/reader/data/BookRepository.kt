package com.mandu.reader.data

import android.content.ContentResolver
import android.content.Context
import android.graphics.Bitmap
import android.net.Uri
import androidx.documentfile.provider.DocumentFile
import com.mandu.reader.core.BookKind
import com.mandu.reader.core.BookRecord
import com.mandu.reader.core.OpenBook
import com.mandu.reader.core.PasswordRequiredException
import com.mandu.reader.core.ReadingState
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import java.io.File
import java.io.FileOutputStream

class BookRepository(context: Context) {
    private val context = context.applicationContext
    private val database = LibraryDatabase(this.context)
    private val sourceCache = SourceCache(this.context)

    suspend fun importFile(uri: Uri, indexContents: Boolean = true): BookRecord = withContext(Dispatchers.IO) {
        sourceCache.persistReadPermission(uri)
        val document = document(uri)
        val localFile = uri.path?.let(::File).takeIf { uri.scheme == ContentResolver.SCHEME_FILE }
        if (localFile?.isDirectory == true || document?.isDirectory == true) {
            throw IllegalArgumentException("请选择文件，目录请使用目录导入。")
        }
        importDocument(uri, localFile?.name ?: document?.name, seriesHint = "", titleOverride = null, indexContents = indexContents)
    }

    suspend fun importTree(
        uri: Uri,
        indexContents: Boolean = true,
        onImported: suspend (BookRecord) -> Unit = {},
        onProgress: suspend (String) -> Unit = {},
        onError: suspend (String, Exception) -> Unit = { _, error -> throw error }
    ): List<BookRecord> = withContext(Dispatchers.IO) {
        sourceCache.persistReadPermission(uri)
        val root = DocumentFile.fromTreeUri(context, uri)
            ?: throw IllegalArgumentException("无法读取所选目录。")
        if (!root.isDirectory) throw IllegalArgumentException("所选来源不是目录。")
        val result = mutableListOf<BookRecord>()
        val visited = mutableSetOf<String>()

        suspend fun walk(directory: DocumentAccess.Entry, parentName: String, depth: Int) {
            currentCoroutineContext().ensureActive()
            if (depth > 64 || !visited.add(directory.uri.toString())) return
            onProgress("扫描文件夹：${directory.name}")
            val children = try { DocumentAccess.children(context, directory.uri) }
                catch (cancelled: CancellationException) { throw cancelled }
                catch (error: Exception) { onError(directory.name, error); return }
            val sorted = children.sortedWith { left, right -> NaturalOrder.compare(left.name, right.name) }
            val bookFiles = sorted.filter { !it.directory && supportedBookName(it.name) }
            val images = sorted.filter { !it.directory && isImageName(it.name) }
            for (file in bookFiles) {
                currentCoroutineContext().ensureActive()
                try {
                    onProgress("加入书库：${file.name}")
                    importDocument(file.uri, file.name, directory.name, null, indexContents)
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (error: Exception) {
                    onError(file.name, error)
                    null
                }?.let { result.add(it); onImported(it) }
            }
            // A mixed directory usually contains loose cover art for its book files; do not create a false extra volume.
            if (images.isNotEmpty() && bookFiles.isEmpty()) {
                try {
                    importDocument(
                        directory.uri,
                        directory.name,
                        parentName,
                        directory.name.takeIf(String::isNotBlank),
                        indexContents
                    )
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (error: Exception) {
                    onError(directory.name, error)
                    null
                }?.let { result.add(it); onImported(it) }
            }
            for (child in sorted.filter { it.directory && !it.name.startsWith('.') && it.name != "__MACOSX" }) {
                walk(child, directory.name, depth + 1)
            }
        }

        walk(DocumentAccess.Entry(root.uri, root.name.orEmpty(), true), "", 0)
        result.distinctBy(BookRecord::id)
    }

    suspend fun books(checkSources: Boolean = true): List<BookRecord> = withContext(Dispatchers.IO) {
        database.books().map { book ->
            if (!checkSources) return@map book
            val available = sourceExists(Uri.parse(book.uri))
            if (book.missing == !available) book else book.copy(missing = !available).also(database::save)
        }
    }

    suspend fun save(book: BookRecord) = withContext(Dispatchers.IO) { database.save(book) }

    suspend fun remove(id: String) = withContext(Dispatchers.IO) {
        // Deliberately leaves both the user's source and recoverable reading state untouched.
        database.remove(id)
    }

    suspend fun open(book: BookRecord, password: String? = null): OpenBook = withContext(Dispatchers.IO) {
        val opened = openInternal(book, password)
        try {
            currentCoroutineContext().ensureActive()
            database.updateIndex(book.id, opened.publication.units.map { it.id }, null, System.currentTimeMillis())
            opened
        } catch (error: Throwable) { opened.close(); throw error }
    }

    suspend fun loadState(id: String): ReadingState = withContext(Dispatchers.IO) { database.loadState(id) }

    suspend fun saveState(id: String, state: ReadingState, completed: Boolean = false) = withContext(Dispatchers.IO) {
        database.saveStateAndProgress(id, state, completed)
    }

    private suspend fun importDocument(
        uri: Uri,
        suppliedName: String?,
        seriesHint: String,
        titleOverride: String?,
        indexContents: Boolean = true
    ): BookRecord {
        val metadata = sourceCache.metadata(uri)
        val name = suppliedName?.takeIf(String::isNotBlank) ?: metadata.name
        val kind = if (metadata.isDirectory) BookKind.IMAGES else detectKind(uri, name)
        val baseTitle = titleOverride ?: name.substringBeforeLast('.', name).ifBlank { "未命名" }
        val guessed = guessSeries(baseTitle, seriesHint)
        val id = sha256(uri.toString())
        val existing = database.book(id)
        var record = BookRecord(
            id = id,
            uri = uri.toString(),
            title = existing?.title ?: baseTitle,
            kind = kind,
            series = existing?.series?.takeIf(String::isNotBlank) ?: guessed.first,
            volume = existing?.volume ?: guessed.second,
            tags = existing?.tags.orEmpty(),
            favorite = existing?.favorite ?: false,
            readStatus = existing?.readStatus ?: "未读",
            lastOpened = existing?.lastOpened ?: 0,
            pageCount = existing?.pageCount ?: 0,
            progress = existing?.progress ?: 0,
            coverPath = existing?.coverPath,
            missing = false
        )
        if (!indexContents) {
            if (existing != null) return existing
            database.save(record)
            return record
        }
        try {
            openInternal(record, null).use { opened ->
                database.replaceIndices(record.id, opened.publication.units.map { it.id })
                val cover = createCover(record.id, opened.publication.revision, opened) ?: record.coverPath
                record = record.copy(
                    title = if (existing == null) opened.publication.title else record.title,
                    pageCount = opened.publication.units.size,
                    coverPath = cover
                )
            }
        } catch (_: PasswordRequiredException) {
            // Encrypted PDFs are valid library records; page count and cover are filled after a successful open.
        }
        database.save(record)
        return record
    }

    private suspend fun openInternal(book: BookRecord, password: String?): OpenBook {
        val job = currentCoroutineContext()
        job.ensureActive()
        val uri = Uri.parse(book.uri)
        val metadata = sourceCache.metadata(uri)
        val revision = when {
            metadata.isDirectory -> directoryRevision(uri) { job.ensureActive() }
            uri.scheme == ContentResolver.SCHEME_FILE || metadata.modified > 0 -> metadata.revision
            else -> sha256("${metadata.revision}:${System.nanoTime()}")
        }
        return when (book.kind) {
            BookKind.PDF -> PdfOpenBook(
                context,
                book.id,
                book.title,
                revision,
                sourceCache.seekable(uri),
                password,
                checkActive = { job.ensureActive() }
            )
            BookKind.EPUB -> {
                val archive = ZipArchive(sourceCache.seekable(uri))
                try {
                    ZipImageOpenBook(book.id, book.title, BookKind.EPUB, revision, archive, EpubParser.parse(archive) { job.ensureActive() })
                } catch (error: Throwable) {
                    archive.close()
                    throw error
                }
            }
            BookKind.CBZ -> {
                val archive = ZipArchive(sourceCache.seekable(uri))
                try {
                    ZipImageOpenBook(book.id, book.title, BookKind.CBZ, revision, archive, checkActive = { job.ensureActive() })
                } catch (error: Throwable) {
                    archive.close()
                    throw error
                }
            }
            BookKind.IMAGES -> {
                val sources = if (metadata.isDirectory) directImages(uri) else listOf(ImageSource(uri, metadata.name))
                if (sources.isEmpty()) throw Exception("目录中没有可阅读的图片。")
                ImageOpenBook(book.id, book.title, revision, context.contentResolver, sources, checkActive = { job.ensureActive() })
            }
        }
    }

    private fun directImages(directoryUri: Uri): List<ImageSource> {
        return DocumentAccess.children(context, directoryUri).filter { !it.directory && isImageName(it.name) }
            .filter { !it.name.startsWith('.') && it.name != "__MACOSX" }
            .sortedWith { left, right -> NaturalOrder.compare(left.name, right.name) }
            .map { ImageSource(it.uri, it.name.ifBlank { "未命名图片" }) }
    }

    /** Enrichment never changes user metadata, reading progress, or resurrects a removed row. */
    suspend fun indexBook(id: String): BookRecord? = withContext(Dispatchers.IO) {
        val book = database.book(id) ?: return@withContext null
        try {
            openInternal(book, null).use { opened ->
                val cover = createCover(book.id, opened.publication.revision, opened)
                currentCoroutineContext().ensureActive()
                database.updateIndex(id, opened.publication.units.map { it.id }, cover)
            }
        } catch (_: PasswordRequiredException) {
            database.book(id)
        }
    }

    private fun directoryRevision(directoryUri: Uri, checkActive: () -> Unit): String {
        var hasUnknownRevision = false
        val snapshot = directImages(directoryUri).joinToString("|") { source ->
            checkActive()
            val metadata = runCatching { sourceCache.metadata(source.uri) }.getOrNull()
            if (metadata == null || metadata.modified <= 0) hasUnknownRevision = true
            "${source.name}:${metadata?.size ?: -1}:${metadata?.modified ?: -1}"
        }
        return sha256(if (hasUnknownRevision) "$snapshot:${System.nanoTime()}" else snapshot)
    }

    private suspend fun detectKind(uri: Uri, name: String): BookKind {
        return when (extension(name)) {
            "epub" -> BookKind.EPUB
            "pdf" -> BookKind.PDF
            "cbz", "zip" -> BookKind.CBZ
            in IMAGE_EXTENSIONS -> BookKind.IMAGES
            else -> {
                val type = context.contentResolver.getType(uri).orEmpty().lowercase()
                when {
                    type == "application/pdf" -> BookKind.PDF
                    type == "application/epub+zip" -> BookKind.EPUB
                    type.startsWith("image/") -> BookKind.IMAGES
                    else -> sniffKind(uri)
                }
            }
        }
    }

    private suspend fun sniffKind(uri: Uri): BookKind {
        val header = context.contentResolver.openInputStream(uri)?.use { input ->
            ByteArray(8).let { bytes -> bytes.copyOf(input.read(bytes).coerceAtLeast(0)) }
        } ?: throw IllegalArgumentException("无法读取文件。")
        if (header.startsWith("%PDF-".toByteArray())) return BookKind.PDF
        if (header.size >= 4 && header[0] == 'P'.code.toByte() && header[1] == 'K'.code.toByte()) {
            val archive = ZipArchive(sourceCache.seekable(uri))
            archive.use {
                return if (it.contains("META-INF/container.xml")) BookKind.EPUB else BookKind.CBZ
            }
        }
        // BitmapFactory/Exif validation occurs while opening and will retain an explicit broken-page slot.
        return BookKind.IMAGES
    }

    private fun sourceExists(uri: Uri): Boolean = when (uri.scheme) {
        ContentResolver.SCHEME_FILE -> uri.path?.let(::File)?.exists() == true
        else -> document(uri)?.exists() == true || runCatching {
            context.contentResolver.openAssetFileDescriptor(uri, "r")?.use { true } ?: false
        }.getOrDefault(false)
    }

    private fun document(uri: Uri): DocumentFile? =
        if (uri.scheme == ContentResolver.SCHEME_FILE) null else
        runCatching { DocumentFile.fromSingleUri(context, uri) }.getOrNull()
            ?: runCatching { DocumentFile.fromTreeUri(context, uri) }.getOrNull()

    private suspend fun createCover(id: String, revision: String, opened: OpenBook): String? {
        val index = opened.publication.units.indexOfFirst { !it.complex && it.error == null && it.imagePath != null }
            .takeIf { it >= 0 } ?: opened.publication.units.indexOfFirst { !it.complex && it.error == null }
        if (index < 0) return null
        val bitmap = try {
            opened.render(index, 512)
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (_: Throwable) {
            return null
        }
        val folder = File(context.cacheDir, "covers").apply { mkdirs() }
        val destination = File(folder, "$id-$revision.png")
        val temporary = File.createTempFile("cover-", ".part", folder)
        try {
            FileOutputStream(temporary).use { output ->
                if (!bitmap.compress(Bitmap.CompressFormat.PNG, 100, output)) return null
            }
            if (!temporary.renameTo(destination)) return null
            return destination.absolutePath
        } finally {
            if (temporary.exists()) temporary.delete()
        }
    }

    private fun guessSeries(title: String, fallback: String): Pair<String, Double?> {
        val patterns = listOf(
            Regex("(?i)(?:第\\s*)?(\\d+(?:\\.\\d+)?)\\s*卷"),
            Regex("(?i)(?:卷|vol(?:ume)?\\.?)[\\s._-]*(\\d+(?:\\.\\d+)?)")
        )
        val match = patterns.firstNotNullOfOrNull { it.find(title) }
        val volume = match?.groupValues?.getOrNull(1)?.toDoubleOrNull()
        val inferred = match?.let { title.removeRange(it.range).trim(' ', '-', '_', '.', '[', ']', '(', ')') }.orEmpty()
        return (inferred.ifBlank { fallback }) to volume
    }

    private fun supportedBookName(name: String): Boolean = extension(name) in setOf("epub", "pdf", "cbz", "zip")

    private fun ByteArray.startsWith(prefix: ByteArray): Boolean =
        size >= prefix.size && prefix.indices.all { this[it] == prefix[it] }
}
