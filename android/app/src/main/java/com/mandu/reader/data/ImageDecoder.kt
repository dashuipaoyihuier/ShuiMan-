package com.mandu.reader.data

import android.content.ContentResolver
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.net.Uri
import android.util.LruCache
import androidx.exifinterface.media.ExifInterface
import java.io.ByteArrayInputStream
import java.io.InputStream
import kotlin.math.max
import kotlin.math.roundToInt

internal data class ImageInfo(val width: Int, val height: Int, val orientation: Int)

internal object ImageDecoder {
    private const val MAX_EDGE = 8_192
    private const val MAX_PIXELS = 48_000_000L
    private val cache = object : LruCache<String, Bitmap>(128 * 1024) {
        override fun sizeOf(key: String, value: Bitmap): Int =
            (value.allocationByteCount / 1024).coerceAtLeast(1)
    }

    fun info(open: () -> InputStream): ImageInfo {
        val options = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        open().use { BitmapFactory.decodeStream(it, null, options) }
        if (options.outWidth <= 0 || options.outHeight <= 0) throw Exception("无法识别图片。")
        val orientation = runCatching { open().use { ExifInterface(it).rotationTag() } }.getOrDefault(0)
        val sideways = orientation == ExifInterface.ORIENTATION_ROTATE_90 ||
            orientation == ExifInterface.ORIENTATION_ROTATE_270 ||
            orientation == ExifInterface.ORIENTATION_TRANSPOSE ||
            orientation == ExifInterface.ORIENTATION_TRANSVERSE
        return if (sideways) ImageInfo(options.outHeight, options.outWidth, orientation)
        else ImageInfo(options.outWidth, options.outHeight, orientation)
    }

    fun info(bytes: ByteArray): ImageInfo = info { ByteArrayInputStream(bytes) }

    fun info(resolver: ContentResolver, uri: Uri): ImageInfo = info {
        resolver.openInputStream(uri) ?: throw Exception("无法读取图片。")
    }

    fun decode(key: String, maxEdge: Int, open: () -> InputStream): Bitmap {
        val target = maxEdge.coerceIn(64, MAX_EDGE)
        val cacheKey = "$key:$target"
        synchronized(cache) { cache.get(cacheKey)?.let { return it } }
        val info = info(open)
        var sample = 1
        while (max(info.width, info.height) / (sample * 2) >= target) sample *= 2
        val options = BitmapFactory.Options().apply {
            inSampleSize = sample
            inPreferredConfig = Bitmap.Config.ARGB_8888
        }
        val decoded = open().use { BitmapFactory.decodeStream(it, null, options) }
            ?: throw Exception("图片解码失败。")
        var result = applyOrientation(decoded, info.orientation)
        val largest = max(result.width, result.height)
        if (largest > target || result.width.toLong() * result.height > MAX_PIXELS) {
            val edgeScale = target.toDouble() / largest
            val pixelScale = kotlin.math.sqrt(MAX_PIXELS.toDouble() / (result.width.toLong() * result.height))
            val scale = minOf(1.0, edgeScale, pixelScale)
            val resized = Bitmap.createScaledBitmap(
                result,
                (result.width * scale).roundToInt().coerceAtLeast(1),
                (result.height * scale).roundToInt().coerceAtLeast(1),
                true
            )
            if (resized !== result && result !== decoded) result.recycle()
            if (resized !== decoded) decoded.recycle()
            result = resized
        }
        synchronized(cache) { cache.put(cacheKey, result) }
        return result
    }

    fun decode(resolver: ContentResolver, uri: Uri, maxEdge: Int): Bitmap =
        decode(uri.toString(), maxEdge) {
            resolver.openInputStream(uri) ?: throw Exception("无法读取图片。")
        }

    fun decode(bytes: ByteArray, key: String, maxEdge: Int): Bitmap =
        decode(key, maxEdge) { ByteArrayInputStream(bytes) }

    private fun applyOrientation(source: Bitmap, orientation: Int): Bitmap {
        val matrix = Matrix()
        when (orientation) {
            ExifInterface.ORIENTATION_FLIP_HORIZONTAL -> matrix.setScale(-1f, 1f)
            ExifInterface.ORIENTATION_ROTATE_180 -> matrix.setRotate(180f)
            ExifInterface.ORIENTATION_FLIP_VERTICAL -> matrix.setScale(1f, -1f)
            ExifInterface.ORIENTATION_TRANSPOSE -> {
                matrix.setRotate(90f)
                matrix.postScale(-1f, 1f)
            }
            ExifInterface.ORIENTATION_ROTATE_90 -> matrix.setRotate(90f)
            ExifInterface.ORIENTATION_TRANSVERSE -> {
                matrix.setRotate(-90f)
                matrix.postScale(-1f, 1f)
            }
            ExifInterface.ORIENTATION_ROTATE_270 -> matrix.setRotate(270f)
            else -> return source
        }
        return Bitmap.createBitmap(source, 0, 0, source.width, source.height, matrix, true).also {
            if (it !== source) source.recycle()
        }
    }

    private fun ExifInterface.rotationTag(): Int =
        getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL)
}
