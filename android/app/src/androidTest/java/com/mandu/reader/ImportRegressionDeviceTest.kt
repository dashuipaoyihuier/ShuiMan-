package com.mandu.reader

import android.net.Uri
import android.content.Intent
import android.os.SystemClock
import android.provider.DocumentsContract
import androidx.activity.compose.setContent
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.lifecycle.ViewModelProvider
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import com.mandu.reader.data.BookRepository
import com.mandu.reader.ui.ImportViewModel
import com.mandu.reader.ui.ManduApp
import com.mandu.reader.ui.ManduTheme
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class ImportRegressionDeviceTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)
    private val repository by lazy { BookRepository(context) }
    private val authority = "com.mandu.reader.test.documents"
    private val fixtureId = "slow-${UUID.randomUUID()}"
    private val tree get() = DocumentsContract.buildTreeDocumentUri(authority, fixtureId)
    private var scenario: ActivityScenario<MainActivity>? = null
    private lateinit var worker: ImportViewModel

    @Before fun prepare() {
        device.wakeUp()
        fixtureAction("make-fixtures")
        val deadline = SystemClock.uptimeMillis() + 10_000
        while (SystemClock.uptimeMillis() < deadline) {
            val ready = runCatching { context.contentResolver.query(
                DocumentsContract.buildDocumentUriUsingTree(tree, fixtureId), null, null, null, null
            )?.use { it.moveToFirst() } == true }.getOrDefault(false)
            if (ready) return
            SystemClock.sleep(50)
        }
        fail("Test provider failed to create and grant the generated fixture tree")
    }
    @After fun cleanup(): Unit = runBlocking {
        instrumentation.runOnMainSync { if (::worker.isInitialized) worker.cancel() }
        scenario?.close()
        repository.books(false).filter { it.uri.contains(fixtureId) }.forEach { repository.remove(it.id) }
        fixtureAction("remove-fixtures")
        SystemClock.sleep(500)
    }

    private fun fixtureAction(method: String) {
        // Shell launch is deterministic even when the previous scenario has left the app in background.
        android.os.ParcelFileDescriptor.AutoCloseInputStream(instrumentation.uiAutomation.executeShellCommand(
            "am start -W -n com.mandu.reader.test/com.mandu.reader.ImportFixtureActivity --es method $method --es fixture $fixtureId"
        )).use { it.readBytes() }
    }

    @Test fun batchPickerConsumptionCannotStrandSpinnerAndBadBookDoesNotStopOthers(): Unit = runBlocking {
        val files = (1..12).map { DocumentsContract.buildDocumentUriUsingTree(tree, "$fixtureId/卷$it.cbz") } +
            DocumentsContract.buildDocumentUriUsingTree(tree, "$fixtureId/损坏.cbz")
        launchWithPending(files, null)
        waitFor("Volumes should enter the shelf before full indexing finishes") {
            repository.books(false).count { it.uri.contains(fixtureId) } == 13 && status().running
        }
        // This used to cancel the LaunchedEffect before `importing = false` and refresh completed.
        waitFor("Batch importer must reach a terminal state") { !status().running && status().message.startsWith("导入完成") }
        assertEquals(1, status().failed)
        assertEquals(12, repository.books(false).count { it.uri.contains(fixtureId) && it.pageCount == 3 })
        assertTrue(device.wait(Until.findObject(By.text("选择文件")), 5000).isEnabled)
    }

    @Test fun recursiveTreeIncludesImageVolumeAndSurvivesActivityRecreation(): Unit = runBlocking {
        launchWithPending(emptyList(), tree)
        waitFor("Directory importer should begin enrichment") { status().message.startsWith("补全索引") }
        scenario!!.recreate()
        scenario!!.onActivity { assertSame(worker, ViewModelProvider(it)[ImportViewModel::class.java]) }
        waitFor("Tree importer must complete after recreation") { !status().running }
        val books = repository.books(false).filter { it.uri.contains(fixtureId) }
        assertEquals(14, books.size)
        assertEquals(1, status().failed)
        val images = books.single { it.title == "图片册" }
        repository.open(images).use { opened ->
            assertEquals(listOf("1.png", "2.png", "10.png"), opened.publication.units.map { it.title })
            assertEquals(48, opened.render(0, 72).width)
        }
    }

    @Test fun cancellingKeepsRegisteredBooksAndReenablesPicker(): Unit = runBlocking {
        launchWithPending(emptyList(), tree)
        waitFor("Wait until complete registration") { status().message.startsWith("补全索引") }
        instrumentation.runOnMainSync { worker.cancel() }
        waitFor("Cancellation should finish promptly", 5000) { !status().running }
        assertTrue(status().message.startsWith("已取消"))
        assertEquals(14, repository.books(false).count { it.uri.contains(fixtureId) })
        assertTrue(device.wait(Until.findObject(By.text("选择文件夹")), 5000).isEnabled)
    }

    @Test fun enrichmentAndOpeningPreserveEditsAndNeverResurrectRemovedBooks(): Unit = runBlocking {
        val uri = DocumentsContract.buildDocumentUriUsingTree(tree, "$fixtureId/卷1.cbz")
        val original = repository.importFile(uri, indexContents = false)
        val edited = original.copy(title = "我的书名", tags = listOf("收藏测试"), favorite = true,
            progress = 2, readStatus = "在读")
        repository.save(edited)
        val indexed = requireNotNull(repository.indexBook(original.id))
        assertEquals(edited.title, indexed.title)
        assertEquals(edited.tags, indexed.tags)
        assertTrue(indexed.favorite)
        assertEquals(2, indexed.progress)
        repository.open(original).close() // Deliberately stale UI snapshot must not overwrite edits/cover.
        val reopened = repository.books(false).single { it.id == original.id }
        assertEquals(edited.title, reopened.title)
        assertEquals(indexed.coverPath, reopened.coverPath)
        assertEquals(2, reopened.progress)
        repository.remove(original.id)
        assertNull(repository.indexBook(original.id))
    }

    private fun launchWithPending(files: List<Uri>, folder: Uri?) {
        scenario = ActivityScenario.launch(MainActivity::class.java)
        scenario!!.onActivity { activity ->
            worker = ViewModelProvider(activity)[ImportViewModel::class.java]
            activity.setContent {
                var pendingFiles by remember { mutableStateOf(files) }
                var pendingFolder by remember { mutableStateOf(folder) }
                ManduTheme { ManduApp(repository, null, pendingFiles, pendingFolder,
                    { pendingFiles = emptyList() }, { pendingFolder = null }, {}, {}, {}, imports = worker) }
            }
        }
        device.wait(Until.findObject(By.text("导入")), 5000).click()
    }
    private fun status(): com.mandu.reader.ui.ImportStatus {
        var snapshot: com.mandu.reader.ui.ImportStatus? = null
        instrumentation.runOnMainSync { snapshot = worker.status }
        return requireNotNull(snapshot)
    }
    private suspend fun waitFor(message: String, timeout: Long = 60_000, condition: suspend () -> Boolean) {
        val deadline = SystemClock.uptimeMillis() + timeout
        while (SystemClock.uptimeMillis() < deadline) {
            if (condition()) return
            kotlinx.coroutines.delay(50)
        }
        fail("$message; status=${status()}")
    }
}
