package com.mandu.reader.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class PageCanvasLayoutTest {
    @Test
    fun confirmedSpreadSharesOneHeightAndContiguousPhysicalSeam() {
        val layout = PageCanvasLayout(1600.0, 1000.0, listOf(.6, .9), spread = true, edgeInset = 0.0)
        assertFrame(layout.frames[0], 50.0, 0.0, 600.0, 1000.0)
        assertFrame(layout.frames[1], 650.0, 0.0, 900.0, 1000.0)
        assertEquals(layout.frames[0].right, layout.frames[1].x, EPSILON)
        assertFits(layout)
    }

    @Test
    fun normalDoublePagesHaveOnlyTheSpecifiedGap() {
        val layout = PageCanvasLayout(1200.0, 800.0, listOf(.75, .75), spread = false)
        val left = layout.frames[0]; val right = layout.frames[1]
        assertEquals(14.0, right.x - left.right, EPSILON)
        assertEquals(752.0, left.height, EPSILON)
        assertEquals(left.height, right.height, EPSILON)
        assertEquals(29.0, left.x, EPSILON)
        assertEquals(24.0, left.y, EPSILON)
        assertFits(layout)
    }

    @Test
    fun ordinaryDoubleLayoutIgnoresSeamAlignmentCorrections() {
        val baseline = PageCanvasLayout(1200.0, 800.0, listOf(.75, .6), spread = false)
        val corrected = PageCanvasLayout(1200.0, 800.0, listOf(.75, .6), spread = false, verticalOffset = .15, rightScale = 1.18)
        assertEquals(baseline.frames, corrected.frames)
        assertEquals(baseline.size, corrected.size)
        val single = PageCanvasLayout(1200.0, 800.0, listOf(1.5), spread = true, verticalOffset = -.15, rightScale = .85)
        assertEquals(1.5, single.frames.single().width / single.frames.single().height, EPSILON)
    }

    @Test
    fun aspectFitPreservesCompleteSingleImageAndEdgeToEdgeRemovesInsets() {
        val full = PageCanvasLayout(1600.0, 1000.0, listOf(1.6), spread = true, edgeInset = 0.0)
        assertFrame(full.frames.single(), 0.0, 0.0, 1600.0, 1000.0)
        val portrait = PageCanvasLayout(1600.0, 1000.0, listOf(.7), spread = false, edgeInset = 0.0)
        assertFrame(portrait.frames.single(), 450.0, 0.0, 700.0, 1000.0)
        val inset = PageCanvasLayout(1600.0, 1000.0, listOf(1.6), spread = true)
        assertEquals(24.0, inset.frames.single().y, EPSILON)
        assertEquals(952.0, inset.frames.single().height, EPSILON)
        assertFits(full); assertFits(portrait); assertFits(inset)
    }

    @Test
    fun positiveAndNegativeAlignmentPreserveProtrudingEdgesAndScaleUniformly() {
        for (offset in listOf(-.06, .06)) {
            val layout = PageCanvasLayout(1200.0, 800.0, listOf(.75, .75), spread = true, verticalOffset = offset, rightScale = 1.08)
            val left = layout.frames[0]; val right = layout.frames[1]
            assertEquals(left.right, right.x, EPSILON)
            assertEquals(offset * left.height, right.y - left.y, EPSILON)
            assertEquals(1.08, right.height / left.height, EPSILON)
            assertEquals(.75, left.width / left.height, EPSILON)
            assertEquals(.75, right.width / right.height, EPSILON)
            assertFits(layout)
        }
    }

    @Test
    fun alignmentClampsMatchNativeMacBounds() {
        for ((offset, scale) in listOf(-.15 to .85, .15 to 1.18)) {
            val bounded = PageCanvasLayout(1200.0, 800.0, listOf(.75, .75), true, offset, rightScale = scale)
            val excessive = PageCanvasLayout(1200.0, 800.0, listOf(.75, .75), true, if (offset < 0) -3.0 else 3.0, rightScale = if (scale < 1) .1 else 10.0)
            assertEquals(bounded.frames, excessive.frames)
            assertEquals(bounded.size, excessive.size)
        }
    }

    @Test
    fun phonePortraitAndTabletLandscapeFitTheWholeUnion() {
        val phone = PageCanvasLayout(360.0, 800.0, listOf(.75, .75), spread = true, edgeInset = 0.0)
        assertFrame(phone.frames[0], 0.0, 280.0, 180.0, 240.0)
        assertFrame(phone.frames[1], 180.0, 280.0, 180.0, 240.0)
        val tablet = PageCanvasLayout(1280.0, 800.0, listOf(.75, .75), spread = true, edgeInset = 0.0)
        assertFrame(tablet.frames[0], 40.0, 0.0, 600.0, 800.0)
        assertFrame(tablet.frames[1], 640.0, 0.0, 600.0, 800.0)
        assertFits(phone); assertFits(tablet)
    }

    @Test
    fun fitWidthAndZoomGrowCanvasWithoutCroppingItsSourceFrames() {
        val widthFit = PageCanvasLayout(600.0, 800.0, listOf(.5), false, fitWidth = true, edgeInset = 0.0)
        assertEquals(600.0, widthFit.size.width, EPSILON)
        assertEquals(1200.0, widthFit.size.height, EPSILON)
        assertFrame(widthFit.frames.single(), 0.0, 0.0, 600.0, 1200.0)
        val zoom = PageCanvasLayout(1200.0, 800.0, listOf(.75, .75), true, zoom = 2.0, edgeInset = 0.0)
        assertEquals(2400.0, zoom.size.width, EPSILON)
        assertEquals(1600.0, zoom.size.height, EPSILON)
        assertEquals(zoom.frames[0].right, zoom.frames[1].x, EPSILON)
        assertFits(widthFit); assertFits(zoom)
    }

    @Test
    fun pixelCoordinatesCanScaleDpInsetsAndOrdinaryGapTogether() {
        val dp = PageCanvasLayout(600.0, 800.0, listOf(.7, .8), false)
        val px = PageCanvasLayout(1800.0, 2400.0, listOf(.7, .8), false, edgeInset = 72.0, normalGap = 42.0)
        for (i in dp.frames.indices) {
            assertEquals(dp.frames[i].x * 3, px.frames[i].x, EPSILON)
            assertEquals(dp.frames[i].y * 3, px.frames[i].y, EPSILON)
            assertEquals(dp.frames[i].width * 3, px.frames[i].width, EPSILON)
            assertEquals(dp.frames[i].height * 3, px.frames[i].height, EPSILON)
        }
    }

    private fun assertFrame(frame: PageCanvasLayout.Rect, x: Double, y: Double, width: Double, height: Double) {
        assertEquals(x, frame.x, EPSILON); assertEquals(y, frame.y, EPSILON)
        assertEquals(width, frame.width, EPSILON); assertEquals(height, frame.height, EPSILON)
    }

    private fun assertFits(layout: PageCanvasLayout) {
        for (frame in layout.frames) {
            assertTrue(frame.x >= -EPSILON && frame.y >= -EPSILON)
            assertTrue(frame.right <= layout.size.width + EPSILON && frame.bottom <= layout.size.height + EPSILON)
        }
    }

    private companion object { const val EPSILON = 1e-8 }
}
