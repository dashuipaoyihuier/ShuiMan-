package com.mandu.reader.core

import org.junit.Assert.*
import org.junit.Test

class ComicLibraryTest {
    private fun book(id: String, series: String = "银河", volume: Double? = null) =
        BookRecord(id, "file:///$id.cbz", "第${id}卷", BookKind.CBZ, series, volume)

    @Test fun existingRecordsGroupWithoutMergingVolumes() {
        val input = listOf(book("10", volume = 10.0), book("2", volume = 2.0), book("1", volume = 1.0))
        val group = ComicLibrary.series(input).single()
        assertEquals("银河", group.title)
        assertEquals(listOf("1", "2", "10"), group.volumes.map { it.id })
        assertEquals(input.toSet(), group.volumes.toSet())
    }

    @Test fun blankSeriesDoNotBecomeOneFalseComic() {
        assertTrue(ComicLibrary.series(listOf(book("1", ""), book("2", " "))).isEmpty())
    }

    @Test fun identityNormalizesWidthCaseAndSurroundingWhitespaceOnly() {
        val groups = ComicLibrary.series(listOf(book("1", " ABC "), book("2", "ＡＢＣ"), book("3", "AB C")))
        assertEquals(2, groups.size)
        assertEquals(2, groups.single { it.key == "abc" }.volumes.size)
    }

    @Test fun unknownAndDuplicateVolumeNumbersAreRetainedInStableOrder() {
        val input = listOf(book("10"), book("2"), book("b", volume = 1.0), book("a", volume = 1.0))
        assertEquals(listOf("a", "b", "2", "10"), ComicLibrary.series(input).single().volumes.map { it.id })
    }

    @Test fun resumeUsesMostRecentUnfinishedAvailableVolume() {
        val input = listOf(book("1").copy(lastOpened = 20), book("2").copy(lastOpened = 30, readStatus = "已读"),
            book("3").copy(lastOpened = 40, missing = true), book("4").copy(lastOpened = 10))
        assertEquals("1", ComicLibrary.series(input).single().resume?.id)
    }
}
