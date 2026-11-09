package com.mandu.reader

import android.content.Intent
import android.os.SystemClock
import android.provider.DocumentsContract
import android.graphics.Bitmap
import android.graphics.Color
import android.net.Uri
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.mandu.reader.core.BookKind
import com.mandu.reader.data.BookRepository
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.ByteArrayOutputStream
import java.io.File
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class MobiDeviceTest {
    @Test fun safGenericMimeAndRecursiveDirectoryImportMobi() = runBlocking {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        val repository = BookRepository(context)
        val fixture = "slow-mobi-${UUID.randomUUID()}"
        val authority = "com.mandu.reader.test.documents"
        val tree = DocumentsContract.buildTreeDocumentUri(authority,fixture)
        val bitmap = Bitmap.createBitmap(40,60,Bitmap.Config.ARGB_8888).apply { eraseColor(Color.BLUE) }
        val image = ByteArrayOutputStream().apply { bitmap.compress(Bitmap.CompressFormat.PNG,100,this) }.toByteArray()
        bitmap.recycle()
        val bytes = MobiFixture.book(images=listOf(image,image,image))
        fun fixtureAction(method: String) {
            context.startActivity(Intent().setClassName("com.mandu.reader.test", "com.mandu.reader.ImportFixtureActivity")
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK).putExtra("method",method).putExtra("fixture",fixture).putExtra("mobi",bytes))
        }
        try {
            fixtureAction("make-mobi-fixtures")
            val deadline = SystemClock.uptimeMillis()+10000
            while (SystemClock.uptimeMillis()<deadline) {
                val ready = runCatching { context.contentResolver.query(DocumentsContract.buildDocumentUriUsingTree(tree,fixture),null,null,null,null)?.use { it.moveToFirst() } == true }.getOrDefault(false)
                if (ready) break
                SystemClock.sleep(50)
            }
            val directoryBooks = repository.importTree(tree)
            assertEquals(1,directoryBooks.size)
            assertEquals(BookKind.MOBI,directoryBooks.single().kind)
            val unknown = DocumentsContract.buildDocumentUriUsingTree(tree,"$fixture/unknown.bin")
            assertEquals("application/octet-stream",context.contentResolver.getType(unknown))
            val record = repository.importFile(unknown)
            assertEquals(BookKind.MOBI,record.kind)
            repository.open(record).use { assertEquals(Color.BLUE,it.render(1,100).getPixel(10,10)) }
        } finally {
            repository.books(false).filter { it.uri.contains(fixture) }.forEach { repository.remove(it.id) }
            fixtureAction("remove-fixtures")
        }
    }

    @Test fun importsMobiByExtensionAndSignatureAndRendersBodyOrder() = runBlocking {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val folder = File(context.cacheDir, "mobi-${UUID.randomUUID()}").apply { mkdirs() }
        val repository = BookRepository(context)
        val ids = mutableListOf<String>()
        fun png(color: Int): ByteArray {
            val bitmap = Bitmap.createBitmap(60,100,Bitmap.Config.ARGB_8888)
            bitmap.eraseColor(color)
            return ByteArrayOutputStream().use { out ->
                bitmap.compress(Bitmap.CompressFormat.PNG,100,out); bitmap.recycle(); out.toByteArray()
            }
        }
        try {
            for ((name, compressed) in listOf("comic.MOBI" to false, "provider-unknown.bin" to true)) {
                val bytes = MobiFixture.book(compressed=compressed, trailer=compressed,
                    images=listOf(png(Color.RED), png(Color.GREEN), png(Color.BLUE)))
                val file = File(folder,name).apply { writeBytes(bytes) }
                val record = repository.importFile(Uri.fromFile(file)); ids += record.id
                assertEquals(BookKind.MOBI,record.kind)
                repository.open(record).use { book ->
                    val units = book.publication.units
                    assertEquals(5,units.size)
                    assertEquals(5,units.map { it.id }.toSet().size)
                    assertNotNull(units.last().error)
                    for ((index,color) in listOf(0 to Color.GREEN, 1 to Color.BLUE, 2 to Color.RED, 3 to Color.BLUE)) {
                        val image = book.render(index,100)
                        assertEquals(60,image.width); assertEquals(100,image.height)
                        assertEquals(color,image.getPixel(30,50))
                    }
                }
                if (name.endsWith("MOBI")) {
                    val legacy = record.copy(kind=BookKind.IMAGES)
                    repository.save(legacy)
                    repository.open(legacy).use { assertEquals(BookKind.MOBI,it.publication.kind) }
                    assertEquals(BookKind.MOBI,repository.books(false).single { it.id == record.id }.kind)
                }
                assertArrayEquals(bytes,file.readBytes())
                repository.open(record).use { assertEquals(5,it.publication.units.size) }
            }
        } finally { ids.forEach { repository.remove(it) }; folder.deleteRecursively() }
    }
}
