package com.mandu.reader.core

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.Typeface
import android.util.LruCache
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.text.TextRecognition
import com.google.mlkit.vision.text.japanese.JapaneseTextRecognizerOptions
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.tasks.await
import kotlinx.coroutines.withContext
import java.util.Locale
import kotlin.coroutines.coroutineContext
import kotlin.math.floor

/**
 * Port of ComicCore's local bubble/glyph orientation evidence. Reflow changes the
 * layout, never the glyph strokes. Thus upright vertical CJK is upright evidence.
 * Returned angles are clockwise display corrections in Android image coordinates.
 */
object GlyphOrientation {
    data class Evidence(
        val rotation: Int? = null,
        val scores: Map<Int, Double> = emptyMap(),
        val characters: Map<Int, Int> = emptyMap(),
        val regions: Int = 0
    )

    private data class Mask(val width: Int, val height: Int, val pixels: ByteArray, val originX: Int = 0, val originY: Int = 0)
    private data class Component(val ink: Boolean, val bounds: Rect, val points: IntArray)
    private data class Vote(val region: Int, val bounds: Rect, val rotation: Int, val score: Double, val margin: Double)
    private data class GlyphLine(val region: Int, val glyphs: List<Mask>)
    private val templateCache = LruCache<String, List<ByteArray>>(1024)
    private val angles = listOf(0, 90, 180, 270)

    suspend fun analyze(bitmap: Bitmap): Evidence = withContext(Dispatchers.Default) {
        val masks = regions(bitmap)
        if (masks.isEmpty()) return@withContext Evidence()
        val recognizer = TextRecognition.getClient(JapaneseTextRecognizerOptions.Builder().build())
        val votes = mutableListOf<Vote>()
        try {
            for (angle in angles) {
                coroutineContext.ensureActive()
                val lines = masks.mapIndexedNotNull { index, mask -> line(rotate(mask, angle))?.let { GlyphLine(index, it) } }
                if (lines.isEmpty()) continue
                val sheet = sheet(lines)
                try {
                    val text = recognizer.process(InputImage.fromBitmap(sheet, 0)).await()
                    for (block in text.textBlocks) for (textLine in block.lines) for (element in textLine.elements) {
                        for (symbol in element.symbols) {
                            val character = symbol.text
                            if (character.length != 1 || character[0].code !in 0x3400..0x9fff || symbol.confidence < .3f) continue
                            val box = symbol.boundingBox ?: continue
                            val column = floor((box.exactCenterX() - 12) / 48.0).toInt()
                            val row = floor((box.exactCenterY() - 12) / 64.0).toInt()
                            val glyphLine = lines.getOrNull(row) ?: continue
                            val tile = glyphLine.glyphs.getOrNull(column) ?: continue
                            val actual = normalized(tile)
                            val templates = templates(character)
                            val matches = (0..3).map { turn ->
                                turn * 90 to (templates.maxOfOrNull { similarity(actual, rotateSquare(it, turn)) } ?: 0.0)
                            }.sortedByDescending { it.second }
                            val best = matches[0]
                            val margin = best.second - matches[1].second
                            if (best.second < .55 || margin < .035) continue
                            val original = masks[glyphLine.region]
                            val bounds = originalBounds(tile, original, angle)
                            val vote = Vote(glyphLine.region, bounds, (angle - best.first + 360) % 360, best.second, margin)
                            // Four candidate sheets can recognize one physical glyph four times.
                            // Keep just its strongest stroke match, even when the OCR labels differ.
                            val existing = votes.indexOfFirst { it.region == vote.region && overlaps(it.bounds, bounds) }
                            if (existing < 0) votes += vote
                            else if (vote.margin * vote.score > votes[existing].margin * votes[existing].score) votes[existing] = vote
                        }
                    }
                } finally {
                    sheet.recycle()
                }
            }
        } finally {
            recognizer.close()
        }
        val scores = mutableMapOf<Int, Double>()
        val characters = mutableMapOf<Int, Int>()
        for (vote in votes) {
            scores[vote.rotation] = (scores[vote.rotation] ?: 0.0) + vote.score
            characters[vote.rotation] = (characters[vote.rotation] ?: 0) + 1
        }
        val ranked = scores.entries.sortedByDescending { it.value }
        val best = ranked.firstOrNull()
        val runner = ranked.getOrNull(1)?.value ?: 0.0
        val rotation = best?.takeIf {
            (characters[it.key] ?: 0) >= 3 && it.value >= 2.4 && it.value - runner >= 1.8 && it.value >= maxOf(.1, runner) * 1.6
        }?.key
        Evidence(rotation, scores.toMap(), characters.toMap(), masks.size)
    }

    private fun originalBounds(tile: Mask, original: Mask, angle: Int): Rect {
        val x: Int; val y: Int; val width: Int; val height: Int
        when (angle) {
            90 -> { x = tile.originY; y = original.height - tile.originX - tile.width; width = tile.height; height = tile.width }
            180 -> { x = original.width - tile.originX - tile.width; y = original.height - tile.originY - tile.height; width = tile.width; height = tile.height }
            270 -> { x = original.width - tile.originY - tile.height; y = tile.originX; width = tile.height; height = tile.width }
            else -> { x = tile.originX; y = tile.originY; width = tile.width; height = tile.height }
        }
        return Rect(x, y, x + width, y + height)
    }

    private fun overlaps(a: Rect, b: Rect): Boolean {
        val width = minOf(a.right, b.right) - maxOf(a.left, b.left)
        val height = minOf(a.bottom, b.bottom) - maxOf(a.top, b.top)
        return width > 0 && height > 0 && width * height > minOf(a.width() * a.height(), b.width() * b.height()) * .6
    }

    private fun templates(character: String): List<ByteArray> {
        templateCache.get(character)?.let { return it }
        val variants = mutableListOf<ByteArray>()
        for (locale in listOf(Locale.JAPAN, Locale.TRADITIONAL_CHINESE)) {
            for ((family, style) in listOf("sans-serif" to Typeface.NORMAL, "sans-serif" to Typeface.BOLD, "serif" to Typeface.NORMAL, "serif" to Typeface.BOLD)) {
                val bitmap = Bitmap.createBitmap(80, 80, Bitmap.Config.ARGB_8888)
                try {
                    val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                        color = Color.BLACK; textSize = 60f; typeface = Typeface.create(family, style); textLocale = locale
                    }
                    if (!paint.hasGlyph(character)) continue
                    val bounds = Rect()
                    paint.getTextBounds(character, 0, character.length, bounds)
                    val canvas = Canvas(bitmap)
                    canvas.drawColor(Color.WHITE)
                    canvas.drawText(character, (80 - bounds.width()) / 2f - bounds.left, (80 - bounds.height()) / 2f - bounds.top, paint)
                    variants += normalized(binary(bitmap))
                } finally { bitmap.recycle() }
            }
        }
        templateCache.put(character, variants)
        return variants
    }

    private fun normalized(mask: Mask): ByteArray {
        var minX = mask.width; var minY = mask.height; var maxX = -1; var maxY = -1
        for (y in 0 until mask.height) for (x in 0 until mask.width) if (mask.pixels[y * mask.width + x] == 1.toByte()) {
            minX = minOf(minX, x); minY = minOf(minY, y); maxX = maxOf(maxX, x); maxY = maxOf(maxY, y)
        }
        val result = ByteArray(1024)
        if (maxX < minX || maxY < minY) return result
        for (y in 0 until 32) for (x in 0 until 32) {
            val sx = minX + minOf(maxX - minX, ((x + .5) * (maxX - minX + 1) / 32).toInt())
            val sy = minY + minOf(maxY - minY, ((y + .5) * (maxY - minY + 1) / 32).toInt())
            result[y * 32 + x] = mask.pixels[sy * mask.width + sx]
        }
        return result
    }

    private fun rotateSquare(pixels: ByteArray, turn: Int): ByteArray {
        if (turn == 0) return pixels
        val result = ByteArray(1024)
        for (y in 0 until 32) for (x in 0 until 32) {
            val nx = when (turn) { 1 -> 31 - y; 2 -> 31 - x; else -> y }
            val ny = when (turn) { 1 -> x; 2 -> 31 - y; else -> 31 - x }
            result[ny * 32 + nx] = pixels[y * 32 + x]
        }
        return result
    }

    private fun similarity(a: ByteArray, b: ByteArray): Double {
        var intersection = 0; var total = 0
        for (i in a.indices) { intersection += a[i].toInt() and b[i].toInt(); total += a[i] + b[i] }
        return 2.0 * intersection / maxOf(1, total)
    }

    private fun binary(bitmap: Bitmap): Mask {
        val colors = IntArray(bitmap.width * bitmap.height)
        bitmap.getPixels(colors, 0, bitmap.width, 0, 0, bitmap.width, bitmap.height)
        val pixels = ByteArray(colors.size) { i ->
            val c = colors[i]
            val luminance = (Color.red(c) * 299 + Color.green(c) * 587 + Color.blue(c) * 114) / 1000
            val whiteComposite = (luminance * Color.alpha(c) + 255 * (255 - Color.alpha(c))) / 255
            if (whiteComposite < 205) 1 else 0
        }
        return Mask(bitmap.width, bitmap.height, pixels)
    }

    private suspend fun regions(image: Bitmap): List<Mask> {
        val factor = minOf(1.0, 1000.0 / maxOf(image.width, image.height))
        val scaled = Bitmap.createScaledBitmap(image, maxOf(1, (image.width * factor).toInt()), maxOf(1, (image.height * factor).toInt()), true)
        val mask = try { binary(scaled) } finally { if (scaled !== image) scaled.recycle() }
        val w = mask.width; val h = mask.height; val pixels = mask.pixels
        val labels = IntArray(pixels.size) { -1 }
        val queue = IntArray(pixels.size)
        val components = mutableListOf<Component>()
        for (p in pixels.indices) {
            if (p % w == 0) coroutineContext.ensureActive()
            if (labels[p] >= 0) continue
            val ink = pixels[p] == 1.toByte()
            val id = components.size
            var head = 0; var tail = 1
            queue[0] = p; labels[p] = id
            var x = p % w; var y = p / w; var right = x + 1; var bottom = y + 1
            while (head < tail) {
                if (head % 16384 == 0) coroutineContext.ensureActive()
                val q = queue[head++]
                x = minOf(x, q % w); y = minOf(y, q / w); right = maxOf(right, q % w + 1); bottom = maxOf(bottom, q / w + 1)
                neighbors(q, w, h) { next ->
                    if (labels[next] < 0 && pixels[next] == pixels[p]) { labels[next] = id; queue[tail++] = next }
                }
            }
            components += Component(ink, Rect(x, y, right, bottom), queue.copyOf(tail))
        }
        val groups = mutableMapOf<Int, MutableList<Int>>()
        for ((id, c) in components.withIndex()) {
            coroutineContext.ensureActive()
            if (!c.ink || c.points.size < 2 || c.bounds.width() >= w * 15 / 100 || c.bounds.height() >= h * 15 / 100) continue
            val adjacent = mutableMapOf<Int, Int>()
            for (p in c.points) neighbors(p, w, h) { n ->
                if (pixels[n] == 0.toByte()) adjacent[labels[n]] = (adjacent[labels[n]] ?: 0) + 1
            }
            val parent = adjacent.maxByOrNull { it.value }?.key ?: continue
            val area = components[parent]
            val bounds = area.bounds
            val boxArea = bounds.width() * bounds.height()
            if (area.points.size < 300 || area.points.size.toDouble() / boxArea < .5 || boxArea >= w * h / 4 ||
                c.bounds.left <= bounds.left || c.bounds.top <= bounds.top || c.bounds.right >= bounds.right || c.bounds.bottom >= bounds.bottom) continue
            groups.getOrPut(parent) { mutableListOf() } += id
        }
        val masks = mutableListOf<Mask>()
        for (key in groups.keys.sorted()) {
            val ids = groups.getValue(key)
            if (ids.size < 4) continue
            val parts = ids.map { components[it] }
            val x = parts.minOf { it.bounds.left }; val y = parts.minOf { it.bounds.top }
            val width = parts.maxOf { it.bounds.right } - x; val height = parts.maxOf { it.bounds.bottom } - y
            if (width < 10 || height < 10) continue
            val tile = Mask(width, height, ByteArray(width * height))
            for (part in parts) for (p in part.points) tile.pixels[(p / w - y) * width + p % w - x] = 1
            if (line(tile) != null) masks += tile
        }
        return masks.sortedByDescending { it.width * it.height }.take(12)
    }

    private inline fun neighbors(p: Int, w: Int, h: Int, action: (Int) -> Unit) {
        if (p % w > 0) action(p - 1)
        if (p % w + 1 < w) action(p + 1)
        if (p >= w) action(p - w)
        if (p < w * (h - 1)) action(p + w)
    }

    private fun rotate(mask: Mask, angle: Int): Mask {
        if (angle == 0) return mask
        val w = if (angle == 180) mask.width else mask.height
        val h = if (angle == 180) mask.height else mask.width
        val result = Mask(w, h, ByteArray(w * h))
        for (y in 0 until mask.height) for (x in 0 until mask.width) {
            val nx = when (angle) { 90 -> mask.height - 1 - y; 180 -> mask.width - 1 - x; else -> y }
            val ny = when (angle) { 90 -> x; 180 -> mask.height - 1 - y; else -> mask.width - 1 - x }
            result.pixels[ny * w + nx] = mask.pixels[y * mask.width + x]
        }
        return result
    }

    private fun bands(projection: BooleanArray): List<IntRange> {
        val result = mutableListOf<IntRange>()
        var start = -1
        for (i in projection.indices) {
            if (projection[i] && start < 0) start = i
            if (!projection[i] && start >= 0) { result += start until i; start = -1 }
        }
        if (start >= 0) result += start until projection.size
        return result
    }

    private fun line(mask: Mask): List<Mask>? {
        val xs = BooleanArray(mask.width); val ys = BooleanArray(mask.height)
        for (y in 0 until mask.height) for (x in 0 until mask.width) if (mask.pixels[y * mask.width + x] == 1.toByte()) { xs[x] = true; ys[y] = true }
        val xb = bands(xs); val yb = bands(ys)
        val sizes = (xb + yb).map { it.last - it.first + 1 }.filter { it >= 5 }.sorted()
        if (sizes.isEmpty()) return null
        val pitch = sizes[sizes.size / 2].toDouble()
        fun merged(bands: List<IntRange>): List<IntRange> {
            val result = mutableListOf<IntRange>()
            for (band in bands) {
                val previous = result.lastOrNull()
                if (previous != null && previous.last - previous.first + 1 < pitch * .65 && band.last - previous.first + 1 <= pitch * 1.4) {
                    result[result.lastIndex] = previous.first..band.last
                } else result += band
            }
            return result
        }
        val glyphs = mutableListOf<Mask>()
        for (x in merged(xb).reversed()) for (y in merged(yb)) {
            val width = x.last - x.first + 1; val height = y.last - y.first + 1
            if (width.toDouble() / height !in .6..1.65 || minOf(width, height) < pitch * .6) continue
            val pixels = ByteArray(width * height)
            for (row in y) System.arraycopy(mask.pixels, row * mask.width + x.first, pixels, (row - y.first) * width, width)
            val coverage = pixels.count { it == 1.toByte() }.toDouble() / pixels.size
            if (coverage !in .08.. .80) continue
            glyphs += Mask(width, height, pixels, x.first, y.first)
        }
        return glyphs.takeIf { it.size in 3..24 }
    }

    private fun sheet(lines: List<GlyphLine>): Bitmap {
        val result = Bitmap.createBitmap(24 + 48 * lines.maxOf { it.glyphs.size }, 24 + lines.size * 64, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(result)
        canvas.drawColor(Color.WHITE)
        val paint = Paint(Paint.FILTER_BITMAP_FLAG)
        for ((row, line) in lines.withIndex()) for ((column, glyph) in line.glyphs.withIndex()) {
            val colors = IntArray(glyph.pixels.size) { if (glyph.pixels[it] == 1.toByte()) Color.BLACK else Color.WHITE }
            val tile = Bitmap.createBitmap(colors, glyph.width, glyph.height, Bitmap.Config.ARGB_8888)
            try {
                val left = 12 + column * 48; val top = 12 + row * 64
                canvas.drawBitmap(tile, null, Rect(left, top, left + 40, top + 40), paint)
            } finally { tile.recycle() }
        }
        return result
    }
}
