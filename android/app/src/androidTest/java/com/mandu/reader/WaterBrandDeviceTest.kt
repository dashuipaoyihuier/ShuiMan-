package com.mandu.reader

import android.content.res.Configuration
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.drawable.AdaptiveIconDrawable
import android.net.Uri
import androidx.compose.ui.graphics.toArgb
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import com.mandu.reader.data.BookRepository
import com.mandu.reader.ui.WaterDarkColors
import com.mandu.reader.ui.WaterLightColors
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class WaterBrandDeviceTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)

    @Test fun nameAndAdaptiveIconPreserveMacIdentity() {
        assertEquals("com.mandu.reader", context.packageName)
        assertEquals("水漫", context.packageManager.getApplicationLabel(context.applicationInfo).toString())
        val icon = context.packageManager.getApplicationIcon(context.applicationInfo)
        assertTrue(icon is AdaptiveIconDrawable)
        val bitmap = Bitmap.createBitmap(432, 432, Bitmap.Config.ARGB_8888)
        icon.setBounds(0, 0, 432, 432)
        icon.draw(Canvas(bitmap))
        val pixels = IntArray(432 * 432)
        bitmap.getPixels(pixels, 0, 432, 0, 0, 432, 432)
        assertTrue("Book paper survives launcher mask", pixels.count { it == 0xFFF5FBF9.toInt() } > 10_000)
        assertTrue("Wave survives launcher mask", pixels.count { it == 0xFF94E3E0.toInt() } > 300)
        File(folder(), "launcher-icon.png").outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
        bitmap.recycle()
    }

    @Test fun fixedPaletteMatchesMacInBothAppearances() {
        assertEquals(0xFF0F6E82.toInt(), WaterLightColors.primary.toArgb())
        assertEquals(0xFFF0F7F5.toInt(), WaterLightColors.background.toArgb())
        assertEquals(0xFF61CCD4.toInt(), WaterDarkColors.primary.toArgb())
        assertEquals(0xFF121F26.toInt(), WaterDarkColors.background.toArgb())
        val dark = context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK == Configuration.UI_MODE_NIGHT_YES
        val scheme = if (dark) WaterDarkColors else WaterLightColors
        assertEquals(scheme.primary.toArgb(), context.getColor(R.color.water_accent))
        assertEquals(scheme.background.toArgb(), context.getColor(R.color.water_surface))
    }

    @Test fun brandedShelfKeepsSeriesAccessibleAndUsesExpectedBackground() = runBlocking {
        device.wakeUp()
        val repository = BookRepository(context)
        val fixture = FixtureFactory.create(context)
        val series = "水漫验收-${UUID.randomUUID().toString().take(8)}"
        val book = repository.importFile(Uri.fromFile(fixture.cbz)).copy(series = series, volume = 1.0)
        repository.save(book)
        try {
            ActivityScenario.launch(MainActivity::class.java).use {
                device.wait(Until.findObject(By.text("浏览")), 15_000)!!.click()
                device.wait(Until.findObject(By.clazz("android.widget.EditText")), 15_000)!!.text = ""
                assertNotNull(device.wait(Until.findObject(By.text("翻开一页，漫入故事。")), 15_000))
                assertTrue(device.hasObject(By.text("水漫")))
                device.waitForIdle(1_000)
                android.os.SystemClock.sleep(350)
                val dark = context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK == Configuration.UI_MODE_NIGHT_YES
                val screenshot = File(folder(), "shelf-${if (dark) "dark" else "light"}-${device.displayWidth}x${device.displayHeight}.png")
                assertTrue(device.takeScreenshot(screenshot))
                val bitmap = BitmapFactory.decodeFile(screenshot.absolutePath)
                val pixels = IntArray(bitmap.width * bitmap.height)
                bitmap.getPixels(pixels, 0, bitmap.width, 0, 0, bitmap.width, bitmap.height)
                val expected = if (dark) 0xFF121F26.toInt() else 0xFFF0F7F5.toInt()
                assertTrue("Visible shelf uses Water background, not wallpaper colors", pixels.count { it == expected } > pixels.size / 100)
                bitmap.recycle()
                device.findObject(By.clazz("android.widget.EditText")).text = series
                device.settleLibrarySearch()
                assertNotNull(device.wait(Until.findObject(By.desc("查看系列$series")), 15_000))
                assertFalse("Banner must not take search-result space", device.hasObject(By.text("翻开一页，漫入故事。")))
                device.findObject(By.desc("查看系列$series")).click()
                assertNotNull(device.wait(Until.findObject(By.text("1 卷 · 按卷号排序")), 15_000))
            }
        } finally {
            repository.remove(book.id)
            fixture.root.deleteRecursively()
        }
    }

    private fun folder() = File(context.getExternalFilesDir(null), "qa/water-brand").apply { mkdirs() }
}
