package com.mandu.reader.data

import com.mandu.reader.MobiFixture
import org.junit.Assert.*
import org.junit.Test
import java.io.File

class MobiParserTest {
    private fun parse(bytes: ByteArray, check: (MobiParser) -> Unit) {
        val file = File.createTempFile("mobi-original-", ".mobi")
        try { file.writeBytes(bytes); MobiParser(file).use(check) } finally { file.delete() }
    }
    @Test fun bodyOrderCoverDuplicatesAndMissingSlots() {
        for (compressed in listOf(false, true)) parse(MobiFixture.book(compressed = compressed, trailer = compressed)) {
            assertEquals(listOf(3, 4, 2, 4, null), it.pages.map { page -> page.record })
            assertEquals(listOf(-1, 0, 1, 2, 3), it.pages.map { page -> page.occurrence })
            assertTrue(it.pages[0].cover)
            assertArrayEquals(byteArrayOf(3), it.record(4))
        }
    }
    @Test fun bodyCoverIsNotInsertedAgain() {
        parse(MobiFixture.book(html = "<img recindex='2'><img recindex='1'>")) {
            assertEquals(2, it.pages.size); assertTrue(it.pages[0].cover)
        }
    }
    @Test fun rejectsUnsupportedAndMalformedBooks() {
        val original = MobiFixture.book()
        for ((offset, width, value) in listOf(Triple(132,2,1), Triple(156,4,8), Triple(120,2,17480),
            Triple(78,4,1), Triple(148,4,42), Triple(128,2,65535), Triple(124,4,-1))) {
            val bad = original.clone(); MobiFixture.put(bad, offset, width, value)
            assertThrows(IllegalArgumentException::class.java) { parse(bad) { fail("accepted invalid MOBI") } }
        }
        assertThrows(IllegalArgumentException::class.java) { parse(original.copyOf(90)) {} }
        assertThrows(IllegalArgumentException::class.java) { parse(MobiFixture.book(html="<p>Story text</p><img recindex='1'>")) {} }
    }
    @Test fun palmDocSupportsOverlapSpaceAndLiteralsAndRejectsInvalidReferences() {
        assertArrayEquals("abababab ".toByteArray() + byteArrayOf(0x80.toByte()),
            MobiParser.decompress(byteArrayOf(97,98,0x80.toByte(),0x13,32,1,0x80.toByte())))
        assertArrayEquals(" A".toByteArray(), MobiParser.decompress(byteArrayOf(0xC1.toByte())))
        for (bytes in listOf(byteArrayOf(0x80.toByte(),0), byteArrayOf(0x80.toByte()), byteArrayOf(3,1))) {
            assertThrows(IllegalArgumentException::class.java) { MobiParser.decompress(bytes) }
        }
    }
}
