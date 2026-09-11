package com.mandu.reader

import android.content.Intent
import android.graphics.Bitmap
import android.net.Uri
import android.os.ParcelFileDescriptor
import android.os.SystemClock
import android.provider.DocumentsContract
import androidx.lifecycle.ViewModelProvider
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.BySelector
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.UiObject2
import androidx.test.uiautomator.Until
import androidx.test.uiautomator.StaleObjectException
import com.mandu.reader.data.BookRepository
import com.mandu.reader.ui.ImportViewModel
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TestWatcher
import org.junit.runner.Description
import org.junit.runner.RunWith
import java.io.File
import java.util.UUID
import java.util.regex.Pattern

/** Real system picker and ActivityResult callbacks, with no pre-grant or direct import injection. */
@RunWith(AndroidJUnit4::class)
class SafImportDeviceTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)
    private val repository by lazy { BookRepository(context) }
    private val authority = "com.mandu.reader.test.documents"
    private val fixture = "slow-${UUID.randomUUID()}"
    private var scenario: ActivityScenario<MainActivity>? = null

    @get:Rule val evidence = object : TestWatcher() {
        override fun failed(error: Throwable, description: Description) {
            val folder = File(context.getExternalFilesDir(null), "qa").apply { mkdirs() }
            instrumentation.uiAutomation.takeScreenshot()?.let { bitmap ->
                File(folder, "saf-${description.methodName}.png").outputStream().use {
                    bitmap.compress(Bitmap.CompressFormat.PNG, 100, it)
                }
                bitmap.recycle()
            }
            device.dumpWindowHierarchy(File(folder, "saf-${description.methodName}.xml"))
        }
    }

    @Before fun createUnprivilegedFixture() {
        device.wakeUp()
        fixtureAction("make-picker-fixtures")
        assertFalse(context.contentResolver.persistedUriPermissions.any { it.uri.toString().contains(fixture) })
        val denied = try {
            context.contentResolver.query(DocumentsContract.buildDocumentUri(authority, "$fixture/卷1.cbz"),
                null, null, null, null)?.close()
            false
        } catch (_: SecurityException) { true }
        assertTrue("The reader must not have source access before selection", denied)
        scenario = ActivityScenario.launch(MainActivity::class.java)
        visible(By.text("导入")).click()
    }

    @After fun removeOnlyGeneratedData(): Unit = runBlocking {
        scenario?.onActivity { ViewModelProvider(it)[ImportViewModel::class.java].cancel() }
        scenario?.close()
        repository.books(false).filter { it.uri.contains(fixture) }.forEach { repository.remove(it.id) }
        context.contentResolver.persistedUriPermissions.filter { it.uri.toString().contains(fixture) }.forEach {
            context.contentResolver.releasePersistableUriPermission(it.uri, Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        fixtureAction("remove-fixtures")
    }

    @Test fun singleSelectionImportsAndRetainsAccessAfterRelaunch(): Unit = runBlocking {
        visible(By.text("选择文件")).click()
        chooseTestRoot()
        visible(By.text("卷1.cbz")).click()
        visible(By.text("第 1 / 3 页"), 45_000)
        val book = repository.books(false).single { it.uri.contains(fixture) }
        assertEquals("$fixture/卷1.cbz", DocumentsContract.getDocumentId(Uri.parse(book.uri)))
        assertReadGrant(Uri.parse(book.uri))
        BookRepository(context).open(book).use { assertEquals(3, it.publication.units.size) }
        scenario!!.close()
        scenario = ActivityScenario.launch(MainActivity::class.java)
        visible(By.text("浏览")).click()
        visible(By.clazz("android.widget.EditText")).text = book.title
        device.settleLibrarySearch()
        visible(By.desc("打开${book.title}")).click()
        visible(By.text("第 1 / 3 页"))
    }

    @Test fun multipleSelectionCompletesAndReenablesThePicker(): Unit = runBlocking {
        visible(By.text("选择文件")).click()
        chooseTestRoot()
        visible(By.text("卷1.cbz")).longClick()
        // DocumentsUI sorts these names lexically; use the adjacent visible card on phones.
        visible(By.text("卷10.cbz")).click()
        visible(By.text(Pattern.compile("(?i)select|open|选择|打开"))).click()
        waitForImport(2)
        assertTrue(visible(By.text("选择文件")).isEnabled)
        val books = repository.books(false).filter { it.uri.contains(fixture) }
        assertEquals(setOf("卷1", "卷10"), books.map { it.title }.toSet())
        books.forEach { assertReadGrant(Uri.parse(it.uri)); assertEquals(3, it.pageCount) }
    }

    @Test fun treeSelectionGrantsDescendantAccessAndImportsImageSubfolder(): Unit = runBlocking {
        visible(By.text("选择文件夹")).click()
        chooseTestRoot()
        visible(By.text(Pattern.compile("(?i)use this folder|使用此文件夹|使用这个文件夹"))).click()
        visible(By.text(Pattern.compile("(?i)allow|允许"))).click()
        waitForImport(14)
        assertTrue(visible(By.text("选择文件夹")).isEnabled)
        assertReadGrant(DocumentsContract.buildTreeDocumentUri(authority, fixture))
        val book = repository.books(false).single { it.uri.contains(fixture) && it.title == "图片册" }
        BookRepository(context).open(book).use { opened ->
            assertEquals(listOf("1.png", "2.png", "10.png"), opened.publication.units.map { it.title })
            assertEquals(48, opened.render(0, 72).width)
        }
    }

    private suspend fun waitForImport(count: Int) {
        val deadline = SystemClock.uptimeMillis() + 60_000
        while (SystemClock.uptimeMillis() < deadline) {
            var finished = false
            scenario!!.onActivity {
                val status = ViewModelProvider(it)[ImportViewModel::class.java].status
                finished = !status.running && status.message.startsWith("导入完成") && status.added == count
            }
            if (finished) return
            delay(100)
        }
        fail("Picker result did not finish importing $count books")
    }

    private fun assertReadGrant(uri: Uri) {
        assertTrue("Persisted read permission for $uri", context.contentResolver.persistedUriPermissions.any {
            it.uri == uri && it.isReadPermission
        })
    }

    private fun chooseTestRoot() {
        // Recent-page provider cards remain in the accessibility tree behind the drawer.
        // Their coordinates can hit an unrelated drawer row (for example Downloads).
        val root = By.text("Import regression fixtures")
            .hasAncestor(By.res("com.google.android.documentsui", "roots_list"))
        val drawer = By.desc(Pattern.compile("(?i)show roots|open navigation drawer|显示根目录|打开导航抽屉"))
        // On phones the breadcrumb can have the same title as the drawer entry.
        // Always open the drawer when its button exists, not merely when text is absent.
        if (device.wait(Until.findObject(drawer), 2_000) != null) {
            clickFresh(drawer)
            // Drawer entries are exposed to accessibility before their slide animation settles.
            device.waitForIdle()
            SystemClock.sleep(400)
        }
        val folder = File(context.getExternalFilesDir(null), "qa").apply { mkdirs() }
        device.dumpWindowHierarchy(File(folder, "saf-drawer-before-root.xml"))
        clickFresh(root)
        clickFresh(By.text(fixture))
        visible(By.text("卷1.cbz"))
    }

    private fun visible(selector: BySelector, timeout: Long = 20_000): UiObject2 =
        device.wait(Until.findObject(selector), timeout) ?: run {
            val folder = File(context.getExternalFilesDir(null), "qa").apply { mkdirs() }
            device.dumpWindowHierarchy(File(folder, "saf-before-cleanup.xml"))
            device.takeScreenshot(File(folder, "saf-before-cleanup.png"))
            throw AssertionError("Control absent: $selector; foreground=${device.currentPackageName}")
        }

    private fun clickFresh(selector: BySelector) {
        repeat(3) {
            try { visible(selector).click(); return }
            catch (_: StaleObjectException) { device.waitForIdle(1000) }
        }
        throw AssertionError("Picker repeatedly replaced its node: $selector")
    }

    private fun fixtureAction(method: String) {
        ParcelFileDescriptor.AutoCloseInputStream(instrumentation.uiAutomation.executeShellCommand(
            "am start -W -n com.mandu.reader.test/com.mandu.reader.ImportFixtureActivity --es method $method --es fixture $fixture"
        )).use { it.readBytes() }
    }
}
