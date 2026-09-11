package com.mandu.reader

import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.net.Uri
import android.os.SystemClock
import android.view.InputDevice
import android.view.MotionEvent
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
import com.mandu.reader.core.ScaleMode
import com.mandu.reader.data.BookRepository
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.util.UUID
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

/** Pixel-level regressions for WIDTH/ORIGINAL geometry and continuous position handoff. */
@RunWith(AndroidJUnit4::class)
class ReaderViewportDeviceTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)
    private lateinit var repository: BookRepository
    private lateinit var record: BookRecord
    private lateinit var root: File
    private var scenario: ActivityScenario<MainActivity>? = null

    @Before fun prepare(): Unit = runBlocking {
        device.wakeUp()
        repository = BookRepository(context)
        root = File(context.cacheDir, "viewport-fixture-${UUID.randomUUID()}").apply {
            check(mkdirs())
        }
        val archive = File(root, "viewport.cbz")
        writeArchive(archive)
        record = repository.importFile(Uri.fromFile(archive)).let {
            it.copy(title = "VIEWPORT-${it.id.take(8)}").also { saved -> repository.save(saved) }
        }
    }

    @After fun finish(): Unit = runBlocking {
        scenario?.close()
        if (::record.isInitialized) repository.remove(record.id)
        if (::root.isInitialized) root.deleteRecursively()
    }

    @Test fun continuousWidthReachesLongPageBottomAndRestoresInsidePage() = runBlocking {
        openReader(PageTurnMode.SCROLL, ScaleMode.WIDTH)
        assertTrue("The middle marker starts outside the viewport", !screenHas(::isGreen))

        swipeUpUntil("middle marker") { screenHas(::isGreen) }
        SystemClock.sleep(700) // settle the writer in addition to the in-memory recreation handoff
        val before = repository.loadState(record.id)
        assertEquals("Still anchored to the first source page", "1-long.png", before.locator?.resource)
        assertTrue("A normalized in-page offset must be durable", before.scrollFraction > 0.05)

        scenario!!.recreate()
        visible(By.text("第 1 / 2 页"))
        assertTrue("Activity recreation must restore the same middle region", waitForPixels(::isGreen))

        swipeUpUntil("bottom marker") { screenHas(::isBlue) }
        assertTrue("The strip must not enter page two before the long-page end is reachable",
            device.hasObject(By.text("第 1 / 2 页")))
    }

    @Test fun widthOverflowPansWithOneFingerWithoutBecomingASwipeTurn() {
        openReader(PageTurnMode.SWIPE, ScaleMode.WIDTH)
        assertTrue(waitForPixels(::isGreen)) // WIDTH initially centres its virtual long canvas.
        swipeUpUntil("lower source region", 30) { screenHas(::isBlue) }
        assertTrue("Vertical panning must not turn the page in SWIPE mode",
            device.hasObject(By.text("第 1 / 2 页")))
    }

    @Test fun originalUsesSmallSourcePixelFootprintWithoutUpscaling() = runBlocking {
        val locator = repository.open(record).use { it.publication.units[1].locator }
        repository.saveState(record.id, readingState(PageTurnMode.TAP, ScaleMode.ORIGINAL).copy(locator = locator))
        launchAndOpen()
        visible(By.text("第 2 / 2 页"))
        assertTrue("Wait for the original page decode before measuring it", waitForPixels(::isMagenta))
        val screenshot = requireNotNull(instrumentation.uiAutomation.takeScreenshot())
        try {
            val points = buildList {
                for (y in 0 until screenshot.height) for (x in 0 until screenshot.width) {
                    if (isMagenta(screenshot.getPixel(x, y))) add(x to y)
                }
            }
            assertTrue("The original-size source must be visible", points.size > 20_000)
            val renderedWidth = points.maxOf { it.first } - points.minOf { it.first } + 1
            val renderedHeight = points.maxOf { it.second } - points.minOf { it.second } + 1
            assertEquals("ORIGINAL must preserve the 240 source-pixel width", 240.0, renderedWidth.toDouble(), 2.0)
            assertEquals("ORIGINAL must preserve the 360 source-pixel height", 360.0, renderedHeight.toDouble(), 2.0)
        } finally {
            screenshot.recycle()
        }
    }

    @Test fun changingScaleModeClearsGestureZoomAndPan() {
        openReader(PageTurnMode.TAP, ScaleMode.FIT)
        assertTrue("Wait for the fitted canvas before injecting multi-touch", waitForPixels(::isGreen))
        pinchOpen()
        assertTrue("Pinch must expose a zoom percentage",
            device.wait(Until.findObject(By.textContains("% · 点击还原")), 5_000) != null)
        visible(By.desc("阅读设置")).click()
        scrollToVisible(By.text("适合宽度")).click()
        dismissSheetToReader("第 1 / 2 页")
        SystemClock.sleep(400)
        val storedMode = runBlocking { repository.loadState(record.id).preferences.scaleMode }
        assertEquals("The settings chip must actually receive the selection", ScaleMode.WIDTH, storedMode)
        assertFalse("Selecting another scale mode must reset gesture zoom",
            device.hasObject(By.textContains("% · 点击还原")))
    }

    @Test fun temporarilyRevealedManualImmersiveChromeAutoHidesAgain() {
        openReader(PageTurnMode.TAP, ScaleMode.FIT, autoHide = true)
        visible(By.desc("沉浸阅读")).click()
        assertTrue(device.wait(Until.gone(By.text("第 1 / 2 页")), 5_000))
        device.click(device.displayWidth / 2, device.displayHeight / 2)
        visible(By.text("第 1 / 2 页"))
        assertTrue("Temporarily revealed immersive chrome must obey the auto-hide timer",
            device.wait(Until.gone(By.text("第 1 / 2 页")), 8_000))
    }

    private fun openReader(mode: PageTurnMode, scale: ScaleMode, autoHide: Boolean = false) = runBlocking {
        repository.saveState(record.id, readingState(mode, scale, autoHide))
        launchAndOpen()
    }

    private fun readingState(mode: PageTurnMode, scale: ScaleMode, autoHide: Boolean = false) =
        ReadingState(preferences = ReaderPreferences(
            direction = ReadingDirection.LTR,
            layout = ReadingLayout.SINGLE,
            pageTurn = mode,
            scaleMode = scale,
            smartSpreads = false,
            automaticPairs = false,
            automaticOrientation = false,
            autoHideControls = autoHide
        ))

    private fun launchAndOpen() {
        scenario?.close()
        val intent = Intent(context, MainActivity::class.java).apply {
            action = Intent.ACTION_MAIN
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
        }
        scenario = ActivityScenario.launch(intent)
        visible(By.text("浏览")).click()
        visible(By.clazz("android.widget.EditText")).text = record.title
        device.settleLibrarySearch()
        visible(By.desc("打开${record.title}")).click()
        visible(By.desc("返回书库"))
    }

    private fun swipeUpUntil(label: String, attempts: Int = 16, condition: () -> Boolean) {
        repeat(attempts) {
            if (condition()) return
            device.swipe(device.displayWidth / 2, (device.displayHeight * .76f).toInt(),
                device.displayWidth / 2, (device.displayHeight * .18f).toInt(), 24)
            SystemClock.sleep(300)
        }
        throw AssertionError("Could not reach $label")
    }

    private fun waitForPixels(predicate: (Int) -> Boolean): Boolean {
        val deadline = SystemClock.uptimeMillis() + 7_000
        do {
            if (screenHas(predicate)) return true
            SystemClock.sleep(120)
        } while (SystemClock.uptimeMillis() < deadline)
        return false
    }

    private fun screenHas(predicate: (Int) -> Boolean): Boolean {
        val screenshot = instrumentation.uiAutomation.takeScreenshot() ?: return false
        return try {
            val x = screenshot.width / 2
            var count = 0
            for (y in screenshot.height / 10 until screenshot.height * 9 / 10 step 3) {
                if (predicate(screenshot.getPixel(x, y))) count++
            }
            count >= 12
        } finally {
            screenshot.recycle()
        }
    }

    private fun pinchOpen() {
        val centreX = device.displayWidth / 2f
        val centreY = device.displayHeight / 2f
        val downTime = SystemClock.uptimeMillis()
        sendTouch(downTime, SystemClock.uptimeMillis(), MotionEvent.ACTION_DOWN, listOf(centreX - 40f to centreY))
        SystemClock.sleep(20)
        sendTouch(downTime, SystemClock.uptimeMillis(), MotionEvent.ACTION_POINTER_DOWN or
            (1 shl MotionEvent.ACTION_POINTER_INDEX_SHIFT), listOf(centreX - 40f to centreY, centreX + 40f to centreY))
        repeat(12) { step ->
            SystemClock.sleep(16)
            val distance = 40f + (step + 1) * 22f
            sendTouch(downTime, SystemClock.uptimeMillis(), MotionEvent.ACTION_MOVE,
                listOf(centreX - distance to centreY, centreX + distance to centreY))
        }
        SystemClock.sleep(16)
        sendTouch(downTime, SystemClock.uptimeMillis(), MotionEvent.ACTION_POINTER_UP or
            (1 shl MotionEvent.ACTION_POINTER_INDEX_SHIFT), listOf(centreX - 304f to centreY, centreX + 304f to centreY))
        SystemClock.sleep(16)
        sendTouch(downTime, SystemClock.uptimeMillis(), MotionEvent.ACTION_UP, listOf(centreX - 304f to centreY))
        SystemClock.sleep(500)
    }

    private fun sendTouch(downTime: Long, eventTime: Long, action: Int, points: List<Pair<Float, Float>>) {
        val properties = Array(points.size) { index -> MotionEvent.PointerProperties().apply {
            id = index
            toolType = MotionEvent.TOOL_TYPE_FINGER
        } }
        val coordinates = Array(points.size) { index -> MotionEvent.PointerCoords().apply {
            x = points[index].first
            y = points[index].second
            pressure = 1f
            size = 1f
        } }
        val event = MotionEvent.obtain(
            downTime, eventTime, action, points.size, properties, coordinates,
            0, 0, 1f, 1f, 0, 0, InputDevice.SOURCE_TOUCHSCREEN, 0
        )
        try { instrumentation.sendPointerSync(event) } finally { event.recycle() }
    }

    private fun visible(selector: BySelector, timeout: Long = 20_000): UiObject2 =
        device.wait(Until.findObject(selector), timeout)
            ?: run {
                val directory = File(context.getExternalFilesDir(null), "qa/reader-viewport").apply { mkdirs() }
                val screenshot = File(directory, "failure-${device.displayWidth}x${device.displayHeight}.png")
                val hierarchy = File(directory, "failure-${device.displayWidth}x${device.displayHeight}.xml")
                screenshot.delete()
                hierarchy.delete()
                device.takeScreenshot(screenshot)
                device.dumpWindowHierarchy(hierarchy)
                throw AssertionError("Control not visible: $selector")
            }

    private fun scrollToVisible(selector: BySelector): UiObject2 {
        repeat(7) {
            device.waitForIdle(1_000)
            SystemClock.sleep(350)
            device.findObject(selector)?.let {
                val bounds = it.visibleBounds
                if (bounds.height() >= 16 * context.resources.displayMetrics.density &&
                    bounds.bottom < device.displayHeight - 80) return it
            }
            device.swipe(
                device.displayWidth / 2, (device.displayHeight * .82f).toInt(),
                device.displayWidth / 2, (device.displayHeight * .50f).toInt(), 50
            )
        }
        return visible(selector, 2_000)
    }

    private fun dismissSheetToReader(pageLabel: String) {
        repeat(2) {
            if (device.wait(Until.findObject(By.text(pageLabel)), 700) != null) return
            device.pressBack()
        }
        visible(By.text(pageLabel), 3_000)
    }

    private fun writeArchive(file: File) {
        val longPage = Bitmap.createBitmap(360, 3600, Bitmap.Config.ARGB_8888)
        Canvas(longPage).apply {
            drawColor(Color.rgb(28, 30, 34))
            drawRect(0f, 0f, 360f, 620f, Paint().apply { color = Color.RED })
            drawRect(0f, 1490f, 360f, 2110f, Paint().apply { color = Color.GREEN })
            drawRect(0f, 2980f, 360f, 3600f, Paint().apply { color = Color.BLUE })
            val ruler = Paint().apply { color = Color.WHITE; strokeWidth = 2f }
            for (y in 0..3600 step 100) drawLine(0f, y.toFloat(), 28f, y.toFloat(), ruler)
        }
        val smallPage = Bitmap.createBitmap(240, 360, Bitmap.Config.ARGB_8888).apply {
            eraseColor(Color.MAGENTA)
        }
        try {
            ZipOutputStream(FileOutputStream(file)).use { output ->
                listOf("1-long.png" to longPage, "2-small.png" to smallPage).forEach { (name, bitmap) ->
                    output.putNextEntry(ZipEntry(name))
                    ByteArrayOutputStream().use { bytes ->
                        check(bitmap.compress(Bitmap.CompressFormat.PNG, 100, bytes))
                        output.write(bytes.toByteArray())
                    }
                    output.closeEntry()
                }
            }
        } finally {
            longPage.recycle()
            smallPage.recycle()
        }
    }

    private fun isGreen(color: Int) = Color.green(color) > 180 && Color.red(color) < 70 && Color.blue(color) < 70
    private fun isBlue(color: Int) = Color.blue(color) > 180 && Color.red(color) < 70 && Color.green(color) < 70
    private fun isMagenta(color: Int) = Color.red(color) > 180 && Color.blue(color) > 180 && Color.green(color) < 70
}
