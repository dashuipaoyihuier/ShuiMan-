package com.mandu.reader.core

import kotlin.math.max
import kotlin.math.roundToInt
import kotlin.math.sqrt

/** Source-pixel dimensions after publication and user rotations have been applied. */
data class PagePixelSize(val width: Double, val height: Double) {
    init {
        require(width.isFinite() && width > 0.0 && height.isFinite() && height > 0.0)
    }

    val ratio: Double get() = width / height
    val pixels: Double get() = width * height
    val maxEdge: Double get() = max(width, height)
}

data class ReaderViewportPlan(
    val geometry: PageCanvasLayout,
    /** Decode bound for the whole group. Geometry remains source-pixel based in ORIGINAL. */
    val decodeMaxEdge: Int
)

/**
 * Pure bridge between source-pixel semantics and [PageCanvasLayout]. Keeping this outside
 * Compose makes WIDTH/ORIGINAL sizing deterministic on phones, tablets and resized windows.
 */
object ReaderViewportLayout {
    const val DEFAULT_RENDER_EDGE = 2_600
    const val MAX_RENDER_EDGE = 8_192
    const val GROUP_PIXEL_BUDGET = 16_000_000.0

    fun sourceSizes(
        publication: Publication,
        group: DisplayGroup,
        rotations: Map<String, Int>,
        fallbackWidth: Double = 700.0,
        fallbackHeight: Double = 1_000.0
    ): List<PagePixelSize> = group.indices.map { index ->
        val unit = publication.units[index]
        val rawWidth = unit.width.takeIf { it > 0 }?.toDouble() ?: fallbackWidth
        val rawHeight = unit.height.takeIf { it > 0 }?.toDouble() ?: fallbackHeight
        if (Math.floorMod(rotations[unit.id] ?: 0, 360) % 180 == 90) {
            PagePixelSize(rawHeight, rawWidth)
        } else {
            PagePixelSize(rawWidth, rawHeight)
        }
    }

    fun plan(
        viewportWidth: Double,
        viewportHeight: Double,
        sourceSizes: List<PagePixelSize>,
        group: DisplayGroup,
        scaleMode: ScaleMode,
        edgeInset: Double,
        normalGap: Double
    ): ReaderViewportPlan {
        val width = viewportWidth.coerceAtLeast(1.0)
        val height = viewportHeight.coerceAtLeast(1.0)
        val sizes = sourceSizes.ifEmpty { listOf(PagePixelSize(700.0, 1_000.0)) }
        val base = PageCanvasLayout(
            viewportWidth = width,
            viewportHeight = height,
            ratios = sizes.map(PagePixelSize::ratio),
            spread = group.spread,
            verticalOffset = group.verticalOffset,
            fitWidth = scaleMode == ScaleMode.WIDTH,
            rightScale = group.rightScale,
            edgeInset = edgeInset,
            normalGap = normalGap
        )
        val geometry = if (scaleMode == ScaleMode.ORIGINAL) {
            // No lower clamp: a 320 px source is 320 physical canvas pixels, even on a tablet.
            val fittedFirstHeight = base.frames.firstOrNull()?.height?.coerceAtLeast(1.0) ?: 1.0
            PageCanvasLayout(
                viewportWidth = width,
                viewportHeight = height,
                ratios = sizes.map(PagePixelSize::ratio),
                spread = group.spread,
                verticalOffset = group.verticalOffset,
                zoom = sizes.first().height / fittedFirstHeight,
                fitWidth = false,
                rightScale = group.rightScale,
                edgeInset = edgeInset,
                normalGap = normalGap
            )
        } else base
        return ReaderViewportPlan(geometry, decodeEdge(sizes, scaleMode))
    }

    /**
     * FIT keeps the established finite preview bound. WIDTH and ORIGINAL decode toward source
     * resolution, sharing a 16 MP group budget and respecting the decoder's 8192 edge ceiling.
     */
    fun decodeEdge(sizes: List<PagePixelSize>, scaleMode: ScaleMode): Int {
        if (scaleMode == ScaleMode.FIT || sizes.isEmpty()) return DEFAULT_RENDER_EDGE
        val totalPixels = sizes.sumOf(PagePixelSize::pixels).coerceAtLeast(1.0)
        val budgetScale = minOf(1.0, sqrt(GROUP_PIXEL_BUDGET / totalPixels))
        val wanted = sizes.maxOf(PagePixelSize::maxEdge) * budgetScale
        return wanted.roundToInt().coerceIn(64, MAX_RENDER_EDGE)
    }
}
