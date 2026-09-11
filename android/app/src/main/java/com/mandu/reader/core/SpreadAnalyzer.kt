package com.mandu.reader.core

import android.graphics.Bitmap
import android.graphics.Matrix
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.text.TextRecognition
import com.google.mlkit.vision.text.japanese.JapaneseTextRecognizerOptions
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.tasks.await
import kotlinx.coroutines.withContext
import kotlin.coroutines.coroutineContext
import kotlin.math.abs
import kotlin.math.hypot

/** Bundled on-device recognition only. No page content leaves the device. */
object SpreadAnalyzer {
    suspend fun analyze(book: OpenBook, index: Int): SpreadDecision = withContext(Dispatchers.Default) {
        val unit = book.publication.units[index]
        if (unit.isCover || unit.complex || unit.error != null) return@withContext SpreadDecision()
        val bitmap = book.render(index, 1100)
        val ratio = bitmap.width.toDouble() / bitmap.height
        val baseline = SpreadDecision(standalone = ratio >= 1.2)
        if (ratio !in .30..3.2) return@withContext baseline
        val hint = unit.rotationHint?.let { Math.floorMod(it, 360) } ?: 0
        val glyph = GlyphOrientation.analyze(bitmap)
        glyph.rotation?.let { rotation ->
            if (rotation != 0) {
                if (hint != 0 && hint != rotation) return@withContext baseline.copy(uncertain=true, reason="字形与出版物朝向冲突，可手动旋转")
                return@withContext SpreadDecision(rotation, true, reason="局部字形方向 · 自动转正")
            }
            // Positive upright glyph evidence protects native vertical CJK from baseline ambiguity.
            if (hint == 0) return@withContext baseline
        }
        val recognizer = TextRecognition.getClient(JapaneseTextRecognizerOptions.Builder().build())
        val scores = mutableMapOf<Int, Double>()
        val counts = mutableMapOf<Int, Int>()
        try {
            for (angle in listOf(0, 90, 180, 270)) {
                coroutineContext.ensureActive()
                val turned = if (angle == 0) bitmap else Bitmap.createBitmap(bitmap, 0, 0, bitmap.width, bitmap.height, Matrix().apply { postRotate(angle.toFloat()) }, true)
                try {
                    val text = recognizer.process(InputImage.fromBitmap(turned, 0)).await()
                    var weight = 0.0; var glyphs = 0
                    for (block in text.textBlocks) for (line in block.lines) {
                        val points = line.cornerPoints ?: continue
                        if (points.size < 4) continue
                        val dx = (points[1].x - points[0].x).toDouble(); val dy = (points[1].y - points[0].y).toDouble()
                        val length = hypot(dx, dy)
                        // Upright horizontal baselines only; native vertical CJK columns are not sideways evidence.
                        if (dx <= 0 || abs(dy) > length * .18) continue
                        val box = line.boundingBox ?: continue
                        val chars = line.text.count { it.isLetterOrDigit() }
                        if (chars < 3 || box.width() < box.height() * 1.7) continue
                        val confidence = line.confidence.toDouble()
                        if (confidence < .65) continue
                        glyphs += chars
                        weight += minOf(chars, 24) * confidence * confidence
                    }
                    scores[angle] = weight; counts[angle] = glyphs
                } finally { if (turned !== bitmap) turned.recycle() }
            }
        } finally { recognizer.close() }
        val ranked = scores.entries.sortedByDescending { it.value }
        val best = ranked.firstOrNull()
        val runner = ranked.getOrNull(1)?.value ?: 0.0
        val convincing = best != null && best.value >= 12 && (counts[best.key] ?: 0) >= 16 && best.value >= maxOf(1.0, runner) * 1.8 && best.value - runner >= 8
        if (convincing && best!!.key != 0) {
            if (hint != 0 && hint != best.key) return@withContext baseline.copy(uncertain=true, reason="文字与出版物朝向冲突，可手动旋转")
            return@withContext SpreadDecision(best.key, true, reason="离线文字朝向分析")
        }
        if (hint in listOf(90, 180, 270)) return@withContext SpreadDecision(hint, true, reason="出版物旋转样式")
        baseline.copy(uncertain = best != null && best.key != 0 && best.value >= 5, reason = if (best != null && best.key != 0 && best.value >= 5) "朝向证据不足，可手动旋转" else "")
    }

    suspend fun analyzePair(book: OpenBook, index: Int): PairDecision = withContext(Dispatchers.Default) {
        val units = book.publication.units
        if (index !in 0 until units.lastIndex || (index..index+1).any { units[it].isCover || units[it].complex || units[it].error != null })
            return@withContext PairDecision(0.0, false, false)
        val a = book.render(index, 384)
        coroutineContext.ensureActive()
        val b = book.render(index+1, 384)
        coroutineContext.ensureActive()
        PairAnalyzer.analyze(a,b)
    }
}
