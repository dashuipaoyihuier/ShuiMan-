package com.mandu.reader.ui

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.grid.rememberLazyGridState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.mandu.reader.core.BookRecord
import com.mandu.reader.core.ComicSeries
import com.mandu.reader.core.VolumeCoverSize
import com.mandu.reader.core.VolumeDisplayPreferences
import com.mandu.reader.core.VolumeViewMode
import com.mandu.reader.data.VolumeDisplayStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

@Composable
internal fun SeriesDetail(series: ComicSeries, onBack: () -> Unit, onOpen: (BookRecord) -> Unit,
                          onSave: (BookRecord) -> Unit, onEdit: (BookRecord) -> Unit, modifier: Modifier) {
    val context = LocalContext.current
    val store = remember(context) { VolumeDisplayStore(context) }
    var display by remember { mutableStateOf<VolumeDisplayPreferences?>(null) }
    LaunchedEffect(store) { display = withContext(Dispatchers.IO) { store.load() } }
    // One lazy state and stable book keys retain the browsing anchor across
    // density/mode changes, rotation, and the library's saved reader round trip.
    val scroll = rememberLazyGridState()
    Column(modifier.fillMaxSize().padding(horizontal = 18.dp)) {
        TextButton(onClick = onBack, modifier = Modifier.semantics { contentDescription = "返回漫画库" }) { Text("‹ 我的漫画") }
        Text(series.title, style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.SemiBold,
            maxLines = 2, overflow = TextOverflow.Ellipsis)
        Text("${series.volumes.size} 卷 · 按卷号排序", color = MaterialTheme.colorScheme.onSurfaceVariant)
        series.resume?.let { book ->
            Button(onClick = { onOpen(book) }, modifier = Modifier.padding(top = 8.dp)) {
                Text("继续阅读：${book.title}", maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
        val preferences = display
        if (preferences == null) {
            Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
        } else {
            VolumeDisplayControls(preferences) { display = it; store.save(it) }
            BoxWithConstraints(Modifier.weight(1f)) {
                val list = preferences.mode == VolumeViewMode.LIST
                LazyVerticalGrid(
                    columns = GridCells.Fixed(if (list) 1 else preferences.coverSize.columns(maxWidth.value)),
                    state = scroll,
                    modifier = Modifier.fillMaxSize().semantics { contentDescription = if (list) "分卷列表" else "分卷网格" },
                    contentPadding = PaddingValues(bottom = 16.dp),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                    verticalArrangement = Arrangement.spacedBy(if (list) 8.dp else 14.dp)
                ) {
                    items(series.volumes, key = { it.id }, contentType = { preferences.mode }) { book ->
                        if (list) VolumeRow(book, preferences.coverSize, onOpen, onSave, onEdit)
                        else VolumeTile(book, onOpen, onSave, onEdit)
                    }
                }
            }
        }
    }
}

@Composable
private fun VolumeDisplayControls(value: VolumeDisplayPreferences, onChange: (VolumeDisplayPreferences) -> Unit) {
    var sizesOpen by remember { mutableStateOf(false) }
    Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(vertical = 8.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
        VolumeViewMode.entries.forEach { mode ->
            FilterChip(selected = value.mode == mode, onClick = { onChange(value.copy(mode = mode)) },
                label = { Text(mode.label) }, modifier = Modifier.semantics { contentDescription = "${mode.label}视图" })
        }
        Box {
            TextButton(onClick = { sizesOpen = true }, modifier = Modifier.semantics {
                contentDescription = "封面大小：${value.coverSize.label}"
            }) { Text("封面：${value.coverSize.label} ▾") }
            DropdownMenu(expanded = sizesOpen, onDismissRequest = { sizesOpen = false }) {
                VolumeCoverSize.entries.forEach { size ->
                    DropdownMenuItem(text = { Text("${size.label}封面") },
                        trailingIcon = { if (value.coverSize == size) Text("✓") },
                        onClick = { onChange(value.copy(coverSize = size)); sizesOpen = false })
                }
            }
        }
    }
}

@Composable
private fun VolumeTile(book: BookRecord, onOpen: (BookRecord) -> Unit,
                       onSave: (BookRecord) -> Unit, onEdit: (BookRecord) -> Unit) {
    Column {
        Card(onClick = { onOpen(book) }, modifier = Modifier.fillMaxWidth().aspectRatio(.70f)
            .semantics { contentDescription = "打开${book.title}" }) {
            Box(Modifier.fillMaxSize()) {
                Cover(book, compact = true)
                Surface(Modifier.align(Alignment.TopEnd), shape = RoundedCornerShape(bottomStart = 12.dp),
                    color = MaterialTheme.colorScheme.surface.copy(alpha = .9f)) { VolumeMenu(book, onSave, onEdit) }
            }
        }
        Text(book.volume?.let { "第 ${it.pretty()} 卷" } ?: "未标卷号", Modifier.padding(top = 6.dp),
            style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.SemiBold,
            maxLines = 1, overflow = TextOverflow.Ellipsis)
        Text(book.title, style = MaterialTheme.typography.bodySmall, maxLines = 2, overflow = TextOverflow.Ellipsis)
        Text(volumeStatus(book), color = if (book.missing) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant,
            style = MaterialTheme.typography.labelSmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

@Composable
private fun VolumeRow(book: BookRecord, size: VolumeCoverSize, onOpen: (BookRecord) -> Unit,
                      onSave: (BookRecord) -> Unit, onEdit: (BookRecord) -> Unit) {
    Card(onClick = { onOpen(book) }, modifier = Modifier.fillMaxWidth().semantics { contentDescription = "打开${book.title}" },
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerLow)) {
        Row(Modifier.fillMaxWidth().padding(start = 10.dp, top = 8.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(Modifier.width(size.listWidth.dp).aspectRatio(.70f).clip(RoundedCornerShape(4.dp))) { Cover(book, compact = true) }
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(book.volume?.let { "第 ${it.pretty()} 卷" } ?: "未标卷号", style = MaterialTheme.typography.labelLarge,
                    color = MaterialTheme.colorScheme.primary, fontWeight = FontWeight.SemiBold)
                Text(book.title, maxLines = 2, overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.bodyMedium)
                Text("${book.kind.shortName()} · ${volumeStatus(book)}",
                    color = if (book.missing) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant,
                    style = MaterialTheme.typography.labelSmall)
            }
            VolumeMenu(book, onSave, onEdit)
        }
    }
}

@Composable
private fun VolumeMenu(book: BookRecord, onSave: (BookRecord) -> Unit, onEdit: (BookRecord) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Box {
        IconButton(onClick = { open = true }, modifier = Modifier.semantics { contentDescription = "管理${book.title}" }) { Text("⋮") }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            DropdownMenuItem(text = { Text(if (book.favorite) "取消收藏" else "收藏") },
                onClick = { onSave(book.copy(favorite = !book.favorite)); open = false })
            DropdownMenuItem(text = { Text("整理书籍") }, onClick = { onEdit(book); open = false })
        }
    }
}

private fun volumeStatus(book: BookRecord): String = when {
    book.missing -> "找不到原文件"
    book.pageCount > 0 && book.lastOpened > 0 -> "${book.readStatus} · ${book.progress.coerceIn(0, book.pageCount)} / ${book.pageCount} 页"
    else -> book.readStatus
}.let { if (book.favorite) "★ $it" else it }
