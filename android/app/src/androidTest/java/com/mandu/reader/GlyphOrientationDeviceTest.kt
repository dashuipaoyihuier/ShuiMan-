package com.mandu.reader

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.Typeface
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.mandu.reader.core.GlyphOrientation
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.Locale

/** Original generated CJK bubbles; exercises real bundled OCR and native font strokes. */
@RunWith(AndroidJUnit4::class)
class GlyphOrientationDeviceTest {
    @Test fun uprightVerticalColumnsAreNotSidewaysText() = checkStoredRotation(0)
    @Test fun clockwiseStoredColumnsNeedCounterclockwiseCorrection() = checkStoredRotation(90)
    @Test fun upsideDownColumnsNeedHalfTurnCorrection() = checkStoredRotation(180)
    @Test fun counterclockwiseStoredColumnsNeedClockwiseCorrection() = checkStoredRotation(270)

    @Test
    fun blankPageAndEmptyBubblesHaveNoOrientationEvidence() = runBlocking {
        for (bubbles in listOf(false, true)) {
            val image = if (bubbles) fixture(drawText = false) else Bitmap.createBitmap(960, 720, Bitmap.Config.ARGB_8888).apply { eraseColor(Color.WHITE) }
            try {
                val evidence = GlyphOrientation.analyze(image)
                assertNull(evidence.toString(), evidence.rotation)
                assertEquals(0, evidence.regions)
                assertTrue(evidence.scores.isEmpty())
                assertTrue(evidence.characters.isEmpty())
                assertFalse("analyzer must not recycle caller's bitmap", image.isRecycled)
            } finally { image.recycle() }
        }
    }

    private fun checkStoredRotation(storedAngle: Int) = runBlocking {
        val original = fixture()
        val stored = if (storedAngle == 0) original else Bitmap.createBitmap(original, 0, 0, original.width, original.height, Matrix().apply { postRotate(storedAngle.toFloat()) }, false)
        val before = stored.getPixel(10, 10)
        try {
            val evidence = GlyphOrientation.analyze(stored)
            val correction = (360 - storedAngle) % 360
            assertTrue("bubble segmentation failed: $evidence", evidence.regions >= 2)
            assertEquals("stored=$storedAngle, evidence=$evidence", correction, evidence.rotation)
            assertTrue("requires distinct physical glyphs: $evidence", (evidence.characters[correction] ?: 0) >= 3)
            // A glyph appears on four OCR sheets, but may contribute only one vote.
            assertTrue("repeated OCR inflated character support: $evidence", evidence.characters.values.sum() <= 24)
            assertFalse(stored.isRecycled)
            assertEquals("source pixels must remain unchanged", before, stored.getPixel(10, 10))
        } finally {
            if (stored !== original) stored.recycle()
            original.recycle()
        }
    }

    private fun fixture(drawText: Boolean = true): Bitmap {
        val bitmap = Bitmap.createBitmap(960, 720, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        canvas.drawColor(Color.rgb(175, 180, 185))
        val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.WHITE }
        val outline = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.BLACK; style = Paint.Style.STROKE; strokeWidth = 4f }
        val text = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.BLACK; textSize = 42f; typeface = Typeface.create("sans-serif", Typeface.NORMAL); textLocale = Locale.JAPAN
        }
        val phrases = listOf("明朝海岸集合時間確認出発", "静夜森林探検仲間無事帰還")
        for ((index, phrase) in phrases.withIndex()) {
            val left = 75f + index * 450f
            val bubble = RectF(left, 110f, left + 300f, 610f)
            canvas.drawOval(bubble, fill)
            canvas.drawOval(bubble, outline)
            if (!drawText) continue
            assertEquals(12, phrase.length)
            for ((i, character) in phrase.withIndex()) {
                val column = i / 4; val row = i % 4
                val bounds = Rect()
                val value = character.toString()
                assertTrue("device must provide CJK glyphs", text.hasGlyph(value))
                text.getTextBounds(value, 0, 1, bounds)
                val centerX = left + 212 - column * 62
                val centerY = 242f + row * 70
                canvas.drawText(value, centerX - bounds.exactCenterX(), centerY - bounds.exactCenterY(), text)
            }
        }
        return bitmap
    }
}
