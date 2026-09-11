package com.mandu.reader

import android.net.Uri
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.mandu.reader.core.ReadingState
import com.mandu.reader.data.BookRepository
import com.mandu.reader.data.ReadingStateWriter
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class ReadingStateWriterDeviceTest {
    @Test
    fun newestSessionAndSnapshotWinAndCompletedPairMarksBookRead() = runBlocking {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val fixtures = FixtureFactory.create(context)
        val repository = BookRepository(context)
        val record = repository.importFile(Uri.fromFile(fixtures.cbz))
        try {
            val units = repository.open(record).use { it.publication.units }
            val staleWriter = ReadingStateWriter(repository, record.id)
            staleWriter.enqueue(ReadingState(units[0].locator))

            val currentWriter = ReadingStateWriter(repository, record.id)
            repeat(50) { index ->
                currentWriter.enqueue(ReadingState(units[index % 2].locator))
            }
            // A disposed/older reader cannot overwrite the active reader session.
            staleWriter.enqueue(ReadingState(units[0].locator))
            // The visible final group is [unit 1, unit 2], while its stable anchor is unit 1.
            currentWriter.enqueue(ReadingState(units[1].locator), completed = true)
            currentWriter.flush()

            val reopened = BookRepository(context)
            assertEquals(units[1].locator, reopened.loadState(record.id).locator)
            val savedBook = reopened.books().single { it.id == record.id }
            assertEquals(savedBook.pageCount, savedBook.progress)
            assertEquals("已读", savedBook.readStatus)
            assertTrue(savedBook.lastOpened > 0)
        } finally {
            repository.remove(record.id)
            fixtures.root.deleteRecursively()
        }
    }
}
