@file:OptIn(
    androidx.compose.material3.ExperimentalMaterial3Api::class,
    kotlinx.coroutines.FlowPreview::class
)

package com.mandu.reader.ui

import android.graphics.Bitmap
import android.graphics.Matrix
import android.os.Build
import android.view.View
import android.view.WindowInsets
import android.view.WindowInsetsController
import androidx.activity.compose.BackHandler
import androidx.activity.compose.LocalActivity
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.snap
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.calculatePan
import androidx.compose.foundation.gestures.calculateZoom
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
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.requiredSize
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.itemsIndexed as gridItemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.Switch
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.window.Dialog
import com.mandu.reader.core.BookRecord
import com.mandu.reader.core.DisplayGroup
import com.mandu.reader.core.LayoutPlanner
import com.mandu.reader.core.OpenBook
import com.mandu.reader.core.PagePixelSize
import com.mandu.reader.core.PageOverride
import com.mandu.reader.core.PageTurn
import com.mandu.reader.core.PageTurnMode
import com.mandu.reader.core.PairDecision
import com.mandu.reader.core.PasswordRequiredException
import com.mandu.reader.core.ReaderPreferences
import com.mandu.reader.core.ReaderViewportLayout
import com.mandu.reader.core.ReadingDirection
import com.mandu.reader.core.ReadingLayout
import com.mandu.reader.core.ReadingState
import com.mandu.reader.core.ScaleMode
import com.mandu.reader.core.SpreadAnalyzer
import com.mandu.reader.core.SpreadDecision
import com.mandu.reader.core.TapAction
import com.mandu.reader.data.BookRepository
import com.mandu.reader.data.ReadingStateWriter
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.debounce
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.ensureActive
import kotlin.coroutines.coroutineContext
import java.util.LinkedHashMap
import java.util.concurrent.ConcurrentHashMap
import kotlin.math.roundToInt
import kotlin.math.abs

private sealed interface OpenState {
    data object Loading : OpenState
    data class Ready(val book: OpenBook, val state: ReadingState) : OpenState
    data class Failed(val message: String, val password: Boolean = false) : OpenState
}

private object ReadingStateHandoff {
    private val states = ConcurrentHashMap<String, ReadingState>()
    fun get(id: String): ReadingState? = states[id]
    fun put(id: String, state: ReadingState) { states[id] = state }
}

@Composable
fun ReaderScreen(
    book: BookRecord,
    repository: BookRepository,
    allBooks: List<BookRecord>,
    onBack: () -> Unit,
    onOpenNext: (BookRecord) -> Unit,
    onMessage: (String) -> Unit,
    snackbarHost: @Composable () -> Unit
) {
    BackHandler(onBack = onBack)
    var openState by remember(book.id) { mutableStateOf<OpenState>(OpenState.Loading) }
    var retry by remember(book.id) { mutableIntStateOf(0) }
    var password by remember(book.id) { mutableStateOf<String?>(null) }
    var showPassword by remember(book.id) { mutableStateOf(false) }

    LaunchedEffect(book.id, retry, password) {
        openState = OpenState.Loading
        var opened: OpenBook? = null
        openState = try {
            val state = ReadingStateHandoff.get(book.id)
                ?: withContext(Dispatchers.IO) { repository.loadState(book.id) }
            opened = withContext(Dispatchers.IO) { repository.open(book, password) }
            coroutineContext.ensureActive()
            OpenState.Ready(opened, state)
        } catch (error: PasswordRequiredException) {
            showPassword = true
            OpenState.Failed(error.message ?: "这份 PDF 需要密码", password = true)
        } catch (error: Throwable) {
            opened?.close()
            if (error is CancellationException) throw error
            OpenState.Failed(error.message ?: "无法打开这本书")
        }
    }

    DisposableEffect(openState) {
        val opened = (openState as? OpenState.Ready)?.book
        onDispose { opened?.close() }
    }

    when (val result = openState) {
        OpenState.Loading -> ReaderLoading(book.title, onBack)
        is OpenState.Failed -> ReaderFailure(book.title, result.message, onBack) { retry++ }
        is OpenState.Ready -> ReaderSession(book, result.book, result.state, repository, allBooks, onBack, onOpenNext, onMessage, snackbarHost)
    }

    if (showPassword) {
        PasswordDialog(
            onDismiss = { showPassword = false },
            onSubmit = { password = it; showPassword = false; retry++ }
        )
    }
}

@Composable
private fun ReaderSession(
    record: BookRecord,
    openBook: OpenBook,
    restored: ReadingState,
    repository: BookRepository,
    allBooks: List<BookRecord>,
    onBack: () -> Unit,
    onOpenNext: (BookRecord) -> Unit,
    onMessage: (String) -> Unit,
    snackbarHost: @Composable () -> Unit
) {
    val publication = openBook.publication
    val cache = remember(openBook) { BitmapMemoryCache(96 * 1024 * 1024) }
    val decisions = remember(openBook) { mutableStateMapOf<String, SpreadDecision>() }
    val pairs = remember(openBook) { mutableStateMapOf<Int, PairDecision>() }
    var appliedDecisions by remember(openBook) { mutableStateOf<Map<String, SpreadDecision>>(emptyMap()) }
    var appliedPairs by remember(openBook) { mutableStateOf<Map<Int, PairDecision>>(emptyMap()) }
    var state by remember(openBook) {
        mutableStateOf(
            if (record.lastOpened == 0L && restored.locator == null && publication.direction != null)
                restored.copy(preferences = restored.preferences.copy(direction = publication.direction))
            else restored
        )
    }
    var frozenGroups by remember(openBook) { mutableStateOf<List<DisplayGroup>>(emptyList()) }
    var frozenRotations by remember(openBook) { mutableStateOf<Map<String, Int>>(emptyMap()) }
    var groupPosition by remember(openBook) { mutableIntStateOf(0) }
    var scrollJumpGeneration by remember(openBook) { mutableIntStateOf(0) }
    var controlsVisible by remember(openBook) { mutableStateOf(true) }
    // Bumped by every touch interaction so the auto-hide timer restarts.
    var interactionTick by remember(openBook) { mutableIntStateOf(0) }
    // Tracks real travel through the continuous strip so the opening position does not count.
    var lastScrollGroup by remember(openBook) { mutableIntStateOf(-1) }
    var immersive by remember(openBook) { mutableStateOf(false) }
    var showNavigator by remember(openBook) { mutableStateOf(false) }
    var showPreferences by remember(openBook) { mutableStateOf(false) }
    var showCorrections by remember(openBook) { mutableStateOf(false) }
    var reachedEnd by remember(openBook) { mutableStateOf(false) }
    var complexFragment by remember(openBook) { mutableStateOf<String?>(null) }
    var layoutWide by remember(openBook) { mutableStateOf(false) }
    val latestOnMessage by rememberUpdatedState(onMessage)
    val stateWriter = remember(record.id) {
        ReadingStateWriter(repository, record.id) { error ->
            latestOnMessage("阅读进度保存失败：${error.message ?: "未知错误"}")
        }
    }
    val nextVolume = remember(record, allBooks) {
        if (record.series.isBlank()) null else allBooks
            .filter { it.series == record.series && !it.missing && it.id != record.id }
            .filter { candidate -> record.volume == null || candidate.volume?.let { it > record.volume } == true }
            .minByOrNull { it.volume ?: Double.MAX_VALUE }
    }
    val latestState by rememberUpdatedState(state)
    val latestCompleted by rememberUpdatedState(
        frozenGroups.getOrNull(groupPosition)?.indices?.contains(publication.units.lastIndex) == true
    )
    DisposableEffect(stateWriter) {
        onDispose {
            ReadingStateHandoff.put(record.id, latestState)
            stateWriter.enqueue(latestState, latestCompleted)
        }
    }

    fun persist(snapshot: ReadingState, completed: Boolean) {
        ReadingStateHandoff.put(record.id, snapshot)
        stateWriter.enqueue(snapshot, completed)
    }
    fun setAnchor(
        sourceIndex: Int,
        replan: Boolean = true,
        adoptAnalysis: Boolean = true,
        preserveScrollPosition: Boolean = false
    ) {
        val decisionSnapshot = if (replan && adoptAnalysis) decisions.toMap() else appliedDecisions
        val pairSnapshot = if (replan && adoptAnalysis) pairs.toMap() else appliedPairs
        val groups = if (replan) LayoutPlanner.groups(publication, state, layoutWide, decisionSnapshot, pairSnapshot) else frozenGroups
        if (groups.isEmpty()) return
        frozenGroups = groups
        if (replan) {
            appliedDecisions = decisionSnapshot
            appliedPairs = pairSnapshot
            frozenRotations = publication.units.associate { unit ->
                unit.id to LayoutPlanner.rotation(unit, state, decisionSnapshot)
            }
        }
        groupPosition = groups.indexOfFirst { sourceIndex in it.indices }.takeIf { it >= 0 }
            ?: groups.indexOfLast { it.firstSourceIndex <= sourceIndex }.coerceAtLeast(0)
        if (state.preferences.scrollsVertically) scrollJumpGeneration++
        val anchor = groups[groupPosition].firstSourceIndex.coerceIn(publication.units.indices)
        state = state.copy(
            locator = publication.units[anchor].locator,
            scrollOffset = groupPosition,
            scrollFraction = if (state.preferences.scrollsVertically && preserveScrollPosition) state.scrollFraction else 0.0
        )
        complexFragment = null
        persist(state, publication.units.lastIndex in groups[groupPosition].indices)
    }
    fun navigate(delta: Int) {
        if (frozenGroups.isEmpty()) return
        interactionTick++
        val active = frozenGroups[groupPosition.coerceIn(frozenGroups.indices)]
        val target = if (delta > 0) (active.indices.maxOrNull() ?: 0) + 1 else (active.indices.minOrNull() ?: 0) - 1
        if (target !in publication.units.indices) {
            if (delta > 0) reachedEnd = true
            return
        }
        setAnchor(target)
    }
    fun updatePreferences(preferences: ReaderPreferences) {
        val enteringOrLeavingScroll = state.preferences.scrollsVertically != preferences.scrollsVertically
        state = state.copy(
            preferences = preferences,
            scrollFraction = if (enteringOrLeavingScroll) 0.0 else state.scrollFraction
        )
        val anchor = frozenGroups.getOrNull(groupPosition)?.firstSourceIndex ?: 0
        setAnchor(anchor, preserveScrollPosition = !enteringOrLeavingScroll)
    }
    /** A touch page turn is a reading gesture, so the chrome gets out of the way at once. */
    fun turnByTouch(delta: Int) {
        navigate(delta)
        if (state.preferences.autoHideControls) controlsVisible = false
    }
    fun updateOverride(index: Int, transform: (PageOverride) -> PageOverride) {
        val id = publication.units[index].id
        val updated = state.overrides.toMutableMap()
        updated[id] = transform(updated[id] ?: PageOverride())
        state = state.copy(overrides = updated)
        setAnchor(index)
    }

    LaunchedEffect(openBook, layoutWide) {
        if (publication.units.isEmpty()) return@LaunchedEffect
        val restoredIndex = state.locator?.let { locator -> publication.units.indexOfFirst { it.locator == locator } }
            ?.takeIf { it >= 0 } ?: 0
        setAnchor(restoredIndex, adoptAnalysis = false, preserveScrollPosition = true)
    }

    // One serialized, cancellable neighborhood pass. Results are intentionally not
    // applied to frozenGroups until navigation or an explicit manual refresh.
    val analysisAnchor = frozenGroups.getOrNull(groupPosition)?.firstSourceIndex ?: 0
    LaunchedEffect(openBook, analysisAnchor, state.preferences.smartSpreads, state.preferences.automaticPairs) {
        if (!state.preferences.smartSpreads) return@LaunchedEffect
        val order = buildList {
            add(analysisAnchor)
            for (distance in 1..5) add(analysisAnchor + distance)
            add(analysisAnchor - 1)
        }.filter { it in publication.units.indices }.distinct()
        for (index in order) {
            val unitId = publication.units[index].id
            if (unitId !in decisions) runCatching { SpreadAnalyzer.analyze(openBook, index) }
                .onFailure { if (it is CancellationException) throw it }
                .onSuccess { decisions[unitId] = it }
            if (state.preferences.automaticPairs && index < publication.units.lastIndex && index !in pairs) {
                runCatching { SpreadAnalyzer.analyzePair(openBook, index) }
                    .onFailure { if (it is CancellationException) throw it }
                    .onSuccess { pairs[index] = it }
            }
        }
    }

    DisposableEffect(openBook) { onDispose { cache.clear() } }
    ImmersiveEffect(!controlsVisible)

    // Touch reading hides the chrome on its own; opening a sheet or turning a page restarts the wait.
    LaunchedEffect(
        controlsVisible, interactionTick, state.preferences.autoHideControls,
        showNavigator, showPreferences, showCorrections, immersive
    ) {
        if (!controlsVisible || !state.preferences.autoHideControls) return@LaunchedEffect
        if (showNavigator || showPreferences || showCorrections) return@LaunchedEffect
        delay(CONTROLS_AUTO_HIDE_MS)
        controlsVisible = false
    }

    // Capture the entry anchor for one continuous-layout generation. Live scroll updates must
    // not feed back into ScrollReader's restoration effect and repeatedly jump the list.
    val scrollInitialGroup = remember(
        openBook, state.preferences.scrollsVertically, state.preferences.scaleMode, scrollJumpGeneration
    ) {
        groupPosition
    }
    val scrollInitialFraction = remember(
        openBook, state.preferences.scrollsVertically, state.preferences.scaleMode, scrollJumpGeneration
    ) {
        state.scrollFraction
    }

    BoxWithConstraints(Modifier.fillMaxSize().background(Color(0xFF0B0C0E))) {
        val wide = maxWidth >= 700.dp
        LaunchedEffect(wide) { layoutWide = wide }
        Scaffold(
            containerColor = Color.Transparent,
            snackbarHost = snackbarHost,
            topBar = {
                if (controlsVisible) ReaderTopBar(
                    title = publication.title.ifBlank { record.title },
                    preferences = state.preferences,
                    onBack = onBack,
                    onPreferences = { showPreferences = true },
                    onNavigator = { showNavigator = true },
                    onImmersive = { immersive = !immersive; controlsVisible = !immersive }
                )
            },
            bottomBar = {
                if (controlsVisible && frozenGroups.isNotEmpty()) ReaderBottomBar(
                    group = frozenGroups[groupPosition.coerceIn(frozenGroups.indices)],
                    total = publication.units.size,
                    bookmarked = frozenGroups[groupPosition.coerceIn(frozenGroups.indices)].indices.any { idx -> publication.units[idx].locator in state.bookmarks },
                    canBack = groupPosition > 0,
                    // On the final group this remains an enabled completion action.
                    canForward = true,
                    onBack = { navigate(-1) },
                    onForward = { navigate(1) },
                    onJump = { setAnchor(it) },
                    onBookmark = {
                        val locator = publication.units[frozenGroups[groupPosition].firstSourceIndex].locator
                        state = state.copy(bookmarks = if (locator in state.bookmarks) state.bookmarks - locator else state.bookmarks + locator)
                        persist(state, publication.units.lastIndex in frozenGroups[groupPosition].indices)
                    },
                    onCorrect = { showCorrections = true }
                )
            }
        ) { padding ->
            // Continuous content always owns the full, stable viewport; its transient bars overlay
            // it instead of changing LazyColumn measurements and moving the reading anchor.
            val contentModifier = if (state.preferences.scrollsVertically) Modifier.fillMaxSize()
                else Modifier.fillMaxSize().padding(if (controlsVisible) padding else androidx.compose.foundation.layout.PaddingValues())
            if (frozenGroups.isEmpty()) {
                Box(contentModifier, contentAlignment = Alignment.Center) { Text("这本书没有可显示的页面", color = Color.White) }
            } else if (state.preferences.scrollsVertically) {
                ScrollReader(
                    // Chrome overlays the continuous strip, so its geometry remains invariant.
                    edgeToEdge = true,
                    groups = frozenGroups,
                    openBook = openBook,
                    state = state,
                    rotations = frozenRotations,
                    complexFragment = complexFragment,
                    cache = cache,
                    initial = scrollInitialGroup,
                    initialFraction = scrollInitialFraction,
                    onPositionChanged = { position, fraction ->
                        groupPosition = position
                        val anchor = frozenGroups[position].firstSourceIndex
                        state = state.copy(
                            locator = publication.units[anchor].locator,
                            scrollOffset = position,
                            scrollFraction = fraction
                        )
                        ReadingStateHandoff.put(record.id, state)
                    },
                    onVisible = { position, fraction ->
                        groupPosition = position
                        val anchor = frozenGroups[position].firstSourceIndex
                        state = state.copy(
                            locator = publication.units[anchor].locator,
                            scrollOffset = position,
                            scrollFraction = fraction
                        )
                        interactionTick++
                        if (lastScrollGroup != -1 && position != lastScrollGroup &&
                            state.preferences.autoHideControls
                        ) controlsVisible = false
                        lastScrollGroup = position
                        persist(state, publication.units.lastIndex in frozenGroups[position].indices)
                    },
                    onToggleControls = { controlsVisible = !controlsVisible; interactionTick++ },
                    modifier = contentModifier
                )
            } else {
                GroupCanvas(
                    edgeToEdge = !controlsVisible,
                    openBook = openBook,
                    group = frozenGroups[groupPosition.coerceIn(frozenGroups.indices)],
                    state = state,
                    rotations = frozenRotations,
                    complexFragment = complexFragment,
                    cache = cache,
                    onPrevious = { turnByTouch(-1) },
                    onNext = { turnByTouch(1) },
                    onToggleControls = { controlsVisible = !controlsVisible; interactionTick++ },
                    modifier = contentModifier
                )
            }
        }
    }

    if (showNavigator) NavigatorSheet(
        openBook = openBook,
        state = state,
        rotations = frozenRotations,
        cache = cache,
        current = frozenGroups.getOrNull(groupPosition)?.firstSourceIndex ?: 0,
        onDismiss = { showNavigator = false },
        onJump = { index, fragment -> setAnchor(index); complexFragment = fragment; showNavigator = false }
    )
    if (showPreferences) PreferencesSheet(
        preferences = state.preferences,
        onDismiss = { showPreferences = false },
        onChange = ::updatePreferences
    )
    if (showCorrections && frozenGroups.isNotEmpty()) CorrectionSheet(
        group = frozenGroups[groupPosition],
        publicationSize = publication.units.size,
        state = state,
        onDismiss = { showCorrections = false },
        onJoin = {
            val first = frozenGroups[groupPosition].firstSourceIndex
            if (first < publication.units.lastIndex) updateOverride(first) {
                it.copy(joinNext = true, standalone = false, pairingBreak = false, earlierOnRight = state.preferences.direction == ReadingDirection.RTL)
            }
            showCorrections = false
        },
        onCancelPair = {
            val first = frozenGroups[groupPosition].indices.minOrNull() ?: 0
            updateOverride(first) { it.copy(joinNext = false, pairingBreak = true) }
            showCorrections = false
        },
        onSwap = {
            val group = frozenGroups[groupPosition]
            val first = group.indices.minOrNull() ?: 0
            val earlierIsRight = group.indices.indexOf(first) == 1
            val rightScale = group.rightScale.takeIf { it != 0.0 } ?: 1.0
            updateOverride(first) {
                it.copy(
                    earlierOnRight = !(it.earlierOnRight ?: earlierIsRight),
                    joinNext = true,
                    pairingBreak = false,
                    pairOffset = -group.verticalOffset / rightScale,
                    pairScale = 1.0 / rightScale
                )
            }
            showCorrections = false
        },
        onRotate = {
            val index = frozenGroups[groupPosition].firstSourceIndex
            val automatic = decisions[publication.units[index].id]?.rotation ?: publication.units[index].rotationHint ?: 0
            updateOverride(index) { it.copy(rotation = ((it.rotation ?: automatic) + 90) % 360) }
            showCorrections = false
        },
        onStandalone = {
            val index = frozenGroups[groupPosition].firstSourceIndex
            updateOverride(index) { it.copy(standalone = !(it.standalone ?: false), joinNext = false) }
            showCorrections = false
        },
        onReset = {
            val ids = frozenGroups[groupPosition].indices.map { publication.units[it].id }
            state = state.copy(overrides = state.overrides - ids.toSet())
            setAnchor(frozenGroups[groupPosition].firstSourceIndex)
            showCorrections = false
        }
    )
    if (reachedEnd) EndOfBookDialog(nextVolume, { reachedEnd = false }) {
        reachedEnd = false
        onOpenNext(it)
    }
}

@Composable
private fun ReaderTopBar(
    title: String,
    preferences: ReaderPreferences,
    onBack: () -> Unit,
    onPreferences: () -> Unit,
    onNavigator: () -> Unit,
    onImmersive: () -> Unit
) {
    Surface(modifier = Modifier.statusBarsPadding(), color = MaterialTheme.colorScheme.surface.copy(alpha = .96f), tonalElevation = 4.dp) {
        Row(
            Modifier.fillMaxWidth().height(64.dp).padding(horizontal = 8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            IconButton(onClick = onBack, modifier = Modifier.semantics { contentDescription = "返回书库" }) { Text("‹", style = MaterialTheme.typography.headlineMedium) }
            Text(title, Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis, fontWeight = FontWeight.SemiBold)
            IconButton(onClick = onNavigator, modifier = Modifier.semantics { contentDescription = "缩略图和目录" }) { Text("▦", style = MaterialTheme.typography.titleLarge) }
            IconButton(onClick = onPreferences, modifier = Modifier.semantics { contentDescription = "阅读设置" }) { Text("⚙", style = MaterialTheme.typography.titleLarge) }
            IconButton(onClick = onImmersive, modifier = Modifier.semantics { contentDescription = "沉浸阅读" }) { Text("⛶", style = MaterialTheme.typography.titleLarge) }
        }
    }
}

@Composable
private fun ReaderBottomBar(
    group: DisplayGroup,
    total: Int,
    bookmarked: Boolean,
    canBack: Boolean,
    canForward: Boolean,
    onBack: () -> Unit,
    onForward: () -> Unit,
    onJump: (Int) -> Unit,
    onBookmark: () -> Unit,
    onCorrect: () -> Unit
) {
    val first = group.indices.minOrNull() ?: 0
    val last = group.indices.maxOrNull() ?: first
    var sliderPosition by remember(first, total) { mutableFloatStateOf(first.toFloat()) }
    Surface(modifier = Modifier.navigationBarsPadding(), color = MaterialTheme.colorScheme.surface.copy(alpha = .96f), tonalElevation = 4.dp) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 5.dp)) {
            Slider(
                value = sliderPosition,
                onValueChange = { sliderPosition = it },
                onValueChangeFinished = { onJump(sliderPosition.roundToInt().coerceIn(0, (total - 1).coerceAtLeast(0))) },
                valueRange = 0f..(total - 1).coerceAtLeast(0).toFloat(),
                modifier = Modifier.fillMaxWidth()
            )
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                IconButton(onClick = onBookmark, modifier = Modifier.semantics { contentDescription = if (bookmarked) "移除书签" else "添加书签" }) { Text(if (bookmarked) "★" else "☆", style = MaterialTheme.typography.titleLarge) }
                TextButton(onClick = onCorrect) { Text("页面修正") }
                Spacer(Modifier.weight(1f))
                IconButton(onClick = onBack, enabled = canBack, modifier = Modifier.semantics { contentDescription = "上一页" }) { Text("‹", style = MaterialTheme.typography.headlineMedium) }
                Text(if (first == last) "第 ${first + 1} / $total 页" else "第 ${first + 1}–${last + 1} / $total 页")
                IconButton(onClick = onForward, enabled = canForward, modifier = Modifier.semantics { contentDescription = "下一页" }) { Text("›", style = MaterialTheme.typography.headlineMedium) }
                Spacer(Modifier.weight(1f))
                Text(if (group.spread) "完整跨页" else if (group.indices.size == 2) "双页" else "单页", style = MaterialTheme.typography.labelMedium)
            }
        }
    }
}

@Composable
private fun GroupCanvas(
    edgeToEdge: Boolean,
    openBook: OpenBook,
    group: DisplayGroup,
    state: ReadingState,
    rotations: Map<String, Int>,
    complexFragment: String?,
    cache: BitmapMemoryCache,
    onPrevious: () -> Unit,
    onNext: () -> Unit,
    onToggleControls: () -> Unit,
    modifier: Modifier = Modifier
) {
    val units = openBook.publication.units
    val complexIndex = group.indices.singleOrNull()?.takeIf { units[it].complex }
    val mode = state.preferences.pageTurn
    val direction = state.preferences.direction
    val scaleMode = state.preferences.scaleMode
    var zoom by remember(group.indices, scaleMode) { mutableFloatStateOf(1f) }
    var pan by remember(group.indices, scaleMode) { mutableStateOf(Offset.Zero) }
    // Live horizontal offset so a swipe drags the page instead of feeling dead, then eases back.
    var dragTarget by remember(group.indices, scaleMode) { mutableFloatStateOf(0f) }
    var dragging by remember(group.indices, scaleMode) { mutableStateOf(false) }
    val dragOffset by animateFloatAsState(
        targetValue = dragTarget,
        animationSpec = if (dragging) snap() else tween(180),
        label = "pageDragOffset"
    )
    BoxWithConstraints(modifier.background(Color(0xFF0B0C0E))) {
        val density = LocalDensity.current
        val edgeInset = with(density) { if (group.spread) 0f else (if (edgeToEdge) 2.dp else 24.dp).toPx() }
        val normalGap = with(density) { (if (edgeToEdge) 4.dp else 14.dp).toPx() }
        val viewportPlan = ReaderViewportLayout.plan(
            viewportWidth = constraints.maxWidth.coerceAtLeast(1).toDouble(),
            viewportHeight = constraints.maxHeight.coerceAtLeast(1).toDouble(),
            sourceSizes = ReaderViewportLayout.sourceSizes(openBook.publication, group, rotations),
            group = group,
            scaleMode = scaleMode,
            edgeInset = edgeInset.toDouble(),
            normalGap = normalGap.toDouble()
        )
        val contentExtent = Size(
            viewportPlan.geometry.size.width.toFloat(),
            viewportPlan.geometry.size.height.toFloat()
        )
        val canvasWidth = with(density) { contentExtent.width.toDp() }
        val canvasHeight = with(density) { contentExtent.height.toDp() }

        Box(
            Modifier.fillMaxSize().pointerInput(group.indices, mode, direction, scaleMode, contentExtent) {
                fun bounded(candidate: Offset, atZoom: Float): Offset {
                    val maxX = ((contentExtent.width * atZoom - size.width).coerceAtLeast(0f)) / 2f
                    val maxY = ((contentExtent.height * atZoom - size.height).coerceAtLeast(0f)) / 2f
                    return Offset(candidate.x.coerceIn(-maxX, maxX), candidate.y.coerceIn(-maxY, maxY))
                }
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = false)
                    var multiTouch = false
                    var travelled = Offset.Zero
                    var tapCandidate = true
                    var horizontalGesture: Boolean? = null
                    while (true) {
                        val event = awaitPointerEvent()
                        if (event.changes.count { it.pressed } >= 2) multiTouch = true
                        val delta = event.calculatePan()
                        if (multiTouch) {
                            // Pinch/pan always owns the gesture, so it cannot also turn the page.
                            tapCandidate = false
                            val nextZoom = (zoom * event.calculateZoom()).coerceIn(1f, 6f)
                            zoom = nextZoom
                            pan = bounded(pan + delta, nextZoom)
                            event.changes.forEach { if (it.pressed) it.consume() }
                        } else if (delta != Offset.Zero) {
                            travelled += delta
                            if (travelled.getDistance() > viewConfiguration.touchSlop) {
                                tapCandidate = false
                                if (horizontalGesture == null) horizontalGesture = abs(travelled.x) > abs(travelled.y)
                            }
                            val overflowX = contentExtent.width * zoom > size.width + 1f
                            val overflowY = contentExtent.height * zoom > size.height + 1f
                            val gestureCanPan = when (horizontalGesture) {
                                true -> overflowX
                                false -> overflowY
                                null -> false
                            }
                            when {
                                gestureCanPan || zoom > 1.05f -> {
                                    pan = bounded(pan + delta, zoom)
                                    event.changes.forEach { if (it.pressed) it.consume() }
                                }
                                mode == PageTurnMode.SWIPE && horizontalGesture == true -> {
                                    dragging = true
                                    dragTarget += delta.x
                                    event.changes.forEach { if (it.pressed) it.consume() }
                                }
                            }
                        }
                        if (event.changes.none { it.pressed }) break
                    }
                    val width = size.width.toFloat()
                    if (tapCandidate && !multiTouch) {
                        if (zoom > 1.05f) {
                            // A zoomed canvas is a pan surface; one tap restores the fitted page.
                            zoom = 1f
                            pan = Offset.Zero
                        } else if (mode == PageTurnMode.SWIPE) onToggleControls()
                        else when (PageTurn.tapAction(down.position.x, width, direction)) {
                            TapAction.PREVIOUS -> onPrevious()
                            TapAction.NEXT -> onNext()
                            TapAction.TOGGLE_CONTROLS -> onToggleControls()
                        }
                    } else if (mode == PageTurnMode.SWIPE && !multiTouch && zoom <= 1.05f && horizontalGesture == true) {
                        when (PageTurn.swipeAction(dragTarget, width, direction)) {
                            TapAction.PREVIOUS -> onPrevious()
                            TapAction.NEXT -> onNext()
                            else -> Unit
                        }
                        dragging = false
                        dragTarget = 0f
                    } else if (dragTarget != 0f) {
                        dragging = false
                        dragTarget = 0f
                    }
                }
            },
            contentAlignment = Alignment.Center
        ) {
            Box(
                Modifier.requiredSize(canvasWidth, canvasHeight).graphicsLayer {
                    scaleX = zoom
                    scaleY = zoom
                    translationX = pan.x + dragOffset
                    translationY = pan.y
                },
                contentAlignment = Alignment.Center
            ) {
                if (complexIndex != null) {
                    RestrictedBookWebView(openBook, units[complexIndex], complexFragment, Modifier.fillMaxSize().padding(if (edgeToEdge) 2.dp else 8.dp))
                } else {
                    RenderedGroup(openBook, group, state, rotations, cache, Modifier.fillMaxSize(), edgeToEdge = edgeToEdge)
                }
            }
            if (zoom > 1.05f) {
                Surface(
                    modifier = Modifier.align(Alignment.TopCenter).padding(10.dp),
                    shape = RoundedCornerShape(16.dp),
                    color = Color.Black.copy(alpha = .58f)
                ) { Text("${(zoom * 100).roundToInt()}% · 点击还原", Modifier.padding(horizontal = 12.dp, vertical = 6.dp), color = Color.White) }
            }
        }
    }
}

@Composable
private fun RenderedGroup(
    openBook: OpenBook,
    group: DisplayGroup,
    state: ReadingState,
    rotations: Map<String, Int>,
    cache: BitmapMemoryCache,
    modifier: Modifier = Modifier,
    edgeToEdge: Boolean = false
) {
    val sourceSizes = remember(openBook, group.indices, rotations) {
        ReaderViewportLayout.sourceSizes(openBook.publication, group, rotations)
    }
    val maxEdge = remember(sourceSizes, state.preferences.scaleMode) {
        ReaderViewportLayout.decodeEdge(sourceSizes, state.preferences.scaleMode)
    }
    var pages by remember(group.indices, maxEdge, rotations) { mutableStateOf<List<Pair<Int, Bitmap>>?>(null) }
    var error by remember(group.indices, maxEdge, rotations) { mutableStateOf<String?>(null) }
    var retry by remember(group.indices) { mutableIntStateOf(0) }

    LaunchedEffect(openBook, group.indices, maxEdge, retry, rotations) {
        pages = null
        error = null
        try {
            val loaded = withContext(Dispatchers.IO) {
                buildList {
                    for (index in group.indices) {
                        val rotation = rotations[openBook.publication.units[index].id] ?: 0
                        val key = "${openBook.publication.revision}:$index:$maxEdge:$rotation"
                        val bitmap = cache[key] ?: openBook.render(index, maxEdge)
                            .let { source -> rotated(source, rotation) }
                            .also { cache.put(key, it) }
                        add(index to bitmap)
                    }
                }
            }
            pages = loaded // Atomic: the pair: no half-spread is committed.
        } catch (throwable: Throwable) {
            if (throwable is CancellationException) throw throwable
            error = throwable.message ?: "页面解码失败"
        }
    }

    BoxWithConstraints(modifier, contentAlignment = Alignment.Center) {
        when {
            error != null -> PageError(error!!, { retry++ })
            pages == null -> CircularProgressIndicator(color = Color.White)
            else -> {
                val loaded = pages.orEmpty()
                val density = LocalDensity.current
                val edgeInset = with(density) { if (group.spread) 0f else (if (edgeToEdge) 2.dp else 24.dp).toPx() }
                val normalGap = with(density) { (if (edgeToEdge) 4.dp else 14.dp).toPx() }
                val actualSizes = loaded.mapIndexed { position, (_, bitmap) ->
                    val unit = openBook.publication.units[group.indices[position]]
                    if (unit.width > 0 && unit.height > 0) sourceSizes[position]
                    else PagePixelSize(bitmap.width.toDouble(), bitmap.height.coerceAtLeast(1).toDouble())
                }
                val plan = remember(
                    constraints.maxWidth, constraints.maxHeight, actualSizes, group,
                    state.preferences.scaleMode, edgeInset, normalGap
                ) {
                    ReaderViewportLayout.plan(
                        viewportWidth = constraints.maxWidth.coerceAtLeast(1).toDouble(),
                        viewportHeight = constraints.maxHeight.coerceAtLeast(1).toDouble(),
                        sourceSizes = actualSizes,
                        group = group,
                        scaleMode = state.preferences.scaleMode,
                        edgeInset = edgeInset.toDouble(),
                        normalGap = normalGap.toDouble()
                    )
                }
                Layout(
                    modifier = Modifier.fillMaxSize(),
                    content = {
                        loaded.forEach { (index, bitmap) ->
                        Image(
                            bitmap = bitmap.asImageBitmap(),
                            contentDescription = openBook.publication.units[index].title,
                            contentScale = ContentScale.FillBounds
                        )
                        }
                    }
                ) { measurables, constraints ->
                    val viewportWidth = constraints.maxWidth.coerceAtLeast(1)
                    val viewportHeight = constraints.maxHeight.coerceAtLeast(1)
                    val geometry = plan.geometry
                    val placeables = measurables.mapIndexed { index, measurable ->
                        val frame = geometry.frames[index]
                        val shiftX = (viewportWidth - geometry.size.width).div(2.0)
                        val shiftY = (viewportHeight - geometry.size.height).div(2.0)
                        val left = (frame.x + shiftX).roundToInt()
                        val top = (frame.y + shiftY).roundToInt()
                        val right = (frame.right + shiftX).roundToInt()
                        val bottom = (frame.bottom + shiftY).roundToInt()
                        Triple(measurable.measure(Constraints.fixed((right - left).coerceAtLeast(1), (bottom - top).coerceAtLeast(1))), left, top)
                    }
                    layout(viewportWidth, viewportHeight) {
                        placeables.forEach { (placeable, left, top) -> placeable.place(left, top) }
                    }
                }
            }
        }
    }
}

@Composable
private fun ScrollReader(
    edgeToEdge: Boolean,
    groups: List<DisplayGroup>,
    openBook: OpenBook,
    state: ReadingState,
    rotations: Map<String, Int>,
    complexFragment: String?,
    cache: BitmapMemoryCache,
    initial: Int,
    initialFraction: Double,
    onPositionChanged: (Int, Double) -> Unit,
    onVisible: (Int, Double) -> Unit,
    onToggleControls: () -> Unit,
    modifier: Modifier = Modifier
) {
    val listState = rememberLazyListState(initialFirstVisibleItemIndex = initial.coerceIn(0, groups.lastIndex.coerceAtLeast(0)))
    val latestOnVisible by rememberUpdatedState(onVisible)
    var latestAnchor by remember(groups) { mutableStateOf<ScrollAnchor?>(null) }
    var restoring by remember(initial, initialFraction, groups, state.preferences.scaleMode) {
        mutableStateOf(initialFraction > 0.0)
    }

    fun currentAnchor(): ScrollAnchor? {
        val item = listState.layoutInfo.visibleItemsInfo.firstOrNull() ?: return null
        return ScrollAnchor(
            position = item.index.coerceIn(groups.indices),
            fraction = (-item.offset).toDouble().div(item.size.coerceAtLeast(1)).coerceIn(0.0, 1.0)
        )
    }

    LaunchedEffect(initial, initialFraction, groups, state.preferences.scaleMode) {
        if (groups.isEmpty()) return@LaunchedEffect
        val target = initial.coerceIn(groups.indices)
        listState.scrollToItem(target)
        if (initialFraction > 0.0) {
            val size = snapshotFlow {
                listState.layoutInfo.visibleItemsInfo.firstOrNull { it.index == target }?.size
            }.filterNotNull().first()
            listState.scrollToItem(target, (size * initialFraction.coerceIn(0.0, 1.0)).roundToInt())
        }
        restoring = false
    }
    LaunchedEffect(listState, groups, restoring) {
        if (restoring || groups.isEmpty()) return@LaunchedEffect
        snapshotFlow { currentAnchor() }.filterNotNull().distinctUntilChanged().collect {
            latestAnchor = it
            onPositionChanged(it.position, it.fraction)
        }
    }
    LaunchedEffect(listState, groups, restoring) {
        if (restoring || groups.isEmpty()) return@LaunchedEffect
        snapshotFlow { currentAnchor() }.filterNotNull().distinctUntilChanged().debounce(250).collect {
            latestOnVisible(it.position, it.fraction)
        }
    }
    DisposableEffect(listState, groups) {
        onDispose { latestAnchor?.let { latestOnVisible(it.position, it.fraction) } }
    }

    BoxWithConstraints(
        modifier
            .background(Color(0xFF0B0C0E))
            // A bare tap toggles the chrome. This deliberately never consumes the event, so the
            // scrollable below keeps full ownership of vertical drags.
            .pointerInput(groups.size) {
                awaitEachGesture {
                    awaitFirstDown(requireUnconsumed = false)
                    var travelled = Offset.Zero
                    var moved = false
                    while (true) {
                        val event = awaitPointerEvent()
                        travelled += event.calculatePan()
                        if (travelled.getDistance() > viewConfiguration.touchSlop) moved = true
                        if (event.changes.none { it.pressed }) break
                    }
                    if (!moved) onToggleControls()
                }
            }
    ) {
        val density = LocalDensity.current
        val viewportWidthPx = constraints.maxWidth.coerceAtLeast(1).toDouble()
        val viewportHeightPx = constraints.maxHeight.coerceAtLeast(1).toDouble()
        val edgeInset = with(density) { if (edgeToEdge) 2.dp.toPx() else 24.dp.toPx() }.toDouble()
        val normalGap = with(density) { if (edgeToEdge) 4.dp.toPx() else 14.dp.toPx() }.toDouble()
        LazyColumn(
            state = listState,
            modifier = Modifier.fillMaxSize(),
            verticalArrangement = Arrangement.spacedBy(if (edgeToEdge) 4.dp else 10.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            itemsIndexed(groups, key = { _, group -> group.indices.joinToString(":") }) { position, group ->
                val sizes = ReaderViewportLayout.sourceSizes(openBook.publication, group, rotations)
                val plan = ReaderViewportLayout.plan(
                    viewportWidth = viewportWidthPx,
                    viewportHeight = viewportHeightPx,
                    sourceSizes = sizes,
                    group = group,
                    scaleMode = state.preferences.scaleMode,
                    edgeInset = if (group.spread) 0.0 else edgeInset,
                    normalGap = normalGap
                )
                val canvasWidth = with(density) { plan.geometry.size.width.toFloat().toDp() }
                val canvasHeight = with(density) { plan.geometry.size.height.toFloat().toDp() }
                val horizontalState = rememberScrollState()
                var horizontallyCentered by remember(group.indices, state.preferences.scaleMode, canvasWidth) { mutableStateOf(false) }
                LaunchedEffect(horizontalState.maxValue, canvasWidth) {
                    if (!horizontallyCentered && horizontalState.maxValue > 0) {
                        horizontalState.scrollTo(horizontalState.maxValue / 2)
                        horizontallyCentered = true
                    }
                }
                Box(
                    Modifier.fillMaxWidth().height(canvasHeight).horizontalScroll(horizontalState)
                ) {
                    Box(Modifier.width(canvasWidth).height(canvasHeight)) {
                    val complexIndex = group.indices.singleOrNull()?.takeIf { openBook.publication.units[it].complex }
                    if (complexIndex != null) RestrictedBookWebView(
                        openBook, openBook.publication.units[complexIndex],
                        targetFragment = complexFragment.takeIf { position == initial }, modifier = Modifier.fillMaxSize()
                    )
                    else RenderedGroup(openBook, group, state, rotations, cache, Modifier.fillMaxSize(), edgeToEdge = edgeToEdge)
                    }
                }
            }
        }
    }
}

private data class ScrollAnchor(val position: Int, val fraction: Double)

private enum class NavigatorTab(val label: String) { PAGES("缩略图"), CONTENTS("目录"), BOOKMARKS("书签") }

@Composable
private fun NavigatorSheet(
    openBook: OpenBook,
    state: ReadingState,
    rotations: Map<String, Int>,
    cache: BitmapMemoryCache,
    current: Int,
    onDismiss: () -> Unit,
    onJump: (Int, String?) -> Unit
) {
    var tab by remember { mutableStateOf(NavigatorTab.PAGES) }
    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(Modifier.fillMaxWidth().fillMaxHeight(.82f).padding(horizontal = 16.dp)) {
            Text("前往", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.SemiBold)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(vertical = 12.dp)) {
                NavigatorTab.entries.forEach { item ->
                    FilterChip(selected = tab == item, onClick = { tab = item }, label = { Text(item.label) })
                }
            }
            when (tab) {
                NavigatorTab.PAGES -> LazyVerticalGrid(
                    columns = GridCells.Adaptive(92.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    gridItemsIndexed(openBook.publication.units, key = { _, unit -> unit.id }) { index, unit ->
                        Column {
                            Surface(
                                onClick = { onJump(index, null) },
                                modifier = Modifier.fillMaxWidth().aspectRatio(.7f),
                                shape = RoundedCornerShape(8.dp),
                                border = if (index == current) androidx.compose.foundation.BorderStroke(3.dp, MaterialTheme.colorScheme.primary) else null,
                                color = MaterialTheme.colorScheme.surfaceVariant
                            ) {
                                if (unit.complex) Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { Text("原版式", style = MaterialTheme.typography.labelSmall) }
                                else PageThumbnail(openBook, index, rotations[unit.id] ?: 0, cache)
                            }
                            Text("${index + 1}", Modifier.align(Alignment.CenterHorizontally).padding(top = 3.dp), style = MaterialTheme.typography.labelSmall)
                        }
                    }
                }
                NavigatorTab.CONTENTS -> {
                    if (openBook.publication.navigation.isEmpty()) EmptyNavigator("这本书没有提供目录，可用缩略图浏览全部页面。")
                    else LazyColumn {
                        itemsIndexed(openBook.publication.navigation, key = { index, item -> "$index:${item.index}:${item.fragment}" }) { _, item ->
                            Surface(onClick = { onJump(item.index.coerceIn(openBook.publication.units.indices), item.fragment) }, modifier = Modifier.fillMaxWidth()) {
                                Row(Modifier.padding(vertical = 14.dp), verticalAlignment = Alignment.CenterVertically) {
                                    Text(item.title, Modifier.weight(1f), maxLines = 2)
                                    Text("${item.index + 1}", color = MaterialTheme.colorScheme.onSurfaceVariant)
                                }
                            }
                            HorizontalDivider()
                        }
                    }
                }
                NavigatorTab.BOOKMARKS -> {
                    val bookmarked = state.bookmarks.mapNotNull { locator ->
                        openBook.publication.units.indexOfFirst { it.locator == locator }.takeIf { it >= 0 }
                    }
                    if (bookmarked.isEmpty()) EmptyNavigator("还没有书签。阅读时点按底栏的星标即可添加。")
                    else LazyColumn {
                        items(bookmarked.size) { position ->
                            val index = bookmarked[position]
                            Surface(onClick = { onJump(index, null) }, modifier = Modifier.fillMaxWidth()) {
                                Row(Modifier.padding(vertical = 14.dp), verticalAlignment = Alignment.CenterVertically) {
                                    Text("★  ${openBook.publication.units[index].title.ifBlank { "第 ${index + 1} 页" }}", Modifier.weight(1f))
                                    Text("${index + 1}")
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun PageThumbnail(openBook: OpenBook, index: Int, rotation: Int, cache: BitmapMemoryCache) {
    var bitmap by remember(openBook, index, rotation) { mutableStateOf<Bitmap?>(null) }
    var failed by remember(openBook, index) { mutableStateOf(false) }
    LaunchedEffect(openBook, index, rotation) {
        try {
            bitmap = withContext(Dispatchers.IO) {
                val key = "${openBook.publication.revision}:$index:256:$rotation"
                cache[key] ?: rotated(openBook.render(index, 256), rotation).also { cache.put(key, it) }
            }
        } catch (error: Throwable) {
            if (error is CancellationException) throw error
            failed = true
        }
    }
    when {
        bitmap != null -> Image(bitmap!!.asImageBitmap(), null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
        failed -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { Text("无法预览", style = MaterialTheme.typography.labelSmall) }
        else -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator(Modifier.size(22.dp), strokeWidth = 2.dp) }
    }
}

@Composable
private fun EmptyNavigator(message: String) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(message, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun PreferencesSheet(
    preferences: ReaderPreferences,
    onDismiss: () -> Unit,
    onChange: (ReaderPreferences) -> Unit
) {
    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp, vertical = 4.dp)) {
            Text("阅读设置", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.SemiBold)
            OptionTitle("翻页方式")
            Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                PageTurnMode.entries.forEach { item ->
                    FilterChip(selected = preferences.pageTurn == item, onClick = { onChange(preferences.copy(pageTurn = item)) }, label = { Text(item.label()) })
                }
            }
            OptionTitle("布局")
            Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                // 连续 is the 上下滚动 page-turn mode now, so it is not offered twice.
                ReadingLayout.entries.filter { it != ReadingLayout.SCROLL }.forEach { item ->
                    FilterChip(selected = preferences.layout == item, onClick = { onChange(preferences.copy(layout = item)) }, label = { Text(item.label()) })
                }
            }
            OptionTitle("翻页方向")
            Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                ReadingDirection.entries.forEach { item ->
                    FilterChip(selected = preferences.direction == item, onClick = { onChange(preferences.copy(direction = item)) }, label = { Text(item.label()) })
                }
            }
            OptionTitle("缩放")
            Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                ScaleMode.entries.forEach { item ->
                    FilterChip(selected = preferences.scaleMode == item, onClick = { onChange(preferences.copy(scaleMode = item)) }, label = { Text(item.label()) })
                }
            }
            PreferenceSwitch("封面独页", "双页模式不让封面和正文拼在一起", preferences.coverAlone) { onChange(preferences.copy(coverAlone = it)) }
            PreferenceSwitch("触屏自动隐藏控件", "翻页后自动收起工具栏，点击中间可再次显示", preferences.autoHideControls) { onChange(preferences.copy(autoHideControls = it)) }
            PreferenceSwitch("智能大跨页", "转正并完整显示高置信跨页", preferences.smartSpreads) { onChange(preferences.copy(smartSpreads = it)) }
            PreferenceSwitch("自动组合相邻页", "只组合接缝证据充分的相邻页面", preferences.automaticPairs) { onChange(preferences.copy(automaticPairs = it)) }
            PreferenceSwitch("积极组合", "提高双图发现率；不确定时仍保持原样", preferences.aggressivePairs) { onChange(preferences.copy(aggressivePairs = it)) }
            PreferenceSwitch("自动判断朝向", "分析可能侧转存储的画面", preferences.automaticOrientation) { onChange(preferences.copy(automaticOrientation = it)) }
            Spacer(Modifier.height(22.dp))
        }
    }
}

@Composable
private fun OptionTitle(text: String) {
    Text(text, Modifier.padding(top = 18.dp, bottom = 6.dp), style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
}

@Composable
private fun PreferenceSwitch(title: String, summary: String, checked: Boolean, onChecked: (Boolean) -> Unit) {
    Row(Modifier.fillMaxWidth().padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(title, fontWeight = FontWeight.Medium)
            Text(summary, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Switch(checked = checked, onCheckedChange = onChecked)
    }
}

@Composable
private fun CorrectionSheet(
    group: DisplayGroup,
    publicationSize: Int,
    state: ReadingState,
    onDismiss: () -> Unit,
    onJoin: () -> Unit,
    onCancelPair: () -> Unit,
    onSwap: () -> Unit,
    onRotate: () -> Unit,
    onStandalone: () -> Unit,
    onReset: () -> Unit
) {
    val first = group.indices.minOrNull() ?: 0
    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(horizontal = 20.dp, vertical = 4.dp)) {
            Text("页面修正", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.SemiBold)
            Text(
                if (group.indices.size == 2) "当前显示第 ${first + 1}–${(group.indices.maxOrNull() ?: first) + 1} 页，左右位置会独立于翻页方向保存。"
                else "当前显示第 ${first + 1} 页。手动修正始终优先于自动分析。",
                Modifier.padding(vertical = 12.dp),
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = onRotate, modifier = Modifier.weight(1f)) { Text("顺时针旋转") }
                Button(onClick = onStandalone, modifier = Modifier.weight(1f)) { Text("独占显示") }
            }
            Spacer(Modifier.height(10.dp))
            if (group.indices.size == 2) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(onClick = onSwap, modifier = Modifier.weight(1f)) { Text("交换左右") }
                    Button(onClick = onCancelPair, modifier = Modifier.weight(1f)) { Text("取消组合") }
                }
            } else if (first < publicationSize - 1) {
                Button(onClick = onJoin, modifier = Modifier.fillMaxWidth()) { Text("与下一页组成跨页") }
                Text(
                    "较早页面会明确保存为${if (state.preferences.direction == ReadingDirection.RTL) "右侧" else "左侧"}，切换翻页方向不会交换画面。",
                    Modifier.padding(top = 6.dp), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            TextButton(onClick = onReset, modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)) { Text("恢复自动判断") }
        }
    }
}

@Composable
private fun PageError(message: String, onRetry: () -> Unit) {
    Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(28.dp)) {
        Text("这一页无法显示", color = Color.White, style = MaterialTheme.typography.titleLarge)
        Text(message, Modifier.padding(vertical = 10.dp), color = Color(0xFFCBCDD2))
        Button(onClick = onRetry) { Text("重试") }
        Text("页面位置会保留，不会跳过后续内容。", Modifier.padding(top = 8.dp), color = Color(0xFF9B9EA5), style = MaterialTheme.typography.bodySmall)
    }
}

@Composable
fun ReaderLoading(title: String, onBack: () -> Unit) {
    Box(Modifier.fillMaxSize().background(Color(0xFF0B0C0E)), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            CircularProgressIndicator(color = Color.White)
            Text(title, Modifier.padding(top = 16.dp), color = Color.White)
            TextButton(onClick = onBack, modifier = Modifier.semantics { contentDescription = "返回书库" }) { Text("返回书库") }
        }
    }
}

@Composable
private fun ReaderFailure(title: String, message: String, onBack: () -> Unit, onRetry: () -> Unit) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(28.dp)) {
            Text(title, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.SemiBold)
            Text("无法打开", Modifier.padding(top = 18.dp), style = MaterialTheme.typography.titleLarge)
            Text(message, Modifier.padding(vertical = 10.dp), color = MaterialTheme.colorScheme.onSurfaceVariant)
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                TextButton(onClick = onBack, modifier = Modifier.semantics { contentDescription = "返回书库" }) { Text("返回书库") }
                Button(onClick = onRetry) { Text("重试") }
            }
        }
    }
}

@Composable
private fun PasswordDialog(onDismiss: () -> Unit, onSubmit: (String) -> Unit) {
    var password by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("PDF 密码") },
        text = {
            Column {
                Text("这份 PDF 已加密。密码只用于当前阅读会话。")
                androidx.compose.material3.OutlinedTextField(
                    value = password,
                    onValueChange = { password = it },
                    modifier = Modifier.fillMaxWidth().padding(top = 12.dp),
                    singleLine = true,
                    visualTransformation = PasswordVisualTransformation(),
                    label = { Text("密码") }
                )
            }
        },
        confirmButton = { TextButton(onClick = { onSubmit(password) }, enabled = password.isNotEmpty()) { Text("打开") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("取消") } }
    )
}

@Composable
private fun EndOfBookDialog(next: BookRecord?, onDismiss: () -> Unit, onOpenNext: (BookRecord) -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("本卷已读完") },
        text = { Text(if (next == null) "阅读进度已保存。" else "下一卷：${next.title}") },
        confirmButton = {
            if (next != null) Button(onClick = { onOpenNext(next) }) { Text("阅读下一卷") }
            else TextButton(onClick = onDismiss) { Text("完成") }
        },
        dismissButton = { if (next != null) TextButton(onClick = onDismiss) { Text("稍后") } }
    )
}

@Composable
private fun ImmersiveEffect(enabled: Boolean) {
    val activity = LocalActivity.current ?: return
    DisposableEffect(enabled, activity) {
        val window = activity.window
        val previous = window.decorView.systemUiVisibility
        if (Build.VERSION.SDK_INT >= 30) {
            window.insetsController?.let { controller ->
                if (enabled) {
                    controller.hide(WindowInsets.Type.systemBars())
                    controller.systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
                } else controller.show(WindowInsets.Type.systemBars())
            }
        } else {
            @Suppress("DEPRECATION")
            window.decorView.systemUiVisibility = if (enabled) {
                View.SYSTEM_UI_FLAG_FULLSCREEN or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
                    View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
                    View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_LAYOUT_STABLE
            } else previous
        }
        onDispose {
            if (Build.VERSION.SDK_INT >= 30) window.insetsController?.show(WindowInsets.Type.systemBars())
            else @Suppress("DEPRECATION") run { window.decorView.systemUiVisibility = previous }
        }
    }
}

private class BitmapMemoryCache(private val maxBytes: Int) {
    private val values = LinkedHashMap<String, Bitmap>(16, .75f, true)
    private var bytes = 0

    @Synchronized operator fun get(key: String): Bitmap? = values[key]

    @Synchronized fun put(key: String, bitmap: Bitmap) {
        values.put(key, bitmap)?.let { bytes -= it.safeBytes() }
        bytes += bitmap.safeBytes()
        val iterator = values.entries.iterator()
        while (bytes > maxBytes && iterator.hasNext()) {
            bytes -= iterator.next().value.safeBytes()
            iterator.remove()
        }
    }

    @Synchronized fun clear() {
        values.clear()
        bytes = 0
    }
}

private fun Bitmap.safeBytes(): Int = runCatching { allocationByteCount }.getOrElse { byteCount }

private fun rotated(source: Bitmap, angle: Int): Bitmap {
    val normalized = Math.floorMod(angle, 360)
    if (normalized == 0) return source
    return Bitmap.createBitmap(
        source, 0, 0, source.width, source.height,
        Matrix().apply { postRotate(normalized.toFloat()) }, true
    )
}
private fun ReadingLayout.label(): String = when (this) { ReadingLayout.AUTO -> "自动"; ReadingLayout.SINGLE -> "单页"; ReadingLayout.DOUBLE -> "双页"; ReadingLayout.SCROLL -> "连续" }

private fun PageTurnMode.label(): String = when (this) {
    PageTurnMode.TAP -> "点击左右侧"
    PageTurnMode.SWIPE -> "左右滑动"
    PageTurnMode.SCROLL -> "上下滚动"
}

/** How long the reading chrome stays up after a touch before hiding itself again. */
private const val CONTROLS_AUTO_HIDE_MS = 4_000L
private fun ReadingDirection.label(): String = if (this == ReadingDirection.RTL) "右→左" else "左→右"
private fun ScaleMode.label(): String = when (this) { ScaleMode.FIT -> "适合窗口"; ScaleMode.WIDTH -> "适合宽度"; ScaleMode.ORIGINAL -> "原始大小" }
