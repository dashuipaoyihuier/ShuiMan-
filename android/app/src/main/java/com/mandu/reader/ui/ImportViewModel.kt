package com.mandu.reader.ui

import android.net.Uri
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.mandu.reader.core.BookRecord
import com.mandu.reader.data.BookRepository
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext

data class ImportStatus(
    val running: Boolean = false,
    val message: String = "",
    val added: Int = 0,
    val failed: Int = 0,
    val errors: List<String> = emptyList()
)

/** Picker events are consumed immediately; the retained worker owns completion and cleanup. */
class ImportViewModel : ViewModel() {
    var status by mutableStateOf(ImportStatus())
        private set
    var revision by mutableStateOf(0)
        private set
    var requestedBook by mutableStateOf<BookRecord?>(null)
        private set
    private val serial = Mutex()
    private var activeJob: Job? = null

    fun consumeRequestedBook() { requestedBook = null }
    fun cancel() { activeJob?.cancel() }

    fun files(repository: BookRepository, uris: List<Uri>) = start(repository, uris.distinct(), null)
    fun folder(repository: BookRepository, uri: Uri) = start(repository, emptyList(), uri)

    private fun start(repository: BookRepository, uris: List<Uri>, folder: Uri?) {
        viewModelScope.launch {
            serial.withLock {
                activeJob = currentCoroutineContext()[Job]
                status = ImportStatus(running = true, message = "正在扫描来源…")
                val registered = linkedMapOf<String, BookRecord>()
                suspend fun added(book: BookRecord) = withContext(Dispatchers.Main.immediate) {
                    registered[book.id] = book
                    status = status.copy(added = registered.size)
                    revision++
                }
                suspend fun failed(name: String, error: Exception) = withContext(Dispatchers.Main.immediate) {
                    status = status.copy(
                        failed = status.failed + 1,
                        errors = (status.errors + "$name：${error.message ?: "无法读取"}").takeLast(5)
                    )
                }
                try {
                    if (folder != null) {
                        repository.importTree(folder, indexContents = false, onImported = ::added,
                            onProgress = { message -> withContext(Dispatchers.Main.immediate) {
                                status = status.copy(message = message)
                            } }, onError = ::failed)
                    } else {
                        uris.forEachIndexed { index, uri ->
                            currentCoroutineContext().ensureActive()
                            status = status.copy(message = "加入书库 ${index + 1} / ${uris.size}")
                            try { added(repository.importFile(uri, indexContents = false)) }
                            catch (cancelled: CancellationException) { throw cancelled }
                            catch (error: Exception) { failed(uri.lastPathSegment.orEmpty(), error) }
                        }
                    }
                    // All volumes are now visible and can be opened while covers/indexes catch up.
                    registered.values.forEachIndexed { index, book ->
                        currentCoroutineContext().ensureActive()
                        status = status.copy(message = "补全索引 ${index + 1} / ${registered.size}：${book.title}")
                        try {
                            val indexed = repository.indexBook(book.id)
                            revision++
                            if (uris.size == 1 && folder == null && indexed != null) requestedBook = indexed
                        } catch (cancelled: CancellationException) { throw cancelled }
                        catch (error: Exception) { failed(book.title, error) }
                    }
                    status = status.copy(message = if (registered.isEmpty() && status.failed == 0)
                        "没有找到支持的漫画文件" else "导入完成：${registered.size} 本，${status.failed} 项未完成")
                } catch (cancelled: CancellationException) {
                    status = status.copy(message = "已取消；已加入的 ${registered.size} 本保留在书库，可直接打开")
                    throw cancelled
                } catch (error: Exception) {
                    failed("导入失败", error)
                    status = status.copy(message = "导入已停止，已加入的书籍保留在书库")
                } finally {
                    status = status.copy(running = false)
                    activeJob = null
                    revision++
                }
            }
        }
    }
}
