package com.mandu.reader

import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.pdf.PdfDocument
import android.net.Uri
import android.os.SystemClock
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import com.mandu.reader.core.*
import com.mandu.reader.data.BookRepository
import com.mandu.reader.data.ReaderPageRenderer
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File

@RunWith(AndroidJUnit4::class)
class PdfWhiteMarginsDeviceTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)
    private val repository by lazy { BookRepository(context) }
    private val files = mutableListOf<File>()
    private val records = mutableListOf<BookRecord>()
    private var scenario: ActivityScenario<MainActivity>? = null

    @After fun cleanup(): Unit = runBlocking {
        scenario?.close()
        records.forEach { repository.remove(it.id); it.coverPath?.let(::File)?.delete() }
        files.forEach { it.delete() }
    }

    @Test fun asymmetricMarginsBlankPagesTinyMarksAndFaintCaptionsAreConservative(): Unit = runBlocking {
        val image = Bitmap.createBitmap(400, 600, Bitmap.Config.ARGB_8888)
        try {
            image.eraseColor(Color.WHITE)
            assertEquals(Rect(0,0,400,600), ReaderPageRenderer.whiteMarginBounds(image))
            image.setPixel(210,180,Color.BLACK)
            assertEquals(Rect(0,0,400,600), ReaderPageRenderer.whiteMarginBounds(image))
            image.eraseColor(Color.WHITE)
            val canvas = Canvas(image)
            canvas.drawRect(80f,160f,320f,400f,Paint().apply { color=Color.RED })
            assertEquals(Rect(77,157,323,403), ReaderPageRenderer.whiteMarginBounds(image))
            // A faint one-pixel caption at the bottom must protect its position.
            image.setPixel(100,580,Color.rgb(249,249,249))
            assertEquals(Rect(77,157,323,584), ReaderPageRenderer.whiteMarginBounds(image))
            image.eraseColor(Color.rgb(245,245,245))
            assertEquals(Rect(0,0,400,600), ReaderPageRenderer.whiteMarginBounds(image))
        } finally { image.recycle() }
    }

    @Test fun pdfCropAndRotationKeepPhysicalSizeRestoreOriginalAndProtectJoinedSpread(): Unit = runBlocking {
        val (file, record) = fixture()
        val bytes = file.readBytes()
        repository.open(record).use { book ->
            val single = DisplayGroup(listOf(0))
            assertTrue(ReaderPageRenderer.shouldCrop(book.publication,single,ReaderPreferences()))
            assertFalse(ReaderPageRenderer.shouldCrop(book.publication,DisplayGroup(listOf(0,1),spread=true),ReaderPreferences()))
            assertFalse(ReaderPageRenderer.shouldCrop(book.publication.copy(kind=BookKind.CBZ),single,ReaderPreferences()))
            assertFalse(ReaderPageRenderer.shouldCrop(book.publication,single,ReaderPreferences(cropPDFWhiteMargins=false)))
            val full = ReaderPageRenderer.render(book,0,600,0,false)
            val cropped = ReaderPageRenderer.render(book,0,600,0,true)
            assertEquals(PagePixelSize(400.0,600.0),full.sourceSize)
            assertEquals(400,full.bitmap.width); assertEquals(600,full.bitmap.height)
            assertTrue(cropped.bitmap.width in 244..248)
            assertEquals(cropped.bitmap.width,cropped.bitmap.height)
            assertEquals(1.0,cropped.sourceSize.ratio,.001)
            val rotated = ReaderPageRenderer.render(book,0,600,90,true)
            assertEquals(cropped.sourceSize,rotated.sourceSize)
            assertEquals(Color.RED,rotated.bitmap.getPixel(rotated.bitmap.width/2,rotated.bitmap.height/2))
            val rotatedFull = ReaderPageRenderer.render(book,0,600,90,false)
            assertEquals(PagePixelSize(600.0,400.0),rotatedFull.sourceSize)
            assertEquals(600,rotatedFull.bitmap.width); assertEquals(400,rotatedFull.bitmap.height)
            val restored = ReaderPageRenderer.render(book,0,600,0,false)
            assertEquals(Color.WHITE,restored.bitmap.getPixel(10,10))
            assertEquals(400,restored.bitmap.width)
            // Analysis and thumbnail callers still receive the complete source page.
            assertEquals(600,book.render(0,600).height)
            assertEquals(400,book.publication.units[0].width)
        }
        assertArrayEquals(bytes,file.readBytes())
    }

    @Test fun readerToggleImmediatelyResizesContentAndPersistsAcrossReopening(): Unit = runBlocking {
        val (_, record) = fixture()
        openReader(record,PageTurnMode.TAP)
        val cropped = awaitRedWidth(.90,1.01)
        assertEquals(1.0,cropped.width().toDouble()/cropped.height(),.025)
        toggleCrop()
        val original = awaitRedWidth(.40,.72)
        assertTrue(cropped.width()>original.width()*1.4)
        // Switch back to the cached cropped version, then restore the full page again.
        toggleCrop(); awaitRedWidth(.90,1.01)
        toggleCrop(); awaitRedWidth(.40,.72)
        scenario!!.close(); scenario=null
        assertFalse(repository.loadState(record.id).preferences.cropPDFWhiteMargins)
        launchAndOpen(record)
        awaitRedWidth(.40,.72)
        assertFalse(repository.loadState(record.id).preferences.cropPDFWhiteMargins)
    }

    @Test fun verticalReaderUsesCroppedPageHeightAndRestoresFullPage(): Unit = runBlocking {
        val (_, record) = fixture()
        openReader(record,PageTurnMode.SCROLL)
        val bounds = awaitRedWidth(.90,1.01)
        assertEquals(1.0,bounds.width().toDouble()/bounds.height(),.025)
        toggleCrop(); awaitRedWidth(.40,.72)
        toggleCrop(); awaitRedWidth(.90,1.01)
    }

    private suspend fun fixture(): Pair<File,BookRecord> {
        val file = File.createTempFile("crop-", ".pdf",context.cacheDir).also { files+=it }
        val pdf = PdfDocument()
        try {
            for (index in 0..1) {
                val page = pdf.startPage(PdfDocument.PageInfo.Builder(400,600,index+1).create())
                page.canvas.drawColor(Color.WHITE)
                page.canvas.drawRect(80f,160f,320f,400f,Paint().apply { color=if(index==0) Color.RED else Color.GREEN })
                pdf.finishPage(page)
            }
            file.outputStream().use(pdf::writeTo)
        } finally { pdf.close() }
        val record = repository.importFile(Uri.fromFile(file)).copy(title="PDF-CROP-${System.nanoTime()}",series="")
        repository.save(record); records+=record
        return file to record
    }
    private suspend fun openReader(record: BookRecord, mode: PageTurnMode) {
        repository.saveState(record.id,ReadingState(preferences=ReaderPreferences(
            layout=ReadingLayout.SINGLE,pageTurn=mode,smartSpreads=false,automaticPairs=false,
            automaticOrientation=false,autoHideControls=false)))
        launchAndOpen(record)
    }
    private fun launchAndOpen(record: BookRecord) {
        device.wakeUp()
        scenario = ActivityScenario.launch(Intent(context,MainActivity::class.java).apply {
            action=Intent.ACTION_MAIN; addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
        })
        device.wait(Until.findObject(By.text("浏览")),10000)!!.click()
        device.wait(Until.findObject(By.clazz("android.widget.EditText")),10000)!!.text=record.title
        device.settleLibrarySearch()
        device.wait(Until.findObject(By.desc("打开${record.title}")),10000)!!.click()
        device.wait(Until.findObject(By.desc("阅读设置")),10000)!!
    }
    private fun toggleCrop() {
        device.wait(Until.findObject(By.desc("阅读设置")),10000)!!.click()
        device.wait(Until.findObject(By.desc("PDF 自动裁白边开关")),10000)!!.click()
        device.pressBack()
    }
    private fun awaitRedWidth(minFraction: Double,maxFraction: Double): Rect {
        val deadline=SystemClock.uptimeMillis()+10000
        var last=Rect()
        while (SystemClock.uptimeMillis()<deadline) {
            val screenshot=instrumentation.uiAutomation.takeScreenshot()!!
            try {
                var left=screenshot.width; var right=-1; var top=screenshot.height; var bottom=-1
                val row=IntArray(screenshot.width)
                for(y in 0 until screenshot.height step 3) {
                    screenshot.getPixels(row,0,row.size,0,y,row.size,1)
                    for(x in row.indices step 3) {
                        val p=row[x]
                        if(Color.red(p)>180 && Color.green(p)<70 && Color.blue(p)<70) {
                            left=minOf(left,x);right=maxOf(right,x);top=minOf(top,y);bottom=maxOf(bottom,y)
                        }
                    }
                }
                if(right>=left) {
                    last=Rect(left,top,right+1,bottom+1)
                    if(last.width().toDouble()/screenshot.width in minFraction..maxFraction) return last
                }
            } finally { screenshot.recycle() }
            SystemClock.sleep(150)
        }
        throw AssertionError("Expected red content width in $minFraction..$maxFraction of screen; actual $last")
    }
}
