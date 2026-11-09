package com.mandu.reader.data

import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.Rect
import com.mandu.reader.core.BookKind
import com.mandu.reader.core.DisplayGroup
import com.mandu.reader.core.OpenBook
import com.mandu.reader.core.PagePixelSize
import com.mandu.reader.core.Publication
import com.mandu.reader.core.ReaderPreferences
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import kotlin.math.ceil

internal data class ReaderPageImage(val bitmap: Bitmap, val sourceSize: PagePixelSize)

/** Display-only transforms: analysis, thumbnails and source files retain the complete page. */
internal object ReaderPageRenderer {
    fun shouldCrop(publication: Publication, group: DisplayGroup, preferences: ReaderPreferences): Boolean =
        publication.kind == BookKind.PDF && preferences.cropPDFWhiteMargins && !(group.spread && group.indices.size == 2)

    suspend fun render(book: OpenBook, index: Int, maxEdge: Int, rotation: Int, crop: Boolean): ReaderPageImage =
        withContext(Dispatchers.IO) {
            val original = book.render(index, maxEdge)
            val bounds = if (crop && book.publication.kind == BookKind.PDF) whiteMarginBounds(original)
                else Rect(0, 0, original.width, original.height)
            val trimmed = if (bounds.width() == original.width && bounds.height() == original.height) original
                else Bitmap.createBitmap(original, bounds.left, bounds.top, bounds.width(), bounds.height())
            val angle = Math.floorMod(rotation, 360)
            val bitmap = if (angle == 0) trimmed else Bitmap.createBitmap(trimmed, 0, 0, trimmed.width, trimmed.height,
                Matrix().apply { postRotate(angle.toFloat()) }, true)
            val unit = book.publication.units[index]
            val width = (unit.width.takeIf { it > 0 } ?: original.width).toDouble() * bounds.width() / original.width
            val height = (unit.height.takeIf { it > 0 } ?: original.height).toDouble() * bounds.height() / original.height
            ReaderPageImage(bitmap, if (angle % 180 == 90) PagePixelSize(height, width) else PagePixelSize(width, height))
        }

    internal suspend fun whiteMarginBounds(bitmap: Bitmap): Rect {
        val width = bitmap.width
        val height = bitmap.height
        val full = Rect(0, 0, width, height)
        // Read every pixel with one row of scratch memory, including thin/faint edge captions.
        val row = IntArray(width)
        var left = width; var right = -1; var top = height; var bottom = -1
        for (y in 0 until height) {
            currentCoroutineContext().ensureActive()
            bitmap.getPixels(row, 0, width, 0, y, width, 1)
            for (x in row.indices) {
                val pixel = row[x]
                if (Color.red(pixel) < 250 || Color.green(pixel) < 250 || Color.blue(pixel) < 250) {
                    left = minOf(left, x); right = maxOf(right, x)
                    top = minOf(top, y); bottom = maxOf(bottom, y)
                }
            }
        }
        if (right < left || bottom < top || (right-left+1).toLong() * (bottom-top+1) < width.toLong() * height * .03) return full
        val margin = maxOf(2, ceil(maxOf(width, height) * .005).toInt())
        return Rect(maxOf(0, left-margin), maxOf(0, top-margin), minOf(width, right+margin+1), minOf(height, bottom+margin+1))
    }
}
