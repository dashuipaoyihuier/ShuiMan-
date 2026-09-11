package com.mandu.reader

import android.net.Uri
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import com.mandu.reader.core.BookRecord
import com.mandu.reader.core.ReaderPreferences
import com.mandu.reader.core.ReadingLayout
import com.mandu.reader.core.ReadingState
import com.mandu.reader.data.BookRepository
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class ComicLibraryDeviceTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)
    private val repository by lazy { BookRepository(context) }
    private lateinit var fixture: FixtureFactory.Set
    private val records = mutableListOf<BookRecord>()
    private var scenario: ActivityScenario<MainActivity>? = null
    private val series = "银河-${UUID.randomUUID().toString().take(8)}"

    @Before fun prepare() = runBlocking {
        device.wakeUp()
        fixture = FixtureFactory.create(context)
        for (number in listOf(10, 2, 1)) {
            val file = File(fixture.root, "银河卷$number.cbz")
            fixture.cbz.copyTo(file)
            val book = repository.importFile(Uri.fromFile(file)).copy(series = series, volume = number.toDouble())
            repository.save(book)
            repository.saveState(book.id, ReadingState(preferences = ReaderPreferences(
                layout = ReadingLayout.SINGLE, smartSpreads = false, automaticOrientation = false,
                automaticPairs = false, autoHideControls = false)))
            records += book
        }
        scenario = ActivityScenario.launch(MainActivity::class.java)
        visible(By.text("浏览")).click()
        visible(By.clazz("android.widget.EditText")).text = series
        device.settleLibrarySearch()
    }

    @After fun clean() = runBlocking {
        scenario?.close()
        records.forEach { repository.remove(it.id) }
        if (::fixture.isInitialized) fixture.root.deleteRecursively()
    }

    @Test fun seriesFirstThenSortedVolumesAndReturnToTheSameSeries() = runBlocking {
        assertEquals(1, device.wait(Until.findObjects(By.desc("查看系列$series")), 10_000).size)
        assertFalse("Volumes must not be flat cards on the main shelf", device.hasObject(By.desc("打开银河卷2")))
        visible(By.desc("查看系列$series")).click()
        visible(By.text("3 卷 · 按卷号排序"))
        val first = visible(By.desc("打开银河卷1")).visibleBounds
        val second = visible(By.desc("打开银河卷2")).visibleBounds
        assertTrue("Volume 1 must precede volume 2", first.top < second.top || first.top == second.top && first.left < second.left)
        visible(By.desc("打开银河卷2")).click()
        visible(By.text("第 1 / 3 页"))
        visible(By.desc("下一页")).click()
        visible(By.text("第 2 / 3 页"))
        device.pressBack()
        visible(By.text("3 卷 · 按卷号排序"))
        visible(By.text("继续阅读：银河卷2"))
        scenario!!.recreate()
        visible(By.text("3 卷 · 按卷号排序"))
        device.pressBack()
        visible(By.desc("查看系列$series"))
        assertEquals(series, visible(By.clazz("android.widget.EditText")).text)
        assertEquals(3, repository.books(false).count { it.series == series })
        assertEquals("2.png", repository.loadState(records.single { it.volume == 2.0 }.id).locator?.resource)
        capture("series-shelf")
        visible(By.desc("查看系列$series")).click()
        visible(By.text("3 卷 · 按卷号排序"))
        capture("series-volumes")
    }

    private fun visible(selector: androidx.test.uiautomator.BySelector) =
        device.wait(Until.findObject(selector), 15_000) ?: run {
            capture("failure")
            throw AssertionError("Missing control: $selector")
        }

    private fun capture(name: String) {
        device.waitForIdle(1_000)
        android.os.SystemClock.sleep(250)
        val folder = File(context.getExternalFilesDir(null), "qa/library").apply { mkdirs() }
        device.takeScreenshot(File(folder, "$name-${device.displayWidth}x${device.displayHeight}.png"))
    }
}
