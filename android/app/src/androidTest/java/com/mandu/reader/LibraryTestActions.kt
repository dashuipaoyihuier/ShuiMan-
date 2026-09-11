package com.mandu.reader

import android.os.SystemClock
import android.os.Build
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import androidx.test.platform.app.InstrumentationRegistry
import java.io.File

/** Search removes the hero and moves cards. Do not click cached pre-layout bounds. */
internal fun UiDevice.settleLibrarySearch() {
    waitForIdle(1_000)
    SystemClock.sleep(350)
    // API 36 can retain removed Compose virtual nodes in the automation cache:
    // screenshots show the filtered grid while the old hero text still queries.
    // Refresh that cache before asserting removal or obtaining click bounds.
    if (Build.VERSION.SDK_INT >= 34) {
        InstrumentationRegistry.getInstrumentation().uiAutomation.clearCache()
    }
    if (!wait(Until.gone(By.text("翻开一页，漫入故事。")), 5_000)) {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val folder = File(context.getExternalFilesDir(null), "qa/search-failure").apply { mkdirs() }
        takeScreenshot(File(folder, "screen.png"))
        dumpWindowHierarchy(File(folder, "hierarchy.xml"))
        throw AssertionError("Search did not settle; text=${findObject(By.clazz("android.widget.EditText"))?.text}")
    }
}
