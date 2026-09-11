package com.mandu.reader

import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Color
import android.net.Uri
import android.os.SystemClock
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.BySelector
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.UiObject2
import androidx.test.uiautomator.Until
import com.mandu.reader.core.BookRecord
import com.mandu.reader.core.PageTurnMode
import com.mandu.reader.core.ReaderPreferences
import com.mandu.reader.core.ReadingDirection
import com.mandu.reader.core.ReadingLayout
import com.mandu.reader.core.ReadingState
import com.mandu.reader.data.BookRepository
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Verifies the three touch page-turn modes against the pixels actually rendered, so the
 * result does not depend on any control being visible. The fixture pages are solid red,
 * green and blue, which makes the visible page unambiguous.
 */
@RunWith(AndroidJUnit4::class)
class PageTurnModeDeviceTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)
    private lateinit var fixtures: FixtureFactory.Set
    private lateinit var repository: BookRepository
    private lateinit var record: BookRecord
    private var scenario: ActivityScenario<MainActivity>? = null

    private enum class Page(val label: String) { ONE("第 1 / 3 页"), TWO("第 2 / 3 页"), THREE("第 3 / 3 页"), NONE("") }

    @Before fun prepare(): Unit = runBlocking {
        device.wakeUp()
        fixtures = FixtureFactory.create(context)
        repository = BookRepository(context)
        record = repository.importFile(Uri.fromFile(fixtures.cbz)).let {
            it.copy(title = "TURN-${it.id.take(8)}").also { unique -> repository.save(unique) }
        }
    }

    @After fun finish(): Unit = runBlocking {
        scenario?.close()
        if (::record.isInitialized) repository.remove(record.id)
        if (::fixtures.isInitialized) fixtures.root.deleteRecursively()
    }

    @Test fun tapModeTurnsPagesFollowingMangaOrder() {
        openReader(PageTurnMode.TAP, ReadingDirection.RTL)
        assertEquals(Page.ONE, visiblePage())
        clickAt(0.15f, 0.5f)   // RTL: the next page sits on the left
        assertEquals(Page.TWO, visiblePage())
        clickAt(0.15f, 0.5f)
        assertEquals(Page.THREE, visiblePage())
        clickAt(0.85f, 0.5f)   // and the previous page on the right
        assertEquals(Page.TWO, visiblePage())
    }

    @Test fun tapModeMirrorsForLeftToRightBooks() {
        openReader(PageTurnMode.TAP, ReadingDirection.LTR)
        assertEquals(Page.ONE, visiblePage())
        clickAt(0.85f, 0.5f)
        assertEquals(Page.TWO, visiblePage())
        clickAt(0.15f, 0.5f)
        assertEquals(Page.ONE, visiblePage())
    }

    @Test fun swipeModeTurnsPagesFollowingMangaOrder() {
        openReader(PageTurnMode.SWIPE, ReadingDirection.RTL)
        assertEquals(Page.ONE, visiblePage())
        swipeHorizontally(fromFraction = 0.2f, toFraction = 0.8f)   // RTL: drag the right-hand page in
        assertEquals(Page.TWO, visiblePage())
        swipeHorizontally(fromFraction = 0.2f, toFraction = 0.8f)
        assertEquals(Page.THREE, visiblePage())
        swipeHorizontally(fromFraction = 0.8f, toFraction = 0.2f)
        assertEquals(Page.TWO, visiblePage())
    }

    @Test fun swipeModeMirrorsForLeftToRightBooks() {
        openReader(PageTurnMode.SWIPE, ReadingDirection.LTR)
        assertEquals(Page.ONE, visiblePage())
        swipeHorizontally(fromFraction = 0.8f, toFraction = 0.2f)
        assertEquals(Page.TWO, visiblePage())
        swipeHorizontally(fromFraction = 0.2f, toFraction = 0.8f)
        assertEquals(Page.ONE, visiblePage())
    }

    @Test fun shortSwipeDoesNotTurnThePage() {
        openReader(PageTurnMode.SWIPE, ReadingDirection.RTL)
        assertEquals(Page.ONE, visiblePage())
        // Below the 10% threshold, a drag must not jump a page.
        device.swipe((device.displayWidth * 0.50f).toInt(), (device.displayHeight * 0.5f).toInt(),
            (device.displayWidth * 0.55f).toInt(), (device.displayHeight * 0.5f).toInt(), 10)
        SystemClock.sleep(900)
        assertEquals(Page.ONE, visiblePage())
    }

    @Test fun verticalScrollModeMovesThroughTheStrip() {
        openReader(PageTurnMode.SCROLL, ReadingDirection.RTL)
        assertEquals(Page.ONE, visiblePage())
        val width = device.displayWidth
        val height = device.displayHeight
        // Webtoon reading: swipe upwards to travel down the strip. A fitted page can be taller
        // than the viewport, so travel until the strip has actually advanced.
        // Stay clear of the bottom bar, which owns its own vertical drags.
        var moved = false
        for (attempt in 0 until 8) {
            device.swipe(width / 2, (height * 0.72f).toInt(), width / 2, (height * 0.16f).toInt(), 20)
            SystemClock.sleep(700)
            if (visiblePage() != Page.ONE) { moved = true; break }
        }
        assertTrue("Continuous mode must scroll away from the first page", moved)
    }

    @Test fun touchReadingHidesTheChromeOnItsOwn() {
        openReader(PageTurnMode.TAP, ReadingDirection.RTL, autoHide = true)
        assertTrue("Controls start visible", device.hasObject(By.text(Page.ONE.label)))
        // No interaction: the chrome must retreat by itself.
        val hidden = device.wait(Until.gone(By.text(Page.ONE.label)), 12_000)
        assertTrue("Reading chrome must auto-hide during touch reading", hidden)
        clickAt(0.5f, 0.5f)
        assertTrue("A centre tap brings the chrome back", device.wait(Until.findObject(By.text(Page.ONE.label)), 5_000) != null)
    }

    @Test fun autoHideCanBeTurnedOff() {
        openReader(PageTurnMode.TAP, ReadingDirection.RTL, autoHide = false)
        SystemClock.sleep(6_000)
        assertTrue("Controls must stay when auto-hide is off", device.hasObject(By.text(Page.ONE.label)))
    }

    @Test fun hiddenChromeFitsTheWholePageWithinANarrowInset() {
        openReader(PageTurnMode.TAP, ReadingDirection.RTL, autoHide = false)
        // Ordinary inner page; cover-alone/confirmed spread groups deliberately have zero inset.
        visible(By.desc("下一页")).click()
        visible(By.text(Page.TWO.label))
        clickAt(.5f, .5f)
        assertTrue(device.wait(Until.gone(By.text(Page.TWO.label)), 5000))
        SystemClock.sleep(1200) // Allow the system-bar transition to finish before pixel measurement.
        // Android's first-use fullscreen teaching overlay dims the page until acknowledged.
        device.wait(Until.findObject(By.text("Got it")), 1500)?.click()
        device.wait(Until.gone(By.text("Viewing full screen")), 5000)
        SystemClock.sleep(1500)
        val bitmap = requireNotNull(instrumentation.uiAutomation.takeScreenshot())
        try {
            java.io.File(context.getExternalFilesDir(null), "hidden-chrome.png").outputStream().use {
                bitmap.compress(Bitmap.CompressFormat.PNG, 100, it)
            }
            val xs = (0 until bitmap.width).filter { x ->
                val color = bitmap.getPixel(x, bitmap.height / 2)
                Color.green(color) > 130 && Color.red(color) < 100 && Color.blue(color) < 120
            }
            val ys = (0 until bitmap.height).filter { y ->
                val color = bitmap.getPixel(bitmap.width / 2, y)
                Color.green(color) > 130 && Color.red(color) < 100 && Color.blue(color) < 120
            }
            assertTrue("The actual page must be visible", xs.isNotEmpty() && ys.isNotEmpty())
            val width = xs.last() - xs.first() + 1
            val height = ys.last() - ys.first() + 1
            val inset = 2 * context.resources.displayMetrics.density
            val expectedHeight = minOf((bitmap.width - 2 * inset) / (48f / 72f), bitmap.height - 2 * inset)
            assertEquals("Use the full display, not the previous toolbar viewport", expectedHeight.toDouble(), height.toDouble(), 3.0)
            assertEquals("Never stretch or crop the source page", 48.0 / 72.0, width.toDouble() / height, .005)
            assertTrue("At least one pair of edges has only the configured narrow inset",
                minOf(xs.first(), ys.first()) <= kotlin.math.ceil(inset.toDouble()).toInt() + 2)
        } finally { bitmap.recycle() }
        clickAt(.5f, .5f)
        assertTrue("Controls remain recoverable", device.wait(Until.findObject(By.text(Page.TWO.label)), 5000) != null)
    }

    private fun openReader(mode: PageTurnMode, direction: ReadingDirection, autoHide: Boolean = false): Unit = runBlocking {
        scenario?.close()
        scenario = null
        repository.saveState(record.id, ReadingState(preferences = ReaderPreferences(
            direction = direction, layout = ReadingLayout.SINGLE, pageTurn = mode,
            smartSpreads = false, automaticPairs = false, automaticOrientation = false,
            autoHideControls = autoHide
        )))
        val intent = Intent(context, MainActivity::class.java).apply {
            action = Intent.ACTION_MAIN
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
        }
        scenario = ActivityScenario.launch(intent)
        // The library screen is reached through the 浏览 tab; the reader is opened from the record.
        visible(By.text("浏览")).click()
        visible(By.clazz("android.widget.EditText")).text = record.title
        device.settleLibrarySearch()
        visible(By.desc("打开${record.title}")).click()
        visible(By.text(Page.ONE.label))
    }

    private fun clickAt(widthFraction: Float, heightFraction: Float) {
        device.click(
            (device.displayWidth * widthFraction).toInt(),
            (device.displayHeight * heightFraction).toInt()
        )
        SystemClock.sleep(900)
    }

    private fun swipeHorizontally(fromFraction: Float, toFraction: Float) {
        val y = (device.displayHeight * 0.5f).toInt()
        device.swipe(
            (device.displayWidth * fromFraction).toInt(), y,
            (device.displayWidth * toFraction).toInt(), y,
            20
        )
        SystemClock.sleep(900)
    }

    /** The visible page is identified by the rendered centre pixel, not by any label. */
    private fun visiblePage(): Page {
        val bitmap: Bitmap = instrumentation.uiAutomation.takeScreenshot()
            ?: throw AssertionError("Could not capture the reading canvas")
        return try {
            val pixel = bitmap.getPixel(bitmap.width / 2, (bitmap.height * 0.42f).toInt())
            when {
                Color.red(pixel) > Color.green(pixel) && Color.red(pixel) > Color.blue(pixel) -> Page.ONE
                Color.green(pixel) > Color.red(pixel) && Color.green(pixel) > Color.blue(pixel) -> Page.TWO
                Color.blue(pixel) > Color.red(pixel) && Color.blue(pixel) > Color.green(pixel) -> Page.THREE
                else -> Page.NONE
            }
        } finally { bitmap.recycle() }
    }

    private fun visible(selector: BySelector, timeoutMs: Long = 20_000): UiObject2 =
        device.wait(Until.findObject(selector), timeoutMs)
            ?: run {
                val folder = java.io.File(context.getExternalFilesDir(null), "qa/page-turn-failure").apply { mkdirs() }
                device.takeScreenshot(java.io.File(folder, "${System.currentTimeMillis()}.png"))
                throw AssertionError("Control not visible: $selector pkg=${device.currentPackageName}")
            }
}
