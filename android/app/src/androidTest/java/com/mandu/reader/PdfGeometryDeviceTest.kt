package com.mandu.reader

import android.graphics.Color
import android.net.Uri
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.mandu.reader.data.BookRepository
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.ByteArrayOutputStream
import java.io.File

@RunWith(AndroidJUnit4::class)
class PdfGeometryDeviceTest {
    @Test fun mixedPageBoxesAndIntrinsicRotationKeepTheirOwnAspectWithoutWhitePadding() = runBlocking {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val file = File.createTempFile("geometry-", ".pdf", context.cacheDir)
        val repository = BookRepository(context)
        var id: String? = null
        try {
            val pages = listOf(
                Triple("[0 0 300 600]", "", 0),
                Triple("[0 0 300 600]", "", 90),
                Triple("[20 40 633 927]", "/CropBox [71 113 472 720]", 0),
                Triple("[20 40 633 927]", "/CropBox [71 113 472 720]", 90),
                Triple("[20 40 633 927]", "/CropBox [71 113 472 720]", 270)
            )
            val stream = "0.1 0.2 0.8 rg -200 -200 2000 2000 re f\n"
            val objects = mutableListOf("<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Count ${pages.size} /Kids [${pages.indices.joinToString(" ") { "${3+it*2} 0 R" }}] >>")
            pages.forEachIndexed { index, (media,crop,angle) ->
                objects += "<< /Type /Page /Parent 2 0 R /MediaBox $media $crop /Rotate $angle /Resources <<>> /Contents ${4+index*2} 0 R >>"
                objects += "<< /Length ${stream.toByteArray().size} >>\nstream\n${stream}endstream"
            }
            val out = ByteArrayOutputStream()
            fun write(value: String) { out.write(value.toByteArray(Charsets.US_ASCII)) }
            write("%PDF-1.4\n")
            val offsets = objects.mapIndexed { index, body -> out.size().also { write("${index+1} 0 obj\n$body\nendobj\n") } }
            val xref = out.size()
            write("xref\n0 ${objects.size+1}\n0000000000 65535 f \n")
            offsets.forEach { write("%010d 00000 n \n".format(it)) }
            write("trailer\n<< /Size ${objects.size+1} /Root 1 0 R >>\nstartxref\n$xref\n%%EOF\n")
            file.writeBytes(out.toByteArray())
            val record = repository.importFile(Uri.fromFile(file)); id = record.id
            repository.open(record).use { book ->
                val ratios = listOf(0.5,2.0,401.0/607,607.0/401,607.0/401)
                ratios.forEachIndexed { index, ratio ->
                    val unit = book.publication.units[index]
                    assertEquals("page $index metadata", ratio,unit.width.toDouble()/unit.height,0.003)
                    val image = book.render(index,997)
                    assertEquals("page $index render",ratio,image.width.toDouble()/image.height,0.003)
                    for ((x,y) in listOf(1 to 1, image.width-2 to 1, 1 to image.height-2, image.width-2 to image.height-2)) {
                        val pixel = image.getPixel(x,y)
                        assertTrue("page $index has white padding",Color.blue(pixel)>150 && Color.red(pixel)<80)
                    }
                }
            }
        } finally { id?.let { repository.remove(it) }; file.delete() }
    }
}
