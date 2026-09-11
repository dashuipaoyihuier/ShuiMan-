package com.mandu.reader.data

import java.math.BigInteger

/** Locale-independent ordering for page and archive names (1, 2, 10). */
internal object NaturalOrder : Comparator<String> {
    override fun compare(left: String, right: String): Int {
        var li = 0
        var ri = 0
        while (li < left.length && ri < right.length) {
            val lc = left[li]
            val rc = right[ri]
            if (lc.isDigit() && rc.isDigit()) {
                val ls = li
                val rs = ri
                while (li < left.length && left[li].isDigit()) li++
                while (ri < right.length && right[ri].isDigit()) ri++
                val lDigits = left.substring(ls, li)
                val rDigits = right.substring(rs, ri)
                val numberOrder = BigInteger(lDigits).compareTo(BigInteger(rDigits))
                if (numberOrder != 0) return numberOrder
                // Equal numeric values: fewer leading zeroes first, then continue.
                if (lDigits.length != rDigits.length) return lDigits.length.compareTo(rDigits.length)
                continue
            }
            val folded = lc.lowercaseChar().compareTo(rc.lowercaseChar())
            if (folded != 0) return folded
            li++
            ri++
        }
        val lengthOrder = (left.length - li).compareTo(right.length - ri)
        return if (lengthOrder != 0) lengthOrder else left.compareTo(right)
    }
}

internal val IMAGE_EXTENSIONS = setOf(
    "jpg", "jpeg", "png", "gif", "tif", "tiff", "bmp", "heic", "heif", "webp"
)

internal fun extension(name: String): String =
    name.substringAfterLast('.', missingDelimiterValue = "").lowercase()

internal fun isImageName(name: String): Boolean = extension(name) in IMAGE_EXTENSIONS

