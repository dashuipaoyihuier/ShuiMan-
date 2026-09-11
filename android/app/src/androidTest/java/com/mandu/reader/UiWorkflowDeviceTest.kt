package com.mandu.reader

import android.content.Intent
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
import com.mandu.reader.core.ReaderPreferences
import com.mandu.reader.core.ReadingDirection
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

/** Exercises real Compose controls and Activity recreation on both phone and tablet. */
@RunWith(AndroidJUnit4::class)
class UiWorkflowDeviceTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)
    private lateinit var fixtures: FixtureFactory.Set
    private lateinit var repository: BookRepository
    private lateinit var record: BookRecord
    private val extraRecords = mutableListOf<BookRecord>()
    private var scenario: ActivityScenario<MainActivity>? = null

    @Before fun prepare(): Unit = runBlocking {
        device.wakeUp()
        fixtures = FixtureFactory.create(context)
        repository = BookRepository(context)
        record = repository.importFile(Uri.fromFile(fixtures.cbz)).let {
            it.copy(title = "UI-${it.id.take(8)}").also { unique -> repository.save(unique) }
        }
        repository.saveState(record.id, ReadingState(preferences = ReaderPreferences(
            direction = ReadingDirection.LTR, layout = ReadingLayout.SINGLE, smartSpreads = false,
            automaticPairs = false, automaticOrientation = false
        )))
        val intent = Intent(context, MainActivity::class.java).apply {
            action = Intent.ACTION_MAIN
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
        }
        scenario = ActivityScenario.launch(intent)
        visible(By.text("浏览")).click()
        visible(By.clazz("android.widget.EditText")).text = record.title
        device.settleLibrarySearch()
        visible(By.desc("打开${record.title}")).click()
        visible(By.text("第 1 / 3 页"))
    }

    @After fun finish(): Unit = runBlocking {
        scenario?.close()
        device.unfreezeRotation()
        if (::record.isInitialized) repository.remove(record.id)
        extraRecords.forEach { repository.remove(it.id) }
        if (::fixtures.isInitialized) fixtures.root.deleteRecursively()
    }

    @Test fun navigationBookmarkRecreationAndSystemBackRetainSource() = runBlocking {
        visible(By.desc("下一页")).click()
        visible(By.text("第 2 / 3 页"))
        visible(By.desc("添加书签")).click()
        visible(By.desc("移除书签"))
        // Deliberately recreate immediately: a debounce must not lose the new bookmark/page.
        scenario!!.recreate()
        visible(By.text("第 2 / 3 页"))
        visible(By.desc("移除书签"))
        device.setOrientationLeft()
        visible(By.text("第 2 / 3 页"))
        capture("reader-rotated")
        device.setOrientationNatural()
        visible(By.text("第 2 / 3 页"))
        capture("reader-natural")
        device.pressBack()
        visible(By.text("我的漫画"))
        visible(By.desc("打开${record.title}")).click()
        visible(By.text("第 2 / 3 页"))
        visible(By.desc("移除书签"))
        val saved = repository.loadState(record.id)
        assertEquals("2.png", saved.locator?.resource)
        assertEquals(listOf(saved.locator), saved.bookmarks)
        assertTrue("Original source must be preserved", fixtures.cbz.isFile)
    }

    @Test fun manualPairKeepsPhysicalOrderAcrossReadingDirectionAndRecreation() = runBlocking {
        visible(By.desc("下一页")).click()
        visible(By.text("第 2 / 3 页"))
        visible(By.text("页面修正")).click()
        visible(By.text("与下一页组成跨页")).click()
        visible(By.text("第 2–3 / 3 页"))
        visible(By.desc("阅读设置")).click()
        visible(By.text("右→左")).click()
        device.pressBack()
        visible(By.text("第 2–3 / 3 页"))
        scenario!!.recreate()
        visible(By.text("第 2–3 / 3 页"))
        capture("manual-spread-rtl")
        assertGreenLeftOfBlue()
        val saved = repository.loadState(record.id)
        val override = saved.overrides.values.single { it.joinNext == true }
        assertEquals(false, override.earlierOnRight)
        assertTrue(fixtures.cbz.isFile)
    }

    @Test fun automaticLayoutTracksWindowWidthAndRetainsCurrentSource() = runBlocking {
        visible(By.desc("阅读设置")).click()
        visible(By.text("自动")).click()
        device.pressBack()
        visible(By.desc("下一页")).click()
        fun checkLayout() {
            val wide = device.displayWidth / context.resources.displayMetrics.density >= 700f
            visible(By.text(if (wide) "第 2–3 / 3 页" else "第 2 / 3 页"))
        }
        checkLayout()
        capture("automatic-natural")
        device.setOrientationLeft()
        checkLayout()
        capture("automatic-rotated")
        device.setOrientationNatural()
        checkLayout()
        assertEquals("2.png", repository.loadState(record.id).locator?.resource)
    }

    @Test fun complexEpubPreservesTextAndMissingPagePositionsWithoutNetworkPermission() = runBlocking {
        val epub = repository.importFile(Uri.fromFile(fixtures.epub)).let {
            it.copy(title = "EPUB-${it.id.take(8)}").also { unique -> repository.save(unique) }
        }.also(extraRecords::add)
        repository.saveState(epub.id, ReadingState(preferences = ReaderPreferences(
            layout = ReadingLayout.SINGLE, smartSpreads = false,
            automaticPairs = false, automaticOrientation = false
        )))
        device.pressBack()
        visible(By.text("浏览")).click()
        visible(By.clazz("android.widget.EditText")).text = epub.title
        device.settleLibrarySearch()
        visible(By.desc("打开${epub.title}")).click()
        visible(By.text("第 1 / 5 页"))
        repeat(3) { index ->
            visible(By.desc("下一页")).click()
            visible(By.text("第 ${index + 2} / 5 页"))
        }
        visible(By.text("这一页无法显示"))
        visible(By.desc("下一页")).click()
        visible(By.text("第 5 / 5 页"))
        visible(By.text("Required caption"))
        capture("complex-epub-local")
        @Suppress("DEPRECATION")
        val permissions = context.packageManager.getPackageInfo(context.packageName,
            android.content.pm.PackageManager.GET_PERMISSIONS).requestedPermissions.orEmpty()
        assertFalse("Offline reader must not inherit network permission", "android.permission.INTERNET" in permissions)
        assertTrue(fixtures.epub.isFile)
    }

    private fun visible(selector: BySelector): UiObject2 =
        device.wait(Until.findObject(selector), 20_000)
            ?: run {
                capture("failure")
                val directory = File(context.getExternalFilesDir(null), "qa").apply { mkdirs() }
                device.dumpWindowHierarchy(File(directory, "failure-window.xml"))
                throw AssertionError("Control not visible: $selector")
            }

    private fun capture(name: String) {
        val directory = File(context.getExternalFilesDir(null), "qa").apply { mkdirs() }
        assertTrue(device.takeScreenshot(File(directory, "$name-${device.displayWidth}x${device.displayHeight}.png")))
    }

    /** Assert actual rendered physical placement, not merely the saved pairing flag. */
    private fun assertGreenLeftOfBlue() {
        val deadline = SystemClock.uptimeMillis() + 10_000
        do {
            val bitmap = instrumentation.uiAutomation.takeScreenshot() ?: continue
            try {
                val green = mutableListOf<Int>()
                val blue = mutableListOf<Int>()
                for (x in 0 until bitmap.width) {
                    val pixel = bitmap.getPixel(x, bitmap.height / 2)
                    val r = Color.red(pixel); val g = Color.green(pixel); val b = Color.blue(pixel)
                    if (g > r * 1.3 && g > b * 1.3) green += x
                    if (b > r * 1.3 && b > g * 1.3) blue += x
                }
                if (green.size > 40 && blue.size > 40) {
                    assertTrue("Physical panels were swapped after changing reading direction", green.average() < blue.average())
                    val gap = blue.min() - green.max() - 1
                    assertTrue("A confirmed spread must share a seam, found a $gap px gutter", gap <= 2)
                    return
                }
            } finally { bitmap.recycle() }
            SystemClock.sleep(100)
        } while (SystemClock.uptimeMillis() < deadline)
        fail("Both source panels must be visibly rendered")
    }
}
