package com.mandu.reader.core

import kotlin.math.cos
import kotlin.math.sin
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PairAnalyzerTest {
    @Test
    fun richContinuousSeamAutomaticallyPairsInSourceOrder() {
        val left = seamPage(right = false)
        val right = seamPage(right = true)

        val decision = analyze(left, right)

        assertTrue(decision.automatic)
        assertTrue(decision.suggested)
        assertFalse(decision.swapped)
    }

    @Test
    fun reverseSourceOrderRestoresPhysicalPlacement() {
        val left = seamPage(right = false)
        val right = seamPage(right = true)

        val decision = analyze(right, left)

        assertTrue(decision.automatic)
        assertTrue(decision.swapped)
    }

    @Test
    fun boundedOffsetAndUniformScaleAreRecovered() {
        val left = seamPage(right = false)
        val shifted = seamPage(right = true, shift = 8)
        val shiftedDecision = analyze(left, shifted)
        assertTrue(shiftedDecision.suggested)
        assertEquals(-8.0 / HEIGHT, shiftedDecision.verticalOffset, 0.003)

        val rescaled = seamPage(right = true, shift = 35, scale = 0.94)
        val rescaledDecision = analyze(left, rescaled)
        assertTrue(rescaledDecision.suggested)
        assertEquals(1.0 / 0.94, rescaledDecision.rightScale, 0.009)
        assertEquals(-35.0 / HEIGHT / 0.94, rescaledDecision.verticalOffset, 0.009)
    }

    @Test
    fun blankDuplicateAndSmoothBordersCannotJustifyPairing() {
        val left = seamPage(right = false)
        val blank = DoubleArray(WIDTH * HEIGHT) { 1.0 }
        val smoothLeft = seamPage(right = false, frequency = 0.05)
        val smoothRight = seamPage(right = true, frequency = 0.05)

        assertFalse(analyze(left, left).suggested)
        assertFalse(analyze(blank, blank).suggested)
        assertFalse(analyze(smoothLeft, smoothRight).suggested)
    }

    @Test
    fun invalidBuffersAndNonPortraitCandidatesReject() {
        assertFalse(PairAnalyzer.analyzePixels(DoubleArray(3), 2, 2, DoubleArray(4), 2, 2).suggested)
        val landscape = DoubleArray(120 * 60) { if (it % 2 == 0) 0.2 else 0.8 }
        assertFalse(PairAnalyzer.analyzePixels(landscape, 120, 60, landscape, 120, 60).suggested)
    }

    private fun analyze(first: DoubleArray, second: DoubleArray): PairDecision = PairAnalyzer.analyzePixels(
        first, WIDTH, HEIGHT, second, WIDTH, HEIGHT
    )

    /** Generated grid; a positive shift lowers right-page content. */
    private fun seamPage(right: Boolean, shift: Int = 0, scale: Double = 1.0, frequency: Double = 1.0): DoubleArray {
        return DoubleArray(WIDTH * HEIGHT) { index ->
            val x = index % WIDTH
            val y = index / WIDTH
            val globalX = (x + if (right) WIDTH else 0).toDouble()
            val sourceScale = if (right) scale else 1.0
            val row = (y - if (right) shift else 0).toDouble() / sourceScale * frequency
            val phase = globalX * 0.008
            val value = 0.50 +
                0.17 * sin(row * 0.049 + phase) +
                0.15 * sin(row * 0.117 + phase * 0.8) +
                0.13 * cos(row * 0.189 - phase * 1.2)
            value.coerceIn(0.0, 1.0)
        }
    }

    private companion object {
        const val WIDTH = 768
        const val HEIGHT = 1024
    }
}
