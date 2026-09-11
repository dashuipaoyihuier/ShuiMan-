package com.mandu.reader.ui

import android.net.Uri
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.rememberSaveableStateHolder
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.viewmodel.compose.viewModel
import com.mandu.reader.core.BookRecord
import com.mandu.reader.data.BookRepository
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

@Composable
fun ManduApp(
    repository: BookRepository,
    externalDocument: Uri?,
    pendingFiles: List<Uri>,
    pendingFolder: Uri?,
    onFilesConsumed: () -> Unit,
    onFolderConsumed: () -> Unit,
    onExternalConsumed: () -> Unit,
    onPickFiles: () -> Unit,
    onPickFolder: () -> Unit,
    imports: ImportViewModel = viewModel()
) {
    var books by remember { mutableStateOf<List<BookRecord>>(emptyList()) }
    var activeBookId by rememberSaveable { mutableStateOf<String?>(null) }
    var pendingActiveBook by remember { mutableStateOf<BookRecord?>(null) }
    var loading by remember { mutableStateOf(true) }
    val snackbar = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val libraryState = rememberSaveableStateHolder()

    suspend fun refresh() {
        books = withContext(Dispatchers.IO) { repository.books(checkSources = false) }
        loading = false
    }

    LaunchedEffect(repository, imports.revision) { refresh() }

    LaunchedEffect(pendingFiles) {
        if (pendingFiles.isEmpty()) return@LaunchedEffect
        imports.files(repository, pendingFiles)
        onFilesConsumed()
    }

    LaunchedEffect(pendingFolder) {
        val uri = pendingFolder ?: return@LaunchedEffect
        imports.folder(repository, uri)
        onFolderConsumed()
    }

    LaunchedEffect(externalDocument) {
        val uri = externalDocument ?: return@LaunchedEffect
        imports.files(repository, listOf(uri))
        onExternalConsumed()
    }

    LaunchedEffect(imports.requestedBook) {
        imports.requestedBook?.let { pendingActiveBook = it; activeBookId = it.id }
        imports.consumeRequestedBook()
    }

    val activeBook = books.firstOrNull { it.id == activeBookId } ?: pendingActiveBook?.takeIf { it.id == activeBookId }
    Surface(modifier = Modifier) {
        if (activeBookId != null && activeBook == null) {
            ReaderLoading("正在恢复阅读…") { activeBookId = null }
        } else if (activeBook == null) {
            libraryState.SaveableStateProvider("library") {
            LibraryScreen(
                books = books,
                loading = loading,
                importStatus = imports.status,
                onCancelImport = imports::cancel,
                onOpen = { pendingActiveBook = it; activeBookId = it.id },
                onPickFiles = onPickFiles,
                onPickFolder = onPickFolder,
                onSave = { changed ->
                    scope.launch {
                        runCatching { withContext(Dispatchers.IO) { repository.save(changed) } }
                            .onSuccess { refresh() }
                            .onFailure { it.rethrowCancellation(); snackbar.showSnackbar(it.userMessage("保存失败")) }
                    }
                },
                onRemove = { book ->
                    scope.launch {
                        runCatching { withContext(Dispatchers.IO) { repository.remove(book.id) } }
                            .onSuccess { refresh() }
                            .onFailure { it.rethrowCancellation(); snackbar.showSnackbar(it.userMessage("移除失败")) }
                    }
                },
                snackbarHost = { SnackbarHost(snackbar) }
            )
            }
        } else {
            ReaderScreen(
                book = activeBook!!,
                repository = repository,
                allBooks = books,
                onBack = {
                    activeBookId = null
                    pendingActiveBook = null
                    scope.launch { refresh() }
                },
                onOpenNext = { pendingActiveBook = it; activeBookId = it.id },
                onMessage = { message -> scope.launch { snackbar.showSnackbar(message) } },
                snackbarHost = { SnackbarHost(snackbar) }
            )
        }
    }
}

private fun Throwable.userMessage(prefix: String): String =
    "$prefix：${message?.takeIf(String::isNotBlank) ?: "未知错误"}"

private fun Throwable.rethrowCancellation() {
    if (this is CancellationException) throw this
}
