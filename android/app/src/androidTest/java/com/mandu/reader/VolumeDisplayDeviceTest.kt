package com.mandu.reader

import android.content.Intent
import android.content.pm.ActivityInfo
import android.net.Uri
import android.os.Build
import android.os.SystemClock
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.BySelector
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import com.mandu.reader.core.*
import com.mandu.reader.data.BookRepository
import com.mandu.reader.data.VolumeDisplayStore
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class VolumeDisplayDeviceTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)
    private val repository by lazy { BookRepository(context) }
    private val store by lazy { VolumeDisplayStore(context) }
    private lateinit var original: VolumeDisplayPreferences
    private lateinit var fixture: FixtureFactory.Set
    private val records = mutableListOf<BookRecord>()
    private var scenario: ActivityScenario<MainActivity>? = null
    private val series = "分卷显示-${UUID.randomUUID().toString().take(8)}"

    @Before fun prepare() = runBlocking {
        device.wakeUp()
        device.setOrientationNatural()
        original = store.load()
        store.save(VolumeDisplayPreferences())
        fixture = FixtureFactory.create(context)
        for (number in 1..40) {
            val file = File(fixture.root, "分卷$number.cbz")
            fixture.cbz.copyTo(file)
            val book = repository.importFile(Uri.fromFile(file)).copy(title = "展示卷$number", series = series, volume = number.toDouble())
            repository.save(book)
            records += book
        }
        launchSeries()
    }

    @After fun clean() = runBlocking {
        scenario?.close()
        device.unfreezeRotation()
        if (::original.isInitialized) store.save(original)
        records.forEach { repository.remove(it.id) }
        if (::fixture.isInitialized) fixture.root.deleteRecursively()
    }

    @Test fun coverSizesListAndRelaunchKeepPreferenceAndReadingProgress() = runBlocking {
        visible(By.desc("分卷网格"))
        visible(By.desc("封面大小：小"))
        val small = visible(By.desc("打开展示卷1")).visibleBounds.width()
        capture("grid-small")
        chooseSize("中")
        val medium = visible(By.desc("打开展示卷1")).visibleBounds.width()
        chooseSize("大")
        val large = visible(By.desc("打开展示卷1")).visibleBounds.width()
        assertTrue("Cover sizes must change actual pixel width", small < medium && medium < large)
        capture("grid-large")
        chooseSize("小")
        visible(By.desc("列表视图")).click()
        settle()
        visible(By.desc("分卷列表"))
        val first = visible(By.desc("打开展示卷1")).visibleBounds
        val second = visible(By.desc("打开展示卷2")).visibleBounds
        assertTrue("One volume per row", first.bottom <= second.top)
        capture("list-small")
        chooseSize("大")
        assertTrue("List thumbnails also resize rows", visible(By.desc("打开展示卷1")).visibleBounds.height() > first.height())
        chooseSize("小")
        prepareReading(records[1])
        visible(By.desc("打开展示卷2")).click()
        visible(By.text("第 1 / 3 页"))
        visible(By.desc("下一页")).click()
        visible(By.text("第 2 / 3 页"))
        device.pressBack()
        visible(By.desc("分卷列表"))
        assertEquals(VolumeDisplayPreferences(VolumeViewMode.LIST, VolumeCoverSize.SMALL), VolumeDisplayStore(context).load())
        scenario!!.recreate()
        visible(By.desc("分卷列表"))
        visible(By.desc("封面大小：小"))
        scenario!!.close()
        launchSeries()
        visible(By.desc("分卷列表"))
        assertEquals("2.png", repository.loadState(records[1].id).locator?.resource)
        visible(By.desc("管理展示卷1")).click()
        visible(By.text("收藏")).click()
        settle()
        assertTrue(repository.books(false).single { it.id == records[0].id }.favorite)
        visible(By.desc("管理展示卷1")).click()
        visible(By.text("整理书籍")).click()
        visible(By.text("系列"))
        device.pressBack()
        assertEquals(40, repository.books(false).count { it.series == series })
        assertTrue(fixture.cbz.isFile)
    }

    @Test fun browsingAnchorSurvivesModeChangeRotationAndReaderRoundTrip() {
        visible(By.desc("列表视图")).click()
        settle()
        val area = visible(By.desc("分卷列表")).visibleBounds
        repeat(3) { device.swipe(area.centerX(), area.bottom - 40, area.centerX(), area.top + 40, 24); settle() }
        val before = firstVisibleTitle()
        assertNotEquals("打开展示卷1", before)
        visible(By.desc("网格视图")).click()
        settle()
        assertNotNull(device.findObject(By.desc(before)))
        visible(By.desc("列表视图")).click()
        settle()
        val anchor = firstVisibleTitle()
        prepareReading(records.single { "打开${it.title}" == anchor })
        device.findObject(By.desc(anchor)).click()
        visible(By.desc("返回书库"))
        device.pressBack()
        visible(By.desc("分卷列表"))
        settle()
        assertNotNull("Return near the same volume", device.findObject(By.desc(anchor)))
        scenario!!.onActivity { it.requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED }
        device.setOrientationLeft()
        settle()
        visible(By.desc("分卷列表"))
        assertNotNull("Rotation keeps the visible volume", device.findObject(By.desc(anchor)))
        scenario!!.recreate()
        visible(By.desc("分卷列表"))
        settle()
        assertNotNull(device.findObject(By.desc(anchor)))
        capture("list-anchor-rotated")
    }

    private fun launchSeries() {
        scenario = ActivityScenario.launch(Intent(context, MainActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
        })
        var phone = false
        scenario!!.onActivity {
            phone = it.resources.configuration.smallestScreenWidthDp < 600
            it.requestedOrientation = if (phone) ActivityInfo.SCREEN_ORIENTATION_PORTRAIT else ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE
        }
        val deadline = SystemClock.uptimeMillis() + 5_000
        while ((device.displayWidth < device.displayHeight) != phone && SystemClock.uptimeMillis() < deadline) SystemClock.sleep(100)
        assertEquals("Phone density evidence must be portrait", phone, device.displayWidth < device.displayHeight)
        visible(By.text("浏览")).click()
        visible(By.clazz("android.widget.EditText")).text = series
        device.settleLibrarySearch()
        visible(By.desc("查看系列$series")).click()
        visible(By.text("40 卷 · 按卷号排序"))
        settle()
    }

    private fun chooseSize(label: String) {
        visible(By.descStartsWith("封面大小：")).click()
        visible(By.text("${label}封面")).click()
        settle()
        visible(By.desc("封面大小：$label"))
    }

    private fun prepareReading(book: BookRecord) = runBlocking {
        repository.saveState(book.id, ReadingState(preferences = ReaderPreferences(
            layout = ReadingLayout.SINGLE, smartSpreads = false, automaticOrientation = false,
            automaticPairs = false, autoHideControls = false)))
    }

    private fun firstVisibleTitle(): String = device.findObjects(By.descStartsWith("打开展示卷"))
        .filter { it.visibleBounds.height() > 50 }
        .minBy { it.visibleBounds.top }.contentDescription

    private fun settle() {
        device.waitForIdle(1_000)
        SystemClock.sleep(350)
        if (Build.VERSION.SDK_INT >= 34) instrumentation.uiAutomation.clearCache()
    }

    private fun visible(selector: BySelector) = device.wait(Until.findObject(selector), 15_000) ?: run {
        capture("failure")
        throw AssertionError("Missing $selector")
    }

    private fun capture(name: String) {
        settle()
        val folder = File(context.getExternalFilesDir(null), "qa/volume-display").apply { mkdirs() }
        device.takeScreenshot(File(folder, "$name-${device.displayWidth}x${device.displayHeight}.png"))
    }
}
