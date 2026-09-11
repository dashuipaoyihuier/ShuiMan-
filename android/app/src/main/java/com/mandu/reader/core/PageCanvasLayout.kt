package com.mandu.reader.core

/**
 * Complete image rectangles in a shared canvas, in top-left coordinates.
 *
 * Port of ComicCore/PageCanvasLayout.swift. A group has one fitted baseline height;
 * fitting its pages independently would leave a gutter inside a confirmed spread.
 * The caller supplies ratios in physical left-to-right order after image rotation.
 * Viewport, insets, normalGap, and returned rectangles use the same units (px or dp).
 */
class PageCanvasLayout(
    viewportWidth: Double,
    viewportHeight: Double,
    ratios: List<Double>,
    spread: Boolean,
    verticalOffset: Double = 0.0,
    zoom: Double = 1.0,
    fitWidth: Boolean = false,
    rightScale: Double = 1.0,
    edgeInset: Double = 24.0,
    normalGap: Double = 14.0
) {
    data class Size(val width: Double, val height: Double)
    data class Rect(val x: Double, val y: Double, val width: Double, val height: Double) {
        val right: Double get() = x + width
        val bottom: Double get() = y + height
    }

    val size: Size
    val frames: List<Rect>

    init {
        require(viewportWidth.isFinite() && viewportWidth >= 0 && viewportHeight.isFinite() && viewportHeight >= 0)
        require(ratios.all { it.isFinite() && it > 0 })
        require(zoom.isFinite() && zoom > 0)
        require(verticalOffset.isFinite() && rightScale.isFinite() && edgeInset.isFinite() && normalGap.isFinite())

        val gap = if (spread) 0.0 else maxOf(0.0, normalGap)
        val paired = spread && ratios.size == 2
        val offset = if (paired) verticalOffset.coerceIn(-0.15, 0.15) else 0.0
        val scale = if (paired) rightScale.coerceIn(0.85, 1.18) else 1.0
        val fullHeight = maxOf(1.0, offset + scale) - minOf(0.0, offset)
        val margin = maxOf(0.0, edgeInset)
        val gaps = gap * maxOf(0, ratios.size - 1)
        val totalRatio = ratios.mapIndexed { index, ratio -> ratio * if (index == 1) scale else 1.0 }.sum()
        val heightByWidth = maxOf(1.0, viewportWidth - margin * 2 - gaps) / (totalRatio.takeIf { it > 0.0 } ?: 1.0)
        val height = (if (fitWidth) heightByWidth else minOf(heightByWidth, maxOf(1.0, viewportHeight - margin * 2) / fullHeight)) * zoom
        val width = height * totalRatio + gaps
        val totalHeight = height * fullHeight
        size = Size(maxOf(viewportWidth, width + margin * 2), maxOf(viewportHeight, totalHeight + margin * 2))
        var x = (size.width - width) / 2
        val y = (size.height - totalHeight) / 2
        frames = ratios.mapIndexed { index, ratio ->
            val pageHeight = height * if (index == 1) scale else 1.0
            val pageY = y + (if (index == 0) maxOf(0.0, -offset) else maxOf(0.0, offset)) * height
            Rect(x, pageY, pageHeight * ratio, pageHeight).also { x += it.width + gap }
        }
    }
}
