package com.mandu.reader.data

import org.junit.Assert.assertEquals
import org.junit.Test

class NaturalOrderTest {
    @Test
    fun numericSegmentsDeterminePageOrder() {
        val values = listOf("page10.png", "page02.png", "page2.png", "page1.png", "Page11.png")
        assertEquals(
            listOf("page1.png", "page2.png", "page02.png", "page10.png", "Page11.png"),
            values.sortedWith(NaturalOrder)
        )
    }

    @Test
    fun archiveResolutionUsesDocumentDirectoryAndDoesNotDecodePlusAsSpace() {
        assertEquals("OPS/images/a+b.png", ZipArchive.resolve("../images/a+b.png#part", "OPS/pages/p.xhtml"))
    }
}
