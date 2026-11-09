package com.mandu.reader

import java.io.ByteArrayOutputStream

/** Original generated PalmDB/MOBI, shared by parser and device regressions. */
object MobiFixture {
    fun put(data: ByteArray, offset: Int, width: Int, value: Int) {
        repeat(width) { data[offset + it] = (value ushr ((width - it - 1) * 8)).toByte() }
    }
    fun book(
        html: String = "<img recindex='3'><img recindex='1'><img recindex='3'><img recindex='99'>",
        compressed: Boolean = false, trailer: Boolean = false,
        images: List<ByteArray> = listOf(byteArrayOf(1), byteArrayOf(2), byteArrayOf(3))
    ): ByteArray {
        val original = html.toByteArray()
        val text = ByteArrayOutputStream()
        var index = 0
        while (index < original.size) {
            var length = 0; var distance = 0
            if (compressed) for (candidate in 1..minOf(index, 2047)) {
                var count = 0
                while (count < 10 && index + count < original.size && original[index + count] == original[index + count - candidate]) count++
                if (count >= 3 && count > length) { length = count; distance = candidate }
            }
            if (length >= 3) {
                val value = 0x8000 or (distance shl 3) or (length - 3)
                text.write(value ushr 8); text.write(value and 255); index += length
            } else {
                val value = original[index++].toInt() and 255
                if (compressed && (value >= 128 || value in 1..8)) text.write(1)
                text.write(value)
            }
        }
        if (trailer) { text.write(0); text.write(0x81) }
        val header = ByteArray(304)
        put(header, 0, 2, if (compressed) 2 else 1); put(header, 4, 4, original.size)
        put(header, 8, 2, 1); put(header, 10, 2, 4096)
        "MOBI".toByteArray().copyInto(header, 16)
        put(header, 20, 4, 264); put(header, 24, 4, 2); put(header, 28, 4, 65001)
        put(header, 36, 4, 6); put(header, 108, 4, 2); put(header, 128, 4, 0x40)
        put(header, 240, 4, if (trailer) 3 else 0)
        "EXTH".toByteArray().copyInto(header, 280)
        put(header, 284, 4, 24); put(header, 288, 4, 1)
        put(header, 292, 4, 201); put(header, 296, 4, 12); put(header, 300, 4, 1)
        val records = listOf(header, text.toByteArray()) + images
        val pdb = ByteArray(78 + records.size * 8 + 2)
        "BOOKMOBI".toByteArray().copyInto(pdb, 60); put(pdb, 76, 2, records.size)
        var offset = pdb.size
        records.forEachIndexed { i, bytes -> put(pdb, 78 + i * 8, 4, offset); offset += bytes.size }
        return ByteArrayOutputStream().apply { write(pdb); records.forEach { write(it) } }.toByteArray()
    }
}
