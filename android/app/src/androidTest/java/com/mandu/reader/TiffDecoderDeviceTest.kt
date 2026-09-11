package com.mandu.reader

import android.graphics.Color
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.mandu.reader.data.TiffDecoder
import mil.nga.tiff.compression.LZWCompression
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.zip.DeflaterOutputStream

@RunWith(AndroidJUnit4::class)
class TiffDecoderDeviceTest {
    @Test
    fun keepsPageOrderAndUnsupportedMiddlePageWithoutBlockingLaterFrames() {
        val bytes = fixture(listOf(rgb(Color.RED), rgb(Color.GREEN).copy(compression = 4), rgb(Color.BLUE)))
        val original = bytes.copyOf()
        val pages = TiffDecoder.pages(bytes)
        assertEquals(3, pages.size)
        assertNull(pages[0].error)
        assertNotNull(pages[1].error)
        assertNull(pages[2].error)
        assertPixel(bytes, 0, Color.RED)
        assertPixel(bytes, 2, Color.BLUE)
        assertTrue(runCatching { TiffDecoder.render(bytes, 1, 100) }.isFailure)
        assertArrayEquals("TIFF decoding must not alter source bytes", original, bytes)
    }

    @Test
    fun readsRawPackBitsDeflateAndLzwInEitherByteOrder() {
        for (order in listOf(ByteOrder.LITTLE_ENDIAN, ByteOrder.BIG_ENDIAN)) {
            for (compression in listOf(1, 5, 8, 32773, 32946)) {
                val bytes = fixture(listOf(rgb(Color.rgb(17, 89, 203)).copy(compression = compression)), order)
                assertNull("order=$order compression=$compression", TiffDecoder.pages(bytes).single().error)
                assertPixel(bytes, 0, Color.rgb(17, 89, 203))
            }
        }
    }

    @Test
    fun rendersSixteenBitGrayWhiteIsZeroRgbAndPalette() {
        val palette = IntArray(256 * 3).apply { this[7] = 65535; this[256 + 7] = 32768 }
        val pages = listOf(
            Frame(3, 2, 1, 16, 1, IntArray(6) { 32768 }),
            Frame(3, 2, 1, 16, 0, IntArray(6) { 0 }),
            Frame(3, 2, 3, 16, 2, IntArray(18) { if (it % 3 == 1) 65535 else 0 }),
            Frame(3, 2, 1, 8, 3, IntArray(6) { 7 }, palette = palette)
        )
        for (order in listOf(ByteOrder.LITTLE_ENDIAN, ByteOrder.BIG_ENDIAN)) {
            val bytes = fixture(pages, order)
            assertTrue(TiffDecoder.pages(bytes).all { it.error == null })
            assertPixel(bytes, 0, Color.rgb(127, 127, 127))
            assertPixel(bytes, 1, Color.WHITE)
            assertPixel(bytes, 2, Color.GREEN)
            assertPixel(bytes, 3, Color.rgb(255, 127, 0))
        }
    }

    @Test
    fun normalizesAllEightTiffOrientationsExactlyOnce() {
        // Asymmetric corners independently identify every rotation and reflection.
        val colors = intArrayOf(Color.RED, Color.GREEN, Color.BLUE, Color.YELLOW, Color.CYAN, Color.MAGENTA)
        val values = colors.flatMap { listOf(Color.red(it), Color.green(it), Color.blue(it)) }.toIntArray()
        val frame = Frame(3, 2, 3, 8, 2, values)
        val expected = listOf(
            colors,
            intArrayOf(colors[2], colors[1], colors[0], colors[5], colors[4], colors[3]),
            colors.reversedArray(),
            intArrayOf(colors[3], colors[4], colors[5], colors[0], colors[1], colors[2]),
            intArrayOf(colors[0], colors[3], colors[1], colors[4], colors[2], colors[5]),
            intArrayOf(colors[3], colors[0], colors[4], colors[1], colors[5], colors[2]),
            intArrayOf(colors[5], colors[2], colors[4], colors[1], colors[3], colors[0]),
            intArrayOf(colors[2], colors[5], colors[1], colors[4], colors[0], colors[3])
        )
        for (orientation in 1..8) {
            val bytes = fixture(listOf(frame.copy(orientation = orientation)))
            val page = TiffDecoder.pages(bytes).single()
            val bitmap = TiffDecoder.render(bytes, 0, 100)
            try {
                assertEquals(if (orientation < 5) 3 else 2, page.width)
                assertEquals(page.width, bitmap.width)
                assertEquals(page.height, bitmap.height)
                val actual = IntArray(6)
                bitmap.getPixels(actual, 0, bitmap.width, 0, 0, bitmap.width, bitmap.height)
                assertArrayEquals("orientation=$orientation", expected[orientation - 1], actual)
            } finally { bitmap.recycle() }
        }
    }

    @Test
    fun samplesLargeImageAndRejectsInvalidBudgetsOrMissingStrip() {
        val bytes = fixture(listOf(Frame(1000, 600, 1, 8, 1, IntArray(600_000) { 192 })))
        val bitmap = TiffDecoder.render(bytes, 0, 100)
        try {
            assertEquals(100, bitmap.width)
            assertEquals(60, bitmap.height)
            assertEquals(Color.rgb(192, 192, 192), bitmap.getPixel(99, 59))
        } finally { bitmap.recycle() }
        assertTrue(runCatching { TiffDecoder.render(bytes, 0, 0) }.isFailure)
        assertTrue(runCatching { TiffDecoder.render(bytes, 1, 100) }.isFailure)
        val truncated = bytes.copyOf(bytes.size - 1)
        assertNotNull(TiffDecoder.pages(truncated).single().error)
        assertTrue(runCatching { TiffDecoder.render(truncated, 0, 100) }.isFailure)
    }

    @Test
    fun packedGrayscalePreservesRowPaddingAndCompressedExpansionIsBounded() {
        for (bits in listOf(1, 2, 4)) {
            val maximum = (1 shl bits) - 1
            val pixels = IntArray(27) { if (it % 2 == 0) maximum else 0 }
            val bytes = fixture(listOf(Frame(9, 3, 1, bits, 1, pixels)))
            assertNull(TiffDecoder.pages(bytes).single().error)
            val bitmap = TiffDecoder.render(bytes, 0, 100)
            try {
                for (y in 0 until 3) for (x in 0 until 9) {
                    assertEquals(if ((y * 9 + x) % 2 == 0) Color.WHITE else Color.BLACK, bitmap.getPixel(x, y))
                }
            } finally { bitmap.recycle() }
        }
        for (compression in listOf(5, 8, 32773)) {
            // Well-formed compressed stream deliberately expands beyond the IFD's declared raster.
            val inconsistent = rgb(Color.RED).copy(width = 1, height = 1, compression = compression)
            val bytes = fixture(listOf(inconsistent, rgb(Color.BLUE)))
            assertTrue("compression=$compression must reject excess expansion", runCatching { TiffDecoder.render(bytes, 0, 100) }.isFailure)
            assertPixel(bytes, 1, Color.BLUE)
        }
    }

    @Test
    fun lzwCodeWidthTransitionsMatchIndependentNgaDecoder() {
        // Exercise EOI immediately before/after width changes and ClearCode at saturation.
        for (length in listOf(253, 254, 255, 765, 766, 767, 1789, 1790, 1791, 3836, 3837, 3838, 5000, 8000)) {
            val boundary = ByteArray(length) { (it * 31).toByte() }
            assertArrayEquals("LZW boundary length=$length", boundary, LZWCompression().decode(lzwLiterals(boundary), ByteOrder.LITTLE_ENDIAN))
        }
        val raw = ByteArray(5000) { (it * 31).toByte() }
        val compressed = lzwLiterals(raw)
        assertArrayEquals(raw, LZWCompression().decode(compressed, ByteOrder.LITTLE_ENDIAN))
        val bytes = fixture(listOf(Frame(100, 50, 1, 8, 1, IntArray(raw.size) { raw[it].toInt() and 255 }, compression = 5)))
        val bitmap = TiffDecoder.render(bytes, 0, 100)
        try {
            for (y in 0 until 50) for (x in 0 until 100) {
                val value = raw[y * 100 + x].toInt() and 255
                assertEquals(Color.rgb(value, value, value), bitmap.getPixel(x, y))
            }
        } finally { bitmap.recycle() }
    }

    private fun assertPixel(bytes: ByteArray, index: Int, color: Int) {
        val bitmap = TiffDecoder.render(bytes, index, 100)
        try { assertEquals(color, bitmap.getPixel(bitmap.width / 2, bitmap.height / 2)) }
        finally { bitmap.recycle() }
    }

    private data class Frame(
        val width: Int, val height: Int, val samples: Int, val bits: Int, val photo: Int, val pixels: IntArray,
        val compression: Int = 1, val orientation: Int = 1, val palette: IntArray? = null
    )
    private data class Entry(val tag: Int, val type: Int, val values: IntArray)
    private fun rgb(color: Int) = Frame(6, 4, 3, 8, 2, IntArray(72) { when (it % 3) { 0 -> Color.red(color); 1 -> Color.green(color); else -> Color.blue(color) } })

    /** Small independent classic TIFF writer, including a literal-only TIFF-LZW stream. */
    private fun fixture(frames: List<Frame>, order: ByteOrder = ByteOrder.LITTLE_ENDIAN): ByteArray {
        val strips = frames.map { frame ->
            val raw = if (frame.bits >= 8) {
                ByteBuffer.allocate(frame.pixels.size * frame.bits / 8).order(order).also { buffer ->
                    for (value in frame.pixels) if (frame.bits == 16) buffer.putShort(value.toShort()) else buffer.put(value.toByte())
                }
            } else {
                val rowBytes = (frame.width * frame.bits + 7) / 8
                ByteBuffer.allocate(rowBytes * frame.height).also { buffer ->
                    for ((i, value) in frame.pixels.withIndex()) {
                        val x = i % frame.width; val y = i / frame.width
                        val p = y * rowBytes + x * frame.bits / 8
                        buffer.put(p, (buffer.get(p).toInt() or (value shl (8 - frame.bits - x * frame.bits % 8))).toByte())
                    }
                }
            }
            when (frame.compression) {
                5 -> lzwLiterals(raw.array())
                8, 32946 -> ByteArrayOutputStream().also { output -> DeflaterOutputStream(output).use { it.write(raw.array()) } }.toByteArray()
                32773 -> ByteArrayOutputStream().also { output ->
                    var p = 0
                    while (p < raw.capacity()) {
                        val size = minOf(128, raw.capacity() - p)
                        output.write(size - 1); output.write(raw.array(), p, size); p += size
                    }
                }.toByteArray()
                else -> raw.array()
            }
        }
        val tables = frames.mapIndexed { i, frame ->
            mutableListOf(
                Entry(256, 4, intArrayOf(frame.width)), Entry(257, 4, intArrayOf(frame.height)),
                Entry(258, 3, IntArray(frame.samples) { frame.bits }), Entry(259, 3, intArrayOf(frame.compression)),
                Entry(262, 3, intArrayOf(frame.photo)), Entry(273, 4, intArrayOf(0)),
                Entry(274, 3, intArrayOf(frame.orientation)), Entry(277, 3, intArrayOf(frame.samples)),
                Entry(278, 4, intArrayOf(frame.height)), Entry(279, 4, intArrayOf(strips[i].size)),
                Entry(284, 3, intArrayOf(1)), Entry(339, 3, intArrayOf(1))
            ).apply { frame.palette?.let { add(Entry(320, 3, it)) } }.sortedBy { it.tag }
        }
        val offsets = IntArray(frames.size)
        var total = 8
        for ((i, table) in tables.withIndex()) { offsets[i] = total; total += 2 + table.size * 12 + 4 }
        val valuesStart = total
        for (table in tables) for (entry in table) {
            val size = entry.values.size * if (entry.type == 3) 2 else 4
            if (size > 4) total += size
        }
        for ((i, table) in tables.withIndex()) { table.first { it.tag == 273 }.values[0] = total; total += strips[i].size }
        val buffer = ByteBuffer.allocate(total).order(order)
        buffer.put(if (order == ByteOrder.LITTLE_ENDIAN) 'I'.code.toByte() else 'M'.code.toByte())
        buffer.put(buffer.get(0)); buffer.putShort(42); buffer.putInt(offsets.first())
        var extra = valuesStart
        for ((i, table) in tables.withIndex()) {
            buffer.position(offsets[i]); buffer.putShort(table.size.toShort())
            for (entry in table) {
                buffer.putShort(entry.tag.toShort()); buffer.putShort(entry.type.toShort()); buffer.putInt(entry.values.size)
                val size = entry.values.size * if (entry.type == 3) 2 else 4
                val field = buffer.position()
                if (size > 4) { buffer.putInt(extra); buffer.position(extra) }
                for (value in entry.values) if (entry.type == 3) buffer.putShort(value.toShort()) else buffer.putInt(value)
                if (size > 4) extra += size
                buffer.position(field + 4)
            }
            buffer.putInt(offsets.getOrNull(i + 1) ?: 0)
            buffer.position(table.first { it.tag == 273 }.values[0]); buffer.put(strips[i])
        }
        return buffer.array()
    }

    private fun lzwLiterals(raw: ByteArray): ByteArray {
        val output = ByteArrayOutputStream()
        var accumulator = 0; var pending = 0
        fun write(code: Int, width: Int) {
            accumulator = (accumulator shl width) or code; pending += width
            while (pending >= 8) { pending -= 8; output.write(accumulator ushr pending) }
            accumulator = accumulator and ((1 shl pending) - 1)
        }
        fun width(index: Int) = when { index < 254 -> 9; index < 766 -> 10; index < 1790 -> 11; else -> 12 }
        write(256, 9)
        var segmentLength = 0
        for (value in raw) {
            // TIFF 6 pp.60–61 requires clearing at dictionary saturation; a frozen
            // full GIF-style table would make conforming TIFF readers expect 13 bits.
            if (segmentLength == 3837) { write(256, 12); segmentLength = 0 }
            write(value.toInt() and 255, width(segmentLength++))
        }
        write(257, width(segmentLength))
        if (pending > 0) output.write(accumulator shl (8 - pending))
        return output.toByteArray()
    }
}
