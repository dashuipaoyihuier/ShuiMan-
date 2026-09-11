package com.mandu.reader.core

import android.graphics.Bitmap
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.roundToInt
import kotlin.math.sqrt

/**
 * Conservative, local-only detector for two adjacent source pages that form a
 * single physical spread.  The returned [PairDecision.swapped] describes
 * physical left-to-right placement; it is deliberately independent of reading
 * direction.
 *
 * This ports ComicCore's `seam-scale-offset-v2`.  It only compares the likely
 * inner edges and rejects low-information, duplicate, and smooth-border pages
 * before accepting a pair.
 */
object PairAnalyzer {
    const val algorithmVersion: String = "seam-scale-offset-v2"

    private const val analysisHeight = 384

    private data class Features(
        val left: DoubleArray,
        val right: DoubleArray,
        val thumbnail: DoubleArray
    )

    private data class Candidate(
        val swapped: Boolean,
        val verticalOffset: Double = 0.0,
        val rightScale: Double = 1.0,
        val score: Double = 0.0,
        val correlation: Double = 0.0,
        val detailCorrelation: Double = 0.0,
        val meanError: Double = 1.0,
        val matchingBands: Int = 0
    )

    /** Converts an Android bitmap to white-composited, normalized grayscale. */
    fun analyze(first: Bitmap, second: Bitmap): PairDecision {
        return analyzePixels(
            bitmapPixels(first), first.width, first.height,
            bitmapPixels(second), second.width, second.height
        )
    }

    /**
     * JVM-testable entry point. Pixels are normalized row-major grayscale in
     * [0, 1]. Invalid dimensions or buffers reject the pair rather than
     * attempting a best-effort match.
     */
    fun analyzePixels(
        first: DoubleArray,
        firstWidth: Int,
        firstHeight: Int,
        second: DoubleArray,
        secondWidth: Int,
        secondHeight: Int
    ): PairDecision {
        if (!valid(first, firstWidth, firstHeight) || !valid(second, secondWidth, secondHeight)) {
            return rejected()
        }
        val firstRatio = firstWidth.toDouble() / firstHeight
        val secondRatio = secondWidth.toDouble() / secondHeight
        if (firstRatio !in 0.45..0.90 || secondRatio !in 0.45..0.90 || abs(firstRatio - secondRatio) >= 0.18) {
            return rejected()
        }
        val a = features(first, firstWidth, firstHeight) ?: return rejected()
        val b = features(second, secondWidth, secondHeight) ?: return rejected()
        if (meanError(a.thumbnail, b.thumbnail) < 0.025 || ink(a.thumbnail) < 0.07 || ink(b.thumbnail) < 0.07) {
            return rejected()
        }

        val forward = placement(a.right, b.left, swapped = false)
        val reverse = placement(b.right, a.left, swapped = true)
        val best = if (forward.score >= reverse.score) forward else reverse
        val placementMargin = abs(forward.score - reverse.score)
        val automatic = best.score >= 0.68 &&
            best.correlation >= 0.84 &&
            best.detailCorrelation >= 0.40 &&
            best.meanError <= 0.12 &&
            best.matchingBands >= 5 &&
            placementMargin >= 0.15
        val suggested = automatic || (
            best.score >= 0.49 &&
                best.correlation >= 0.64 &&
                best.detailCorrelation >= 0.19 &&
                best.meanError <= 0.16 &&
                best.matchingBands >= 3 &&
                placementMargin >= 0.10
            )
        return PairDecision(
            score = best.score,
            swapped = best.swapped,
            automatic = automatic,
            suggested = suggested,
            verticalOffset = best.verticalOffset,
            rightScale = best.rightScale
        )
    }

    private fun bitmapPixels(bitmap: Bitmap): DoubleArray {
        val argb = IntArray(bitmap.width * bitmap.height)
        bitmap.getPixels(argb, 0, bitmap.width, 0, 0, bitmap.width, bitmap.height)
        return DoubleArray(argb.size) { index ->
            val color = argb[index]
            val alpha = ((color ushr 24) and 0xff) / 255.0
            val red = ((color ushr 16) and 0xff) / 255.0
            val green = ((color ushr 8) and 0xff) / 255.0
            val blue = (color and 0xff) / 255.0
            // CGContext first fills the target white, then draws. Match that
            // behavior for translucent Android bitmaps.
            alpha * (0.299 * red + 0.587 * green + 0.114 * blue) + (1.0 - alpha)
        }
    }

    private fun valid(pixels: DoubleArray, width: Int, height: Int): Boolean {
        return width > 0 && height > 0 && pixels.size == width * height && pixels.all { it.isFinite() }
    }

    private fun rejected(): PairDecision = PairDecision(
        score = 0.0,
        swapped = false,
        automatic = false,
        suggested = false
    )

    private fun features(pixels: DoubleArray, width: Int, height: Int): Features? {
        val scaledWidth = max(64, (width.toDouble() / height * analysisHeight).roundToInt())
        val page = resample(pixels, width, height, scaledWidth, analysisHeight)
        val thumbnail = resample(pixels, width, height, 24, 32)
        val margin = (scaledWidth * 0.04).toInt()

        fun edgeInk(x: Int): Double {
            var count = 0
            for (y in 0 until analysisHeight) {
                if (page[y * scaledWidth + x] < 0.85) count++
            }
            return count.toDouble() / analysisHeight
        }

        var left = 0
        for (offset in 0..margin) {
            if (edgeInk(offset) > 0.12) {
                left = offset
                break
            }
        }
        var right = scaledWidth - 1
        for (offset in 0..margin) {
            val x = scaledWidth - 1 - offset
            if (edgeInk(x) > 0.12) {
                right = x
                break
            }
        }
        return Features(
            left = profile(page, scaledWidth, left, min(scaledWidth, left + 3)),
            right = profile(page, scaledWidth, max(0, right - 2), right + 1),
            thumbnail = thumbnail
        )
    }

    private fun profile(pixels: DoubleArray, width: Int, startX: Int, endExclusive: Int): DoubleArray {
        val count = endExclusive - startX
        if (count <= 0) return DoubleArray(0)
        return DoubleArray(analysisHeight) { y ->
            var sum = 0.0
            for (x in startX until endExclusive) sum += pixels[y * width + x]
            sum / count
        }
    }

    private fun resample(
        source: DoubleArray,
        sourceWidth: Int,
        sourceHeight: Int,
        targetWidth: Int,
        targetHeight: Int
    ): DoubleArray {
        return DoubleArray(targetWidth * targetHeight) { destination ->
            val x = destination % targetWidth
            val y = destination / targetWidth
            // Pixel-centre bilinear sampling matches the downsampled grayscale
            // inputs used by the Swift implementation closely while keeping the
            // pure-array API independent of Android's Bitmap scaling.
            val sourceX = (x + 0.5) * sourceWidth / targetWidth - 0.5
            val sourceY = (y + 0.5) * sourceHeight / targetHeight - 0.5
            val sourceX0 = kotlin.math.floor(sourceX).toInt()
            val sourceY0 = kotlin.math.floor(sourceY).toInt()
            val x0 = sourceX0.coerceIn(0, sourceWidth - 1)
            val y0 = sourceY0.coerceIn(0, sourceHeight - 1)
            val x1 = (sourceX0 + 1).coerceIn(0, sourceWidth - 1)
            val y1 = (sourceY0 + 1).coerceIn(0, sourceHeight - 1)
            val tx = (sourceX - kotlin.math.floor(sourceX)).coerceIn(0.0, 1.0)
            val ty = (sourceY - kotlin.math.floor(sourceY)).coerceIn(0.0, 1.0)
            val top = source[y0 * sourceWidth + x0] * (1.0 - tx) + source[y0 * sourceWidth + x1] * tx
            val bottom = source[y1 * sourceWidth + x0] * (1.0 - tx) + source[y1 * sourceWidth + x1] * tx
            (top * (1.0 - ty) + bottom * ty).coerceIn(0.0, 1.0)
        }
    }

    private fun placement(left: DoubleArray, right: DoubleArray, swapped: Boolean): Candidate {
        if (left.size != analysisHeight || right.size != analysisHeight || min(deviation(left), deviation(right)) < 0.08) {
            return Candidate(swapped)
        }
        if (changeCount(left) < analysisHeight * 0.12 || changeCount(right) < analysisHeight * 0.12) {
            return Candidate(swapped)
        }

        fun measure(scale: Double, offset: Double): Candidate {
            val a = DoubleArray(analysisHeight)
            val b = DoubleArray(analysisHeight)
            var count = 0
            for (row in 0 until analysisHeight) {
                val source = ((row.toDouble() / analysisHeight) - offset) / scale * analysisHeight
                if (source < 0.0 || source >= analysisHeight - 1.0) continue
                val lower = source.toInt()
                val fraction = source - lower
                a[count] = left[row]
                b[count] = right[lower] * (1.0 - fraction) + right[lower + 1] * fraction
                count++
            }
            if (count < (analysisHeight * 0.80).toInt()) return Candidate(swapped)
            val usedA = a.copyOf(count)
            val usedB = b.copyOf(count)
            val correlation = correlate(usedA, usedB)
            val error = meanError(usedA, usedB)
            val firstDerivative = DoubleArray(count - 1) { i -> usedA[i + 1] - usedA[i] }
            val secondDerivative = DoubleArray(count - 1) { i -> usedB[i + 1] - usedB[i] }
            val detail = correlate(firstDerivative, secondDerivative)
            var bands = 0
            for (band in 0 until 12) {
                val start = band * count / 12
                val end = (band + 1) * count / 12
                val bandA = usedA.copyOfRange(start, end)
                val bandB = usedB.copyOfRange(start, end)
                if (min(deviation(bandA), deviation(bandB)) >= 0.055 &&
                    correlate(bandA, bandB) > 0.7 && meanError(bandA, bandB) < 0.15
                ) bands++
            }
            val score = 0.55 * max(0.0, correlation) +
                0.20 * max(0.0, detail) + 0.25 * bands.toDouble() / 12.0
            return Candidate(swapped, offset, scale, score, correlation, detail, error, bands)
        }

        var best = Candidate(swapped)
        for (shift in -5..5) {
            val candidate = measure(1.0, shift.toDouble() / analysisHeight)
            if (candidate.score > best.score) best = candidate
        }
        val baseline = best
        var coarse = best
        for (s in -5..5) for (t in -8..8) {
            val candidate = measure(1.0 + s * 0.02, t * 0.01)
            if (candidate.score > coarse.score) coarse = candidate
        }
        for (s in -3..3) for (t in -4..4) {
            val candidate = measure(coarse.rightScale + s * 0.002, coarse.verticalOffset + t * 0.001)
            if (candidate.score > best.score) best = candidate
        }
        if (best.score < baseline.score + 0.025) return baseline
        if (abs(best.rightScale - 1.0) > 0.015 || abs(best.verticalOffset) > 0.015) {
            if (best.correlation < 0.78 || best.detailCorrelation < 0.28 || best.matchingBands < 4) return baseline
        }

        val seed = best
        fun fidelity(value: Candidate): Double = value.correlation + 0.25 * value.detailCorrelation - 0.5 * value.meanError
        for (s in -10..10) for (t in -6..6) {
            val candidate = measure(seed.rightScale + s * 0.0005, seed.verticalOffset + t * 0.0005)
            if (candidate.matchingBands >= seed.matchingBands - 1 &&
                candidate.score >= seed.score - 0.025 &&
                fidelity(candidate) > fidelity(best)
            ) best = candidate
        }
        return best
    }

    private fun ink(thumbnail: DoubleArray): Double = thumbnail.count { it < 0.75 }.toDouble() / thumbnail.size

    private fun changeCount(values: DoubleArray): Int {
        var changes = 0
        for (i in 1 until values.size) if (abs(values[i] - values[i - 1]) > 0.035) changes++
        return changes
    }

    private fun meanError(first: DoubleArray, second: DoubleArray): Double {
        val size = min(first.size, second.size)
        if (size == 0) return 0.0
        var total = 0.0
        for (index in 0 until size) total += abs(first[index] - second[index])
        return total / max(1, size)
    }

    private fun deviation(values: DoubleArray): Double {
        if (values.isEmpty()) return 0.0
        var mean = 0.0
        for (value in values) mean += value
        mean /= values.size
        var sum = 0.0
        for (value in values) sum += (value - mean).pow(2)
        return sqrt(sum / values.size)
    }

    private fun correlate(first: DoubleArray, second: DoubleArray): Double {
        val size = min(first.size, second.size)
        if (size == 0) return 0.0
        var firstMean = 0.0
        var secondMean = 0.0
        for (index in 0 until size) {
            firstMean += first[index]
            secondMean += second[index]
        }
        firstMean /= size
        secondMean /= size
        var cross = 0.0
        var firstVariance = 0.0
        var secondVariance = 0.0
        for (index in 0 until size) {
            val x = first[index] - firstMean
            val y = second[index] - secondMean
            cross += x * y
            firstVariance += x * x
            secondVariance += y * y
        }
        return cross / max(1e-8, sqrt(firstVariance * secondVariance))
    }
}
