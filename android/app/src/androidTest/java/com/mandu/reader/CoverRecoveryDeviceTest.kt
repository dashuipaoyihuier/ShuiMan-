package com.mandu.reader

import android.graphics.BitmapFactory
import android.graphics.Color
import android.net.Uri
import android.os.SystemClock
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import com.mandu.reader.core.BookRecord
import com.mandu.reader.core.ReadingState
import com.mandu.reader.core.SourceLocator
import com.mandu.reader.data.BookRepository
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

@RunWith(AndroidJUnit4::class)
class CoverRecoveryDeviceTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val repository by lazy { BookRepository(context) }
    private lateinit var fixtures: FixtureFactory.Set
    private val ids = mutableListOf<String>()
    private var scenario: ActivityScenario<MainActivity>? = null
    @Before fun prepare() { fixtures = FixtureFactory.create(context) }
    @After fun cleanup(): Unit = runBlocking {
        scenario?.close()
        repository.books(false).filter { it.id in ids }.forEach { it.coverPath?.let(::File)?.delete(); repository.remove(it.id) }
        fixtures.root.deleteRecursively()
    }
    private suspend fun add(file: File, index: Boolean = true): BookRecord = repository.importFile(Uri.fromFile(file),index).also { ids += it.id }
    private fun assertCover(book: BookRecord) {
        assertNotNull(book.coverPath)
        assertTrue(File(book.coverPath!!).isFile)
        val image = BitmapFactory.decodeFile(book.coverPath)
        assertNotNull(image)
        image.recycle()
    }
    @Test fun interruptedImportsDeletedAndCorruptCoversRecoverWithoutChangingUserState() = runBlocking {
        val original = add(fixtures.cbz, false).copy(title="COVER-KEEP",favorite=true,tags=listOf("保留"))
        repository.save(original)
        val state = ReadingState(locator=SourceLocator("2.png",1))
        repository.saveState(original.id,state)
        repository.repairMissingCovers()
        val first = repository.books(false).single { it.id == original.id }
        assertCover(first)
        assertEquals(File(context.filesDir,"covers"),File(first.coverPath!!).parentFile)
        File(first.coverPath).delete()
        repository.repairMissingCovers()
        val second = repository.books(false).single { it.id == original.id }
        assertCover(second); assertNotEquals(first.coverPath,second.coverPath)
        File(second.coverPath!!).writeBytes(byteArrayOf(1,2,3))
        repository.repairMissingCovers()
        val third = repository.books(false).single { it.id == original.id }
        assertCover(third); assertNotEquals(second.coverPath,third.coverPath)
        assertEquals("COVER-KEEP",third.title); assertTrue(third.favorite); assertEquals(listOf("保留"),third.tags)
        assertEquals(state,repository.loadState(original.id))
    }
    @Test fun epubUsesDeclaredCoverOutsideSpineEvenWhenBodyNeedsWebLayout() = runBlocking {
        val file = File(fixtures.root,"cover-metadata.epub")
        zip(file,linkedMapOf(
            "META-INF/container.xml" to """<container><rootfiles><rootfile full-path="book.opf"/></rootfiles></container>""".toByteArray(),
            "book.opf" to """<package version="3.0"><metadata><title>Original cover fixture</title></metadata><manifest><item id="cover" href="art.png" media-type="image/png" properties="cover-image"/><item id="body" href="body.xhtml" media-type="application/xhtml+xml"/></manifest><spine><itemref idref="body"/></spine></package>""".toByteArray(),
            "body.xhtml" to "<html><body><p>Original text-only page.</p></body></html>".toByteArray(),
            "art.png" to fixtures.image.readBytes()))
        val book = add(file)
        assertCover(book)
        repository.open(book).use {
            assertEquals(1,it.publication.units.size)
            assertEquals("body.xhtml",it.publication.units.single().locator.resource)
            assertTrue(it.publication.units.single().complex)
        }
    }
    @Test fun damagedFirstImageDoesNotPreventAValidLaterCover() = runBlocking {
        val file = File(fixtures.root,"broken-first.cbz")
        zip(file,linkedMapOf("1.png" to fixtures.image.readBytes().copyOf(33), "2.png" to fixtures.image.readBytes()))
        val book = add(file)
        assertCover(book)
        repository.open(book).use { assertEquals(2,it.publication.units.size) }
    }
    @Test fun enteringExistingLibraryAutomaticallyRestoresAndDisplaysMissingCover() = runBlocking {
        val device = UiDevice.getInstance(instrumentation)
        device.wakeUp()
        val book = add(fixtures.image,false).copy(title="COVER-${System.nanoTime()}",series="")
        repository.save(book)
        scenario = ActivityScenario.launch(MainActivity::class.java)
        val deadline = SystemClock.uptimeMillis()+15000
        var repaired: BookRecord? = null
        while (SystemClock.uptimeMillis()<deadline) {
            repaired = repository.books(false).firstOrNull { it.id == book.id && it.coverPath != null }
            if (repaired != null) break
            SystemClock.sleep(100)
        }
        assertNotNull("Existing shelf must repair covers without reimport",repaired)
        assertCover(repaired!!)
        device.wait(Until.findObject(By.text("浏览")),10000)!!.click()
        device.wait(Until.findObject(By.clazz("android.widget.EditText")),10000)!!.text = book.title
        device.settleLibrarySearch()
        val card = device.wait(Until.findObject(By.desc("打开${book.title}")),10000)!!
        val bounds = card.visibleBounds
        var displayed = false
        for (attempt in 0 until 30) {
            val image = instrumentation.uiAutomation.takeScreenshot()!!
            val pixel = image.getPixel(bounds.centerX(),bounds.centerY()); image.recycle()
            if (Color.red(pixel)>150 && Color.green(pixel)<100 && Color.blue(pixel)<100) { displayed=true; break }
            SystemClock.sleep(100)
        }
        assertTrue("The card must display the restored red cover, not just update the database",displayed)
    }
    private fun zip(file: File, entries: Map<String,ByteArray>) {
        ZipOutputStream(file.outputStream()).use { out -> entries.forEach { (path,bytes) ->
            out.putNextEntry(ZipEntry(path)); out.write(bytes); out.closeEntry()
        } }
    }
}
