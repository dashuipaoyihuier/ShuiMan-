package com.mandu.reader

import android.graphics.Color
import android.net.Uri
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.mandu.reader.core.BookKind
import com.mandu.reader.core.PageOverride
import com.mandu.reader.core.ReaderPreferences
import com.mandu.reader.core.ReadingDirection
import com.mandu.reader.core.ReadingLayout
import com.mandu.reader.core.ReadingState
import com.mandu.reader.data.BookRepository
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Format-contract checks against files created during the test. These intentionally
 * use file:// URIs for target-cache fixtures; production document access remains SAF.
 */
@RunWith(AndroidJUnit4::class)
class FormatDeviceTest {
    private val context get() = InstrumentationRegistry.getInstrumentation().targetContext
    private lateinit var fixtures: FixtureFactory.Set

    @Before
    fun createFixtures() {
        fixtures = FixtureFactory.create(context)
    }

    @After
    fun removeFixtures() {
        if (::fixtures.isInitialized) {
            runBlocking {
                val repository = BookRepository(context)
                val prefix = Uri.fromFile(fixtures.root).toString() + "/"
                repository.books().filter { it.uri.startsWith(prefix) }.forEach { repository.remove(it.id) }
            }
            fixtures.root.deleteRecursively()
        }
    }

    @Test
    fun importsAndRendersOriginalBitmapPdfAndCbzWithNaturalOrder() = runBlocking {
        val repository = BookRepository(context)

        val imageRecord = repository.importFile(Uri.fromFile(fixtures.image))
        assertEquals(BookKind.IMAGES, imageRecord.kind)
        repository.open(imageRecord).use { imageBook ->
            assertEquals(1, imageBook.publication.units.size)
            assertDominant(imageBook.render(0, 64).getPixel(12, 18), red = true)
        }

        val pdfRecord = repository.importFile(Uri.fromFile(fixtures.pdf))
        assertEquals(BookKind.PDF, pdfRecord.kind)
        repository.open(pdfRecord).use { pdfBook ->
            assertEquals(2, pdfBook.publication.units.size)
            val rendered = pdfBook.render(1, 64)
            assertTrue(rendered.width > 0 && rendered.height > 0)
            assertDominant(rendered.getPixel(rendered.width / 2, rendered.height / 2), blue = true)
        }

        val cbzRecord = repository.importFile(Uri.fromFile(fixtures.cbz))
        assertEquals(BookKind.CBZ, cbzRecord.kind)
        repository.open(cbzRecord).use { cbzBook ->
            assertEquals(listOf("1.png", "2.png", "10.png"), cbzBook.publication.units.map { it.locator.resource })
            assertDominant(cbzBook.render(0, 64).getPixel(12, 18), red = true)
            assertDominant(cbzBook.render(1, 64).getPixel(12, 18), green = true)
            assertDominant(cbzBook.render(2, 64).getPixel(12, 18), blue = true)
        }
    }

    @Test
    fun epubSpineKeepsDuplicatesMissingSlotsAndComplexContentRoute() = runBlocking {
        val repository = BookRepository(context)
        val record = repository.importFile(Uri.fromFile(fixtures.epub))
        assertEquals(BookKind.EPUB, record.kind)

        repository.open(record).use { book ->
            val publication = book.publication
            assertEquals(5, publication.units.size)
            assertEquals(ReadingDirection.RTL, publication.direction)
            assertEquals(
                listOf("OPS/cover.xhtml", "OPS/normal.xhtml", "OPS/normal.xhtml", "OPS/missing.xhtml", "OPS/complex.xhtml"),
                publication.units.map { it.locator.resource }
            )
            assertTrue(publication.units[0].isCover)
            assertTrue(publication.units[1].id != publication.units[2].id)
            assertNotNull(publication.units[3].error)
            assertTrue(publication.units[4].complex)
            assertNotNull(book.resource("OPS/images/page.png"))
            assertDominant(book.render(1, 64).getPixel(12, 18), blue = true)
        }
    }

    @Test
    fun stateWithStableDuplicateLocatorOverridesAndBookmarksSurvivesRepositoryReopen() = runBlocking {
        val firstRepository = BookRepository(context)
        val record = firstRepository.importFile(Uri.fromFile(fixtures.epub))
        val publication = firstRepository.open(record).use { it.publication }
        val selected = publication.units[2]
        val override = PageOverride(
            rotation = 90,
            standalone = true,
            joinNext = false,
            earlierOnRight = true,
            pairOffset = 0.03,
            pairScale = 1.04,
            pairingBreak = true
        )
        val expected = ReadingState(
            locator = selected.locator,
            preferences = ReaderPreferences(direction = ReadingDirection.RTL, layout = ReadingLayout.DOUBLE),
            overrides = mapOf(selected.id to override),
            bookmarks = listOf(publication.units[1].locator, selected.locator),
            scrollOffset = 17
        )
        firstRepository.saveState(record.id, expected)

        val reopenedRepository = BookRepository(context)
        assertTrue(reopenedRepository.books().any { it.id == record.id })
        assertEquals(expected, reopenedRepository.loadState(record.id))
    }

    private fun assertDominant(color: Int, red: Boolean = false, green: Boolean = false, blue: Boolean = false) {
        val channels = listOf(Color.red(color), Color.green(color), Color.blue(color))
        if (red) assertTrue("expected red-dominant pixel: $channels", channels[0] > channels[1] && channels[0] > channels[2])
        if (green) assertTrue("expected green-dominant pixel: $channels", channels[1] > channels[0] && channels[1] > channels[2])
        if (blue) assertTrue("expected blue-dominant pixel: $channels", channels[2] > channels[0] && channels[2] > channels[1])
        assertFalse("no dominant colour requested", !red && !green && !blue)
    }
}
