package com.mandu.reader.data

import android.graphics.Bitmap
import android.graphics.Color
import mil.nga.tiff.FieldType
import mil.nga.tiff.Rasters
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.zip.Inflater
import kotlin.math.ceil
import kotlin.math.sqrt

/** Classic TIFF directories keep their physical order, including unsupported pages. */
internal object TiffDecoder {
    data class Page(val width: Int, val height: Int, val error: String? = null)
    private const val MAX_FILE_BYTES = 256 * 1024 * 1024
    private const val MAX_BLOCK_BYTES = 16 * 1024 * 1024
    private const val MAX_OUTPUT_PIXELS = 12_000_000
    private val numericTags = setOf(256, 257, 258, 259, 262, 266, 273, 274, 277, 278, 279, 284, 317, 320, 322, 323, 324, 325, 338, 339)
    private data class Directory(val tags: Map<Int, LongArray>, val malformed: String? = null) {
        fun value(tag: Int, default: Long = 0): Long = tags[tag]?.firstOrNull() ?: default
        val width get() = value(256).coerceIn(0, Int.MAX_VALUE.toLong()).toInt()
        val height get() = value(257).coerceIn(0, Int.MAX_VALUE.toLong()).toInt()
        val orientation get() = value(274, 1).toInt()
        val samples get() = value(277, 1).toInt()
        val bits get() = tags[258]?.map { it.toInt() } ?: listOf(1)
        val tiled get() = tags.containsKey(324)
        val blockWidth get() = if (tiled) value(322).toInt() else width
        val blockHeight get() = if (tiled) value(323).toInt() else minOf(value(278, height.toLong()), height.toLong()).toInt()
        val offsets get() = tags[if (tiled) 324 else 273] ?: longArrayOf()
        val counts get() = tags[if (tiled) 325 else 279] ?: longArrayOf()
        fun page(error: String?): Page = if (orientation in 5..8) Page(height, width, error) else Page(width, height, error)
    }

    fun pages(bytes: ByteArray): List<Page> = parse(bytes).second.map { directory ->
        directory.page(runCatching { validate(directory, bytes.size) }.exceptionOrNull()?.message)
    }

    /** Output and raster windows are sampled/bounded; the complete source raster is never allocated. */
    fun render(bytes: ByteArray, pageIndex: Int, maxSide: Int): Bitmap {
        require(maxSide > 0) { "TIFF 解码尺寸必须大于零" }
        val (order, directories) = parse(bytes)
        val page = directories.getOrNull(pageIndex) ?: error("TIFF 页码超出范围")
        validate(page, bytes.size)
        val side = minOf(maxSide, 8192)
        val step = maxOf(1, ceil(maxOf(page.width, page.height).toDouble() / side).toInt(), ceil(sqrt(page.width.toDouble() * page.height / MAX_OUTPUT_PIXELS)).toInt())
        val sampledWidth = (page.width + step - 1) / step
        val sampledHeight = (page.height + step - 1) / step
        val outWidth = if (page.orientation in 5..8) sampledHeight else sampledWidth
        val outHeight = if (page.orientation in 5..8) sampledWidth else sampledHeight
        val output = IntArray(outWidth * outHeight)
        val photo = page.value(262).toInt()
        val bitDepth = page.bits.first()
        val maximum = (1 shl bitDepth) - 1
        val palette = page.tags[320]
        val paletteSize = 1 shl bitDepth
        val colorSamples = if (photo == 2) 3 else 1
        val alphaType = page.tags[338]?.firstOrNull()?.toInt() ?: 0
        val blockWidth = page.blockWidth
        val blockHeight = page.blockHeight
        // Decode one strip/tile at a time. Codec output has a hard byte ceiling;
        // NGA's typed raster access wraps that block without copying a full image.
        var top = 0
        while (top < page.height) {
            val bottom = minOf(page.height, top + blockHeight)
            var left = 0
            while (left < page.width) {
                val right = minOf(page.width, left + blockWidth)
                val firstY = (top + step - 1) / step
                val lastY = (bottom + step - 1) / step
                val firstX = (left + step - 1) / step
                val lastX = (right + step - 1) / step
                if (firstX < lastX && firstY < lastY) {
                    val raster = readBlock(page, bytes, order, left / blockWidth, top / blockHeight)
                    for (oy in firstY until lastY) for (ox in firstX until lastX) {
                        val x = ox * step - left; val y = oy * step - top
                        fun sample(index: Int) = raster.getPixelSample(index, x, y).toInt()
                        fun channel(index: Int) = (sample(index).toLong() * 255 / maximum).toInt().coerceIn(0, 255)
                        var r: Int; var g: Int; var b: Int
                        when (photo) {
                            2 -> { r = channel(0); g = channel(1); b = channel(2) }
                            3 -> {
                                val index = sample(0)
                                require(index in 0 until paletteSize) { "TIFF 调色板索引无效" }
                                r = (palette!![index] * 255 / 65535).toInt()
                                g = (palette[index + paletteSize] * 255 / 65535).toInt()
                                b = (palette[index + paletteSize * 2] * 255 / 65535).toInt()
                            }
                            else -> { r = if (photo == 0) 255 - channel(0) else channel(0); g = r; b = r }
                        }
                        val alpha = if (page.samples > colorSamples && alphaType in 1..2) channel(colorSamples) else 255
                        if (alphaType == 1 && alpha in 1..254) {
                            r = (r * 255 / alpha).coerceAtMost(255); g = (g * 255 / alpha).coerceAtMost(255); b = (b * 255 / alpha).coerceAtMost(255)
                        }
                        val dx: Int; val dy: Int
                        when (page.orientation) {
                            2 -> { dx = sampledWidth - 1 - ox; dy = oy }
                            3 -> { dx = sampledWidth - 1 - ox; dy = sampledHeight - 1 - oy }
                            4 -> { dx = ox; dy = sampledHeight - 1 - oy }
                            5 -> { dx = oy; dy = ox }
                            6 -> { dx = sampledHeight - 1 - oy; dy = ox }
                            7 -> { dx = sampledHeight - 1 - oy; dy = sampledWidth - 1 - ox }
                            8 -> { dx = oy; dy = sampledWidth - 1 - ox }
                            else -> { dx = ox; dy = oy }
                        }
                        output[dy * outWidth + dx] = Color.argb(alpha, r, g, b)
                    }
                }
                left = right
            }
            top = bottom
        }
        return Bitmap.createBitmap(output, outWidth, outHeight, Bitmap.Config.ARGB_8888)
    }

    private fun validate(d: Directory, fileSize: Int) {
        require(d.malformed == null) { d.malformed ?: "TIFF 目录无效" }
        require(d.width in 1..100_000 && d.height in 1..100_000 && d.width.toLong() * d.height <= 512_000_000) { "TIFF 页面尺寸无效或超出解码预算" }
        require(d.value(259, 1) in listOf(1L, 5L, 8L, 32946L, 32773L)) { "此 TIFF 页的压缩方式暂不支持（CCITT/JPEG 等），已保留页位" }
        require(d.samples in 1..4 && d.bits.size in listOf(1, d.samples)) { "TIFF 像素通道声明无效" }
        require(d.bits.all { it in listOf(1, 2, 4, 8, 16) && it == d.bits.first() } && (d.bits.first() >= 8 || d.samples == 1)) { "此 TIFF 页像素位深暂不支持，已保留页位" }
        require((d.tags[339] ?: longArrayOf(1)).all { it == 1L }) { "此 TIFF 页的浮点或有符号像素暂不支持" }
        require(d.tags.containsKey(262) && d.value(262) in 0L..3L) { "此 TIFF 页的颜色空间缺失或暂不支持" }
        require(d.value(262) != 2L || d.samples >= 3) { "TIFF RGB 通道不足" }
        require(d.value(262) != 3L || (d.samples == 1 && d.tags[320]?.size == 3 * (1 shl d.bits.first()))) { "TIFF 调色板缺失或长度无效" }
        require(d.value(284, 1) in 1L..2L && d.value(266, 1) == 1L && d.orientation in 1..8) { "TIFF 像素排列或朝向暂不支持" }
        require(d.value(317, 1) in 1L..2L) { "TIFF 预测编码暂不支持" }
        require(d.bits.first() >= 8 || d.value(317, 1) == 1L) { "TIFF 低位深水平预测编码暂不支持" }
        require(d.blockWidth in 1..100_000 && d.blockHeight in 1..100_000) { "TIFF 条带/图块尺寸无效" }
        val blockBytes = d.blockWidth.toLong() * d.blockHeight * d.samples * maxOf(8, d.bits.first()) / 8
        require(blockBytes <= MAX_BLOCK_BYTES) { "TIFF 单条带/图块超出 16 MiB 解码预算" }
        val blocks = ((d.width.toLong() + d.blockWidth - 1) / d.blockWidth) * ((d.height.toLong() + d.blockHeight - 1) / d.blockHeight) * if (d.value(284, 1) == 2L) d.samples else 1
        require(blocks <= 1_000_000 && d.offsets.size.toLong() == blocks && d.counts.size.toLong() == blocks) { "TIFF 条带/图块索引数量无效" }
        for (i in d.offsets.indices) require(d.offsets[i] >= 8 && d.counts[i] in 1..MAX_BLOCK_BYTES.toLong() && d.offsets[i] + d.counts[i] <= fileSize) { "TIFF 条带/图块数据缺失或超出预算" }
    }

    private fun readBlock(d: Directory, bytes: ByteArray, order: ByteOrder, column: Int, row: Int): Rasters {
        val width = d.blockWidth
        val height = if (d.tiled) d.blockHeight else minOf(d.blockHeight, d.height - row * d.blockHeight)
        val columns = (d.width + d.blockWidth - 1) / d.blockWidth
        val rows = (d.height + d.blockHeight - 1) / d.blockHeight
        val planar = d.value(284, 1) == 2L
        val samples = if (planar) 1 else d.samples
        val bits = d.bits.first()
        val rowBytes = (width * samples * bits + 7) / 8
        val expected = rowBytes * height
        val buffers = Array(if (planar) d.samples else 1) { channel ->
            val index = channel * columns * rows + row * columns + column
            val decoded = decompress(bytes, d.offsets[index].toInt(), d.counts[index].toInt(), d.value(259, 1).toInt(), expected)
            val unpacked = if (bits >= 8) decoded else ByteArray(width * height) { pixel ->
                val y = pixel / width; val x = pixel % width
                ((decoded[y * rowBytes + x * bits / 8].toInt() ushr (8 - bits - x * bits % 8)) and ((1 shl bits) - 1)).toByte()
            }
            val buffer = ByteBuffer.wrap(unpacked).order(order)
            if (d.value(317, 1) == 2L) {
                require(bits >= 8) { "TIFF 低位深水平预测编码暂不支持" }
                val sampleBytes = bits / 8
                for (y in 0 until height) for (x in 1 until width) for (s in 0 until samples) {
                    val p = ((y * width + x) * samples + s) * sampleBytes
                    val previous = p - samples * sampleBytes
                    if (bits == 8) buffer.put(p, (buffer.get(p) + buffer.get(previous)).toByte())
                    else buffer.putShort(p, (buffer.getShort(p) + buffer.getShort(previous)).toShort())
                }
            }
            buffer
        }
        val types = Array(d.samples) { if (bits == 16) FieldType.SHORT else FieldType.BYTE }
        return if (planar) Rasters(width, height, types, buffers) else Rasters(width, height, types, buffers[0])
    }

    /** Fixed-size output prevents compressed TIFF strips from expanding beyond their declared raster. */
    private fun decompress(source: ByteArray, offset: Int, count: Int, compression: Int, expected: Int): ByteArray {
        require(expected in 1..MAX_BLOCK_BYTES) { "TIFF 条带解码超过预算" }
        if (compression == 1) {
            require(count >= expected) { "TIFF 原始条带截断" }
            return source.copyOfRange(offset, offset + expected)
        }
        val output = ByteArray(expected)
        var written = 0
        when (compression) {
            8, 32946 -> {
                val inflater = Inflater()
                try {
                    inflater.setInput(source, offset, count)
                    while (!inflater.finished() && written < expected) {
                        val size = inflater.inflate(output, written, expected - written)
                        require(size > 0 || inflater.finished()) { "TIFF Deflate 数据截断或无效" }
                        written += size
                    }
                    if (!inflater.finished()) {
                        require(inflater.inflate(ByteArray(1)) == 0 && inflater.finished()) { "TIFF Deflate 展开超过声明尺寸" }
                    }
                } finally { inflater.end() }
            }
            32773 -> {
                var cursor = offset
                val end = offset + count
                while (cursor < end) {
                    val control = source[cursor++].toInt()
                    if (control == -128) continue
                    val length = if (control >= 0) control + 1 else 1 - control
                    require(written + length <= expected) { "TIFF PackBits 展开超过声明尺寸" }
                    if (control >= 0) {
                        require(cursor + length <= end) { "TIFF PackBits 条带截断" }
                        System.arraycopy(source, cursor, output, written, length); cursor += length
                    } else {
                        require(cursor < end) { "TIFF PackBits 条带截断" }
                        output.fill(source[cursor++], written, written + length)
                    }
                    written += length
                }
            }
            5 -> {
                val prefixes = IntArray(4096)
                val suffixes = ByteArray(4096)
                val stack = ByteArray(4096)
                var bitOffset = 0; var codeWidth = 9; var next = 258; var previous = -1; var first = 0
                fun code(): Int {
                    require(bitOffset + codeWidth <= count * 8) { "TIFF LZW 条带截断" }
                    var result = 0
                    repeat(codeWidth) {
                        result = (result shl 1) or ((source[offset + bitOffset / 8].toInt() ushr (7 - bitOffset % 8)) and 1)
                        bitOffset++
                    }
                    return result
                }
                while (true) {
                    val current = code()
                    if (current == 257) break
                    if (current == 256) { codeWidth = 9; next = 258; previous = -1; continue }
                    var value = current
                    var length = 0
                    if (value == next && previous >= 0) { stack[length++] = first.toByte(); value = previous }
                    else require(value < next) { "TIFF LZW 字典索引无效" }
                    while (value >= 258) {
                        require(value < next && length < stack.size - 1) { "TIFF LZW 字典无效" }
                        stack[length++] = suffixes[value]; value = prefixes[value]
                    }
                    require(value in 0..255) { "TIFF LZW 代码无效" }
                    first = value
                    stack[length++] = first.toByte()
                    require(written + length <= expected) { "TIFF LZW 展开超过声明尺寸" }
                    for (i in length - 1 downTo 0) output[written++] = stack[i]
                    if (previous >= 0 && next < 4096) {
                        prefixes[next] = previous; suffixes[next] = first.toByte(); next++
                        // TIFF uses EarlyChange=1, unlike GIF's LZW bit-width transition.
                        if (next == (1 shl codeWidth) - 1 && codeWidth < 12) codeWidth++
                    }
                    previous = current
                }
            }
            else -> error("TIFF 压缩方式暂不支持")
        }
        require(written == expected) { "TIFF 条带像素数据不足" }
        return output
    }

    /** Minimal bounded classic-TIFF IFD walk; no pixel decoding during library indexing. */
    private fun parse(bytes: ByteArray): Pair<ByteOrder, List<Directory>> {
        require(bytes.size in 8..MAX_FILE_BYTES) { "TIFF 文件无效或超过 256 MiB" }
        val order = when {
            bytes[0] == 73.toByte() && bytes[1] == 73.toByte() -> ByteOrder.LITTLE_ENDIAN
            bytes[0] == 77.toByte() && bytes[1] == 77.toByte() -> ByteOrder.BIG_ENDIAN
            else -> error("TIFF 字节序无效")
        }
        val buffer = ByteBuffer.wrap(bytes).order(order)
        fun short(offset: Int) = buffer.getShort(offset).toInt() and 0xffff
        fun long(offset: Int) = buffer.getInt(offset).toLong() and 0xffffffffL
        require(short(2) == 42) { "BigTIFF 或未知 TIFF 版本暂不支持" }
        var offset = long(4)
        val visited = mutableSetOf<Long>()
        val directories = mutableListOf<Directory>()
        while (offset != 0L) {
            require(directories.size < 10_000 && visited.add(offset)) { "TIFF 目录循环或页数超过限制" }
            if (offset < 8 || offset + 2 > bytes.size) {
                directories += Directory(emptyMap(), "TIFF 页目录缺失"); break
            }
            val count = short(offset.toInt())
            val end = offset + 2 + count * 12L
            if (end + 4 > bytes.size) { directories += Directory(emptyMap(), "TIFF 页目录截断"); break }
            val tags = mutableMapOf<Int, LongArray>()
            var pageError: String? = null
            var totalValues = 0L
            for (i in 0 until count) {
                val start = offset.toInt() + 2 + i * 12
                val tag = short(start)
                if (tag !in numericTags) continue
                try {
                    require(!tags.containsKey(tag)) { "TIFF 目录包含重复字段" }
                    val type = short(start + 2)
                    val size = when (type) { 1 -> 1; 3 -> 2; 4 -> 4; else -> error("TIFF 数值字段类型无效") }
                    val values = long(start + 4)
                    totalValues += values
                    require(values in 1..1_000_000 && totalValues <= 1_000_000) { "TIFF 目录数据超过预算" }
                    val data = if (values * size <= 4) (start + 8).toLong() else long(start + 8)
                    require(data >= 0 && data + values * size <= bytes.size) { "TIFF 目录字段数据缺失" }
                    tags[tag] = LongArray(values.toInt()) { j ->
                        val p = data.toInt() + j * size
                        when (type) { 1 -> (bytes[p].toInt() and 0xff).toLong(); 3 -> short(p).toLong(); else -> long(p) }
                    }
                } catch (exception: IllegalArgumentException) { pageError = exception.message }
                catch (exception: IllegalStateException) { pageError = exception.message }
            }
            directories += Directory(tags, pageError)
            offset = long(end.toInt())
        }
        require(directories.isNotEmpty()) { "TIFF 没有页面" }
        return order to directories
    }
}
