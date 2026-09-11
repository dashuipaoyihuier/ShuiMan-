package com.mandu.reader.core

import org.junit.Assert.*
import org.junit.Test

class ReaderViewportLayoutTest {
    private fun plan(mode: ScaleMode, width: Double, height: Double) = ReaderViewportLayout.plan(
        1080.0, 2400.0, listOf(PagePixelSize(width, height)), DisplayGroup(listOf(0)), mode, 2.0, 4.0
    )

    @Test fun widthKeepsEvenVeryLongImagesAtFullAvailableWidth() {
        val layout = plan(ScaleMode.WIDTH, 600.0, 12000.0).geometry
        assertEquals(1076.0, layout.frames.single().width, 0.001)
        assertEquals(21520.0, layout.frames.single().height, 0.001)
        assertTrue(layout.size.height >= layout.frames.single().bottom)
    }

    @Test fun originalDoesNotUpscaleSmallSources() {
        val frame = plan(ScaleMode.ORIGINAL, 320.0, 480.0).geometry.frames.single()
        assertEquals(320.0, frame.width, 0.001)
        assertEquals(480.0, frame.height, 0.001)
    }

    @Test fun originalKeepsLargeSourceGeometryBeyondViewport() {
        val layout = plan(ScaleMode.ORIGINAL, 4000.0, 6000.0).geometry
        assertEquals(4000.0, layout.frames.single().width, 0.001)
        assertEquals(6000.0, layout.frames.single().height, 0.001)
        assertTrue(layout.size.width > 1080.0)
        assertTrue(layout.size.height > 2400.0)
    }

    @Test fun fitPreservesRatioAndWholeContent() {
        val frame = plan(ScaleMode.FIT, 600.0, 12000.0).geometry.frames.single()
        assertEquals(0.05, frame.width / frame.height, 0.00001)
        assertTrue(frame.x >= 2.0 && frame.y >= 2.0)
        assertTrue(frame.right <= 1078.0 && frame.bottom <= 2398.0)
    }

    @Test fun detailDecodeIsBoundedButNotAlwaysA2600Thumbnail() {
        assertEquals(6000, plan(ScaleMode.ORIGINAL, 600.0, 6000.0).decodeMaxEdge)
        assertEquals(2600, plan(ScaleMode.FIT, 600.0, 6000.0).decodeMaxEdge)
        val bounded = plan(ScaleMode.ORIGINAL, 12000.0, 18000.0).decodeMaxEdge
        assertTrue(bounded <= 8192)
        assertTrue(bounded * bounded * (12000.0 / 18000.0) <= 16_010_000.0)
    }
}
