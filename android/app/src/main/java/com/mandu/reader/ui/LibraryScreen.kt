package com.mandu.reader.ui

import android.graphics.BitmapFactory
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilterChip
import androidx.compose.material3.IconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationRail
import androidx.compose.material3.NavigationRailItem
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.mandu.reader.core.BookKind
import com.mandu.reader.R
import com.mandu.reader.core.BookRecord
import com.mandu.reader.core.ComicLibrary
import com.mandu.reader.core.ComicSeries
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.text.DateFormat
import java.util.Date

private enum class LibraryTab(val label: String, val icon: Int) {
    READING("阅读", R.drawable.ic_reading), BROWSE("浏览", R.drawable.ic_library),
    TAGS("标签", R.drawable.ic_tag), IMPORT("导入", R.drawable.ic_import)
}

@Composable
fun LibraryScreen(
    books: List<BookRecord>,
    loading: Boolean,
    importStatus: ImportStatus,
    onCancelImport: () -> Unit,
    onOpen: (BookRecord) -> Unit,
    onPickFiles: () -> Unit,
    onPickFolder: () -> Unit,
    onSave: (BookRecord) -> Unit,
    onRemove: (BookRecord) -> Unit,
    snackbarHost: @Composable () -> Unit
) {
    var tab by rememberSaveable { mutableStateOf(LibraryTab.BROWSE) }
    var search by rememberSaveable { mutableStateOf("") }
    var status by rememberSaveable { mutableStateOf("全部") }
    var favoritesOnly by rememberSaveable { mutableStateOf(false) }
    var tag by rememberSaveable { mutableStateOf<String?>(null) }
    var selectedSeries by rememberSaveable { mutableStateOf<String?>(null) }
    var editing by remember { mutableStateOf<BookRecord?>(null) }

    val tags = remember(books) { books.flatMap { it.tags }.distinct().sorted() }
    val filtered = remember(books, search, status, favoritesOnly, tag) {
        books.filter { book ->
            val matchesText = search.isBlank() || listOf(book.title, book.series, book.tags.joinToString()).any {
                it.contains(search.trim(), ignoreCase = true)
            }
            matchesText && (status == "全部" || book.readStatus == status) &&
                (!favoritesOnly || book.favorite) && (tag == null || tag in book.tags)
        }
    }
    val current = books.filter { it.lastOpened > 0 && it.readStatus != "已读" }
        .sortedByDescending { it.lastOpened }
    val series = remember(books) { ComicLibrary.series(books) }
    val detail = series.firstOrNull { it.key == selectedSeries }
    BackHandler(enabled = tab == LibraryTab.BROWSE && selectedSeries != null) { selectedSeries = null }
    LaunchedEffect(loading, books, selectedSeries) {
        if (!loading && selectedSeries != null && detail == null) selectedSeries = null
    }

    BoxWithConstraints(Modifier.fillMaxSize()) {
        val wide = maxWidth >= 720.dp
        Row(Modifier.fillMaxSize()) {
            if (wide) LibraryRail(tab, { tab = it; selectedSeries = null })
            Scaffold(
                modifier = Modifier.weight(1f),
                snackbarHost = snackbarHost,
                bottomBar = { if (!wide) LibraryBar(tab, { tab = it; selectedSeries = null }) }
            ) { padding ->
                when (tab) {
                    LibraryTab.IMPORT -> ImportPane(importStatus, onCancelImport, onPickFiles, onPickFolder, Modifier.padding(padding))
                    LibraryTab.TAGS -> TagPane(tags, tag, { tag = it; tab = LibraryTab.BROWSE; selectedSeries = null }, Modifier.padding(padding))
                    LibraryTab.READING, LibraryTab.BROWSE -> if (tab == LibraryTab.BROWSE && detail != null) {
                        key(detail.key) {
                            SeriesDetail(detail, onBack = { selectedSeries = null }, onOpen = onOpen,
                                onSave = onSave, onEdit = { editing = it }, modifier = Modifier.padding(padding))
                        }
                    } else LibraryContent(
                        title = if (tab == LibraryTab.READING) "继续阅读" else "我的漫画",
                        books = if (tab == LibraryTab.READING && search.isBlank() && status == "全部" && !favoritesOnly && tag == null) current else filtered,
                        total = books.size,
                        seriesCount = series.size,
                        groupSeries = tab == LibraryTab.BROWSE,
                        onSeries = { selectedSeries = it.key },
                        loading = loading,
                        search = search,
                        onSearch = { search = it },
                        status = status,
                        onStatus = { status = it },
                        favoritesOnly = favoritesOnly,
                        onFavoritesOnly = { favoritesOnly = it },
                        tag = tag,
                        onClearTag = { tag = null },
                        onOpen = onOpen,
                        onSave = onSave,
                        onEdit = { editing = it },
                        onImport = { tab = LibraryTab.IMPORT },
                        modifier = Modifier.padding(padding)
                    )
                }
            }
        }
    }

    editing?.let { book ->
        EditBookDialog(
            book = book,
            onDismiss = { editing = null },
            onSave = { onSave(it); editing = null },
            onRemove = { onRemove(book); editing = null }
        )
    }
}

@Composable
private fun LibraryRail(selected: LibraryTab, onSelect: (LibraryTab) -> Unit) {
    NavigationRail(Modifier.fillMaxHeight().width(88.dp)) {
        Spacer(Modifier.height(28.dp))
        Image(painterResource(R.drawable.water_app_icon), null, Modifier.size(42.dp))
        Text(stringResource(R.string.app_name), style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
        Spacer(Modifier.height(20.dp))
        LibraryTab.entries.forEach { item ->
            NavigationRailItem(
                selected = selected == item,
                onClick = { onSelect(item) },
                icon = { Icon(painterResource(item.icon), null, Modifier.size(25.dp)) },
                label = { Text(item.label) }
            )
        }
    }
}

@Composable
private fun LibraryBar(selected: LibraryTab, onSelect: (LibraryTab) -> Unit) {
    NavigationBar {
        LibraryTab.entries.forEach { item ->
            NavigationBarItem(
                selected = selected == item,
                onClick = { onSelect(item) },
                icon = { Icon(painterResource(item.icon), null, Modifier.size(25.dp)) },
                label = { Text(item.label) }
            )
        }
    }
}

@Composable
private fun LibraryContent(
    title: String,
    books: List<BookRecord>,
    total: Int,
    seriesCount: Int,
    groupSeries: Boolean,
    onSeries: (ComicSeries) -> Unit,
    loading: Boolean,
    search: String,
    onSearch: (String) -> Unit,
    status: String,
    onStatus: (String) -> Unit,
    favoritesOnly: Boolean,
    onFavoritesOnly: (Boolean) -> Unit,
    tag: String?,
    onClearTag: () -> Unit,
    onOpen: (BookRecord) -> Unit,
    onSave: (BookRecord) -> Unit,
    onEdit: (BookRecord) -> Unit,
    onImport: () -> Unit,
    modifier: Modifier = Modifier
) {
    Column(modifier.fillMaxSize().padding(horizontal = 18.dp)) {
        Row(
            Modifier.fillMaxWidth().padding(top = 18.dp, bottom = 12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(Modifier.weight(1f)) {
                Text(title, style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.SemiBold)
                Text("$seriesCount 部系列 · $total 卷 · 本机书库", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Button(onClick = onImport) { Text("导入") }
        }
        OutlinedTextField(
            value = search,
            onValueChange = onSearch,
            modifier = Modifier.fillMaxWidth(),
            singleLine = true,
            label = { Text("搜索书名、系列或标签") },
            leadingIcon = { Text("⌕") },
            trailingIcon = { if (search.isNotEmpty()) IconButton(onClick = { onSearch("") }) { Text("×") } }
        )
        Row(
            Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(vertical = 10.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            listOf("全部", "未读", "在读", "已读").forEach { item ->
                FilterChip(selected = status == item, onClick = { onStatus(item) }, label = { Text(item) })
            }
            FilterChip(selected = favoritesOnly, onClick = { onFavoritesOnly(!favoritesOnly) }, label = { Text("★ 收藏") })
            if (tag != null) FilterChip(selected = true, onClick = onClearTag, label = { Text("#$tag ×") })
        }
        when {
            loading -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
            books.isEmpty() -> {
                if (groupSeries && total == 0 && search.isBlank()) WaterCover()
                EmptyLibrary(search.isNotBlank() || status != "全部" || favoritesOnly || tag != null, onImport)
            }
            else -> LazyVerticalGrid(
                columns = GridCells.Adaptive(148.dp),
                modifier = Modifier.fillMaxSize(),
                horizontalArrangement = Arrangement.spacedBy(14.dp),
                verticalArrangement = Arrangement.spacedBy(18.dp)
            ) {
                if (groupSeries && search.isBlank() && status == "全部" && !favoritesOnly && tag == null) {
                    item(key = "water-brand", span = { GridItemSpan(maxLineSpan) }) { WaterCover() }
                }
                if (groupSeries) {
                    items(ComicLibrary.series(books), key = { "series:${it.key}" }) { series ->
                        SeriesCard(series, onSeries)
                    }
                }
                items(if (groupSeries) books.filter { ComicLibrary.seriesKey(it) == null } else books,
                    key = { "book:${it.id}" }) { book ->
                    BookCard(book, onOpen, onSave, onEdit)
                }
            }
        }
    }
}

@Composable
private fun SeriesCard(series: ComicSeries, onOpen: (ComicSeries) -> Unit) {
    Column {
        Card(onClick = { onOpen(series) },
            modifier = Modifier.fillMaxWidth().aspectRatio(.70f).semantics { contentDescription = "查看系列${series.title}" }) {
            Box(Modifier.fillMaxSize()) {
                Cover(series.cover)
                Surface(Modifier.align(Alignment.TopStart).padding(8.dp), shape = RoundedCornerShape(6.dp)) {
                    Text("${series.volumes.size} 卷", Modifier.padding(6.dp), style = MaterialTheme.typography.labelMedium)
                }
            }
        }
        Text(series.title, Modifier.padding(top = 8.dp), fontWeight = FontWeight.SemiBold, maxLines = 2, overflow = TextOverflow.Ellipsis)
        Text("${series.volumes.count { it.readStatus == "已读" }} 卷已读 · 点击查看分卷",
            style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun BookCard(book: BookRecord, onOpen: (BookRecord) -> Unit, onSave: (BookRecord) -> Unit, onEdit: (BookRecord) -> Unit) {
    Column {
        Card(
            onClick = { onOpen(book) },
            modifier = Modifier.fillMaxWidth().aspectRatio(.70f).semantics { contentDescription = "打开${book.title}" },
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant)
        ) {
            Box(Modifier.fillMaxSize()) {
                Cover(book)
                Surface(
                    modifier = Modifier.align(Alignment.TopStart).padding(8.dp),
                    shape = RoundedCornerShape(6.dp),
                    color = MaterialTheme.colorScheme.surface.copy(alpha = .88f)
                ) { Text(book.kind.shortName(), Modifier.padding(horizontal = 6.dp, vertical = 3.dp), style = MaterialTheme.typography.labelSmall) }
                IconButton(
                    onClick = { onSave(book.copy(favorite = !book.favorite)) },
                    modifier = Modifier.align(Alignment.TopEnd)
                ) { Text(if (book.favorite) "★" else "☆", style = MaterialTheme.typography.titleLarge) }
                if (book.missing) {
                    Surface(Modifier.align(Alignment.BottomCenter).fillMaxWidth(), color = MaterialTheme.colorScheme.errorContainer) {
                        Text("找不到原文件", Modifier.padding(7.dp), color = MaterialTheme.colorScheme.onErrorContainer)
                    }
                }
            }
        }
        Row(verticalAlignment = Alignment.Top) {
            Column(Modifier.weight(1f).padding(top = 8.dp)) {
                Text(book.title, maxLines = 2, overflow = TextOverflow.Ellipsis, fontWeight = FontWeight.Medium)
                val line = when {
                    book.series.isNotBlank() && book.volume != null -> "${book.series} · 第 ${book.volume.pretty()} 卷"
                    book.series.isNotBlank() -> book.series
                    book.lastOpened > 0 -> DateFormat.getDateInstance(DateFormat.MEDIUM).format(Date(book.lastOpened))
                    else -> book.readStatus
                }
                Text(line, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1)
            }
            IconButton(onClick = { onEdit(book) }) { Text("⋮") }
        }
        if (book.pageCount > 0) {
            LinearProgressIndicator(
                progress = { (book.progress.toFloat() / book.pageCount).coerceIn(0f, 1f) },
                modifier = Modifier.fillMaxWidth().padding(top = 5.dp)
            )
        }
    }
}

@Composable
internal fun Cover(book: BookRecord, compact: Boolean = false) {
    var bitmap by remember(book.coverPath) { mutableStateOf<android.graphics.Bitmap?>(null) }
    LaunchedEffect(book.coverPath) {
        bitmap = withContext(Dispatchers.IO) {
            runCatching { book.coverPath?.let(BitmapFactory::decodeFile) }.getOrNull()
        }
    }
    val image = bitmap
    if (image != null) {
        Image(image.asImageBitmap(), book.title, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
    } else {
        Box(
            Modifier.fillMaxSize().background(MaterialTheme.colorScheme.secondaryContainer),
            contentAlignment = Alignment.Center
        ) {
            if (compact) {
                Icon(painterResource(R.drawable.ic_reading), null, Modifier.size(24.dp),
                    tint = MaterialTheme.colorScheme.onSecondaryContainer)
            } else Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(18.dp)) {
                Icon(painterResource(R.drawable.ic_reading), null, Modifier.size(48.dp),
                    tint = MaterialTheme.colorScheme.onSecondaryContainer)
                Spacer(Modifier.height(12.dp))
                Text(book.title, maxLines = 3, overflow = TextOverflow.Ellipsis, fontWeight = FontWeight.SemiBold)
            }
        }
    }
}

@Composable
private fun EmptyLibrary(filtered: Boolean, onImport: () -> Unit) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(if (filtered) "没有符合条件的漫画" else "书架还是空的", style = MaterialTheme.typography.titleLarge)
            Spacer(Modifier.height(8.dp))
            Text(if (filtered) "试试清除筛选条件" else "导入本机漫画、PDF 或 EPUB 即可开始", color = MaterialTheme.colorScheme.onSurfaceVariant)
            if (!filtered) { Spacer(Modifier.height(16.dp)); Button(onClick = onImport) { Text("导入漫画") } }
        }
    }
}

@Composable
private fun TagPane(tags: List<String>, selected: String?, onSelect: (String?) -> Unit, modifier: Modifier) {
    Column(modifier.fillMaxSize().padding(24.dp)) {
        Text("标签", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.SemiBold)
        Spacer(Modifier.height(8.dp))
        Text("用标签快速回到想读的系列", color = MaterialTheme.colorScheme.onSurfaceVariant)
        Spacer(Modifier.height(20.dp))
        if (tags.isEmpty()) Text("还没有标签。在书籍菜单中可以添加。")
        else Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            tags.forEach { item -> FilterChip(selected = selected == item, onClick = { onSelect(item) }, label = { Text("#$item") }) }
        }
    }
}

@Composable
private fun ImportPane(status: ImportStatus, onCancel: () -> Unit, onPickFiles: () -> Unit, onPickFolder: () -> Unit, modifier: Modifier) {
    val importing = status.running
    Box(modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(24.dp)) {
            Text("从本机导入", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.SemiBold)
            Spacer(Modifier.height(10.dp))
            Text("支持 EPUB、PDF、CBZ、图片和漫画文件夹。\n原文件不会被修改。", color = MaterialTheme.colorScheme.onSurfaceVariant)
            Spacer(Modifier.height(24.dp))
            Button(onClick = onPickFiles, enabled = !importing, modifier = Modifier.fillMaxWidth(.7f)) { Text("选择文件") }
            Spacer(Modifier.height(12.dp))
            OutlinedButton(onClick = onPickFolder, enabled = !importing, modifier = Modifier.fillMaxWidth(.7f)) { Text("选择文件夹") }
            if (importing) {
                Spacer(Modifier.height(24.dp))
                CircularProgressIndicator(Modifier.size(28.dp))
                Text("已加入 ${status.added} 本 · 可以切到浏览继续阅读", Modifier.padding(top = 8.dp))
                TextButton(onClick = onCancel) { Text("取消导入") }
            }
            if (status.message.isNotEmpty()) Text(status.message, Modifier.padding(top = 8.dp), maxLines = 3, overflow = TextOverflow.Ellipsis)
            status.errors.forEach { Text(it, maxLines = 2, overflow = TextOverflow.Ellipsis, color = MaterialTheme.colorScheme.error) }
        }
    }
}

@Composable
private fun EditBookDialog(book: BookRecord, onDismiss: () -> Unit, onSave: (BookRecord) -> Unit, onRemove: () -> Unit) {
    var title by remember(book.id) { mutableStateOf(book.title) }
    var series by remember(book.id) { mutableStateOf(book.series) }
    var volume by remember(book.id) { mutableStateOf(book.volume?.pretty().orEmpty()) }
    var tags by remember(book.id) { mutableStateOf(book.tags.joinToString("，")) }
    var status by remember(book.id) { mutableStateOf(book.readStatus) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("整理书籍") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(9.dp)) {
                OutlinedTextField(title, { title = it }, label = { Text("书名") }, singleLine = true)
                OutlinedTextField(series, { series = it }, label = { Text("系列") }, singleLine = true)
                OutlinedTextField(volume, { volume = it.filter { c -> c.isDigit() || c == '.' } }, label = { Text("卷号") }, singleLine = true)
                OutlinedTextField(tags, { tags = it }, label = { Text("标签（用逗号分隔）") }, singleLine = true)
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    listOf("未读", "在读", "已读").forEach { item -> FilterChip(selected = status == item, onClick = { status = item }, label = { Text(item) }) }
                }
                TextButton(onClick = onRemove) { Text("从书库移除", color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            Button(
                enabled = title.isNotBlank(),
                onClick = {
                    onSave(
                        book.copy(
                            title = title.trim(), series = series.trim(), volume = volume.toDoubleOrNull(), readStatus = status,
                            tags = tags.split(',', '，').map(String::trim).filter(String::isNotBlank).distinct()
                        )
                    )
                }
            ) { Text("保存") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("取消") } }
    )
}

internal fun BookKind.shortName(): String = when (this) {
    BookKind.EPUB -> "EPUB"
    BookKind.PDF -> "PDF"
    BookKind.IMAGES -> "图片"
    BookKind.CBZ -> "CBZ"
}

internal fun Double.pretty(): String = if (this % 1.0 == 0.0) toInt().toString() else toString()
