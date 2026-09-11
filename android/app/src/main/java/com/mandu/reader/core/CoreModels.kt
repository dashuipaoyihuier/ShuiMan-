package com.mandu.reader.core

import android.graphics.Bitmap

enum class BookKind { EPUB, PDF, IMAGES, CBZ }
enum class ReadingLayout { AUTO, SINGLE, DOUBLE, SCROLL }
enum class ReadingDirection { LTR, RTL }
enum class ScaleMode { FIT, WIDTH, ORIGINAL }

/** How a touch turns the page. */
enum class PageTurnMode { TAP, SWIPE, SCROLL }

/** What a touch gesture on the reading canvas should do. */
enum class TapAction { PREVIOUS, NEXT, TOGGLE_CONTROLS }

/**
 * Pure decision logic for touch page turning, split out of the Compose layer so the
 * manga reading direction can be verified without a device.
 */
object PageTurn {
    /** Fraction of the canvas width on each side that turns the page instead of toggling chrome. */
    const val EDGE_FRACTION = 0.35f

    /** Fraction of the canvas width a swipe must travel before it turns the page. */
    const val SWIPE_THRESHOLD_FRACTION = 0.10f

    /**
     * Left/right edges turn the page; the centre toggles the reading controls.
     * RTL places the next page on the left, which is the Japanese manga reading order.
     */
    fun tapAction(x: Float, width: Float, direction: ReadingDirection): TapAction {
        if (width <= 0f) return TapAction.TOGGLE_CONTROLS
        val edge = width * EDGE_FRACTION
        return when {
            x < edge -> if (direction == ReadingDirection.RTL) TapAction.NEXT else TapAction.PREVIOUS
            x > width - edge -> if (direction == ReadingDirection.RTL) TapAction.PREVIOUS else TapAction.NEXT
            else -> TapAction.TOGGLE_CONTROLS
        }
    }

    /**
     * Horizontal swipe direction, or null when the drag is too short to count.
     * The next page is dragged in from the side it physically sits on: leftwards for LTR,
     * rightwards for RTL.
     */
    fun swipeAction(
        dragX: Float,
        width: Float,
        direction: ReadingDirection,
        thresholdFraction: Float = SWIPE_THRESHOLD_FRACTION
    ): TapAction? {
        if (width <= 0f) return null
        if (kotlin.math.abs(dragX) < width * thresholdFraction) return null
        val towardsNext = if (direction == ReadingDirection.RTL) dragX > 0f else dragX < 0f
        return if (towardsNext) TapAction.NEXT else TapAction.PREVIOUS
    }
}
data class SourceLocator(val resource: String, val occurrence: Int = 0, val imageIndex: Int = 0) {
    val key: String get() = "$occurrence:$imageIndex:$resource"
}
data class ReadingUnit(
    val locator: SourceLocator, val title: String, val imagePath: String? = null,
    val width: Int = 0, val height: Int = 0, val isCover: Boolean = false,
    val rotationHint: Int? = null, val complex: Boolean = false, val error: String? = null
) { val id: String get() = locator.key }
data class NavigationItem(val title: String, val index: Int, val fragment: String? = null)
data class Publication(
    val id: String, val title: String, val kind: BookKind, val units: List<ReadingUnit>,
    val direction: ReadingDirection? = null, val navigation: List<NavigationItem> = emptyList(),
    val warnings: List<String> = emptyList(), val revision: String = ""
)
data class BookRecord(
    val id: String, val uri: String, val title: String, val kind: BookKind,
    val series: String = "", val volume: Double? = null, val tags: List<String> = emptyList(),
    val favorite: Boolean = false, val readStatus: String = "未读", val lastOpened: Long = 0,
    val pageCount: Int = 0, val progress: Int = 0, val coverPath: String? = null,
    val missing: Boolean = false
)
data class PageOverride(
    val rotation: Int? = null, val standalone: Boolean? = null, val joinNext: Boolean? = null,
    val earlierOnRight: Boolean? = null, val pairOffset: Double = 0.0, val pairScale: Double = 1.0,
    val pairingBreak: Boolean = false
)
data class ReaderPreferences(
    // Manga default: the next page sits on the left, so the reading order is right-to-left.
    val direction: ReadingDirection = ReadingDirection.RTL, val layout: ReadingLayout = ReadingLayout.AUTO,
    val pageTurn: PageTurnMode = PageTurnMode.TAP,
    val scaleMode: ScaleMode = ScaleMode.FIT, val coverAlone: Boolean = true,
    val smartSpreads: Boolean = true, val automaticPairs: Boolean = true,
    val aggressivePairs: Boolean = true, val automaticOrientation: Boolean = true,
    val autoHideControls: Boolean = true
) {
    /** Continuous vertical scrolling is the 条漫 gesture; it also absorbs the legacy SCROLL layout. */
    val scrollsVertically: Boolean get() = pageTurn == PageTurnMode.SCROLL || layout == ReadingLayout.SCROLL
}
data class ReadingState(
    val locator: SourceLocator? = null, val preferences: ReaderPreferences = ReaderPreferences(),
    val overrides: Map<String, PageOverride> = emptyMap(), val bookmarks: List<SourceLocator> = emptyList(),
    val scrollOffset: Int = 0, val scrollFraction: Double = 0.0
)
data class SpreadDecision(val rotation: Int = 0, val standalone: Boolean = false, val uncertain: Boolean = false, val reason: String = "")
data class PairDecision(val score: Double, val swapped: Boolean, val automatic: Boolean, val suggested: Boolean = false, val verticalOffset: Double = 0.0, val rightScale: Double = 1.0)
data class DisplayGroup(val indices: List<Int>, val spread: Boolean = false, val verticalOffset: Double = 0.0, val rightScale: Double = 1.0) {
    val firstSourceIndex: Int get() = indices.minOrNull() ?: 0
}
interface OpenBook : java.io.Closeable {
    val publication: Publication
    suspend fun render(index: Int, maxEdge: Int = 2400): Bitmap
    fun resource(path: String): ByteArray?
}
class PasswordRequiredException(message: String = "这份 PDF 需要密码或密码不正确") : Exception(message)
