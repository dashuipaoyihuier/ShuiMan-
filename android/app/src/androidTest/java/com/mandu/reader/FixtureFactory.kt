package com.mandu.reader

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.pdf.PdfDocument
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.util.UUID
import java.util.zip.CRC32
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

/** Creates small, original format fixtures in the target app cache at test time. */
internal object FixtureFactory {
    internal data class Set(
        val root: File,
        val image: File,
        val pdf: File,
        val epub: File,
        val cbz: File
    )

    fun create(context: Context): Set {
        val root = File(context.cacheDir, "format-device-fixtures-${UUID.randomUUID()}")
        check(root.mkdirs()) { "Could not create test fixture directory" }

        val image = File(root, "single.png")
        writePng(image, Color.rgb(210, 40, 35))

        val pdf = File(root, "pages.pdf")
        writePdf(pdf)

        val cover = pngBytes(Color.rgb(245, 160, 30))
        val page = pngBytes(Color.rgb(35, 90, 215))
        val second = pngBytes(Color.rgb(45, 170, 85))

        val epub = File(root, "spine-contract.epub")
        writeEpub(epub, cover, page, second)

        val cbz = File(root, "natural-order.cbz")
        writeZip(cbz, linkedMapOf(
            "10.png" to pngBytes(Color.rgb(70, 75, 220)),
            "2.png" to pngBytes(Color.rgb(50, 175, 85)),
            "1.png" to pngBytes(Color.rgb(220, 55, 50)),
            "notes.txt" to "not a comic page".toByteArray()
        ))
        return Set(root, image, pdf, epub, cbz)
    }

    private fun writePng(file: File, color: Int) {
        FileOutputStream(file).use { output ->
            check(solidBitmap(color).compress(Bitmap.CompressFormat.PNG, 100, output))
        }
    }

    private fun pngBytes(color: Int): ByteArray = ByteArrayOutputStream().use { output ->
        check(solidBitmap(color).compress(Bitmap.CompressFormat.PNG, 100, output))
        output.toByteArray()
    }

    private fun solidBitmap(color: Int): Bitmap = Bitmap.createBitmap(48, 72, Bitmap.Config.ARGB_8888).also {
        it.eraseColor(color)
    }

    private fun writePdf(file: File) {
        val document = PdfDocument()
        try {
            listOf(Color.rgb(125, 55, 210), Color.rgb(35, 160, 190)).forEachIndexed { index, color ->
                val info = PdfDocument.PageInfo.Builder(120, 180, index + 1).create()
                val page = document.startPage(info)
                page.canvas.drawColor(color)
                document.finishPage(page)
            }
            FileOutputStream(file).use { document.writeTo(it) }
        } finally {
            document.close()
        }
    }

    private fun writeEpub(file: File, cover: ByteArray, page: ByteArray, second: ByteArray) {
        val manifest = """
            <package version="3.0" unique-identifier="book" xmlns="http://www.idpf.org/2007/opf">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>Generated EPUB</dc:title><meta name="cover" content="cover-image"/></metadata>
              <manifest>
                <item id="cover" href="cover.xhtml" media-type="application/xhtml+xml"/>
                <item id="normal" href="normal.xhtml" media-type="application/xhtml+xml"/>
                <item id="missing" href="missing.xhtml" media-type="application/xhtml+xml"/>
                <item id="complex" href="complex.xhtml" media-type="application/xhtml+xml"/>
                <item id="cover-image" href="images/cover.png" media-type="image/png" properties="cover-image"/>
                <item id="page-image" href="images/page.png" media-type="image/png"/>
                <item id="second-image" href="images/second.png" media-type="image/png"/>
              </manifest>
              <spine page-progression-direction="rtl">
                <itemref idref="cover"/><itemref idref="normal"/><itemref idref="normal"/><itemref idref="missing"/><itemref idref="complex"/>
              </spine>
            </package>
        """.trimIndent().toByteArray()
        val entries = linkedMapOf(
            "mimetype" to "application/epub+zip".toByteArray(),
            "META-INF/container.xml" to """
                <?xml version="1.0"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles><rootfile full-path="OPS/book.opf" media-type="application/oebps-package+xml"/></rootfiles>
                </container>
            """.trimIndent().toByteArray(),
            "OPS/book.opf" to manifest,
            "OPS/cover.xhtml" to "<html><body><img src=\"images/cover.png\"/></body></html>".toByteArray(),
            "OPS/normal.xhtml" to "<html><body><img src=\"images/page.png\"/></body></html>".toByteArray(),
            // `missing.xhtml` is intentionally in the spine/manifest but absent from the ZIP.
            "OPS/complex.xhtml" to "<html><body><p>Required caption</p><img src=\"images/page.png\"/><img src=\"images/second.png\"/></body></html>".toByteArray(),
            "OPS/images/cover.png" to cover,
            "OPS/images/page.png" to page,
            "OPS/images/second.png" to second
        )
        ZipOutputStream(FileOutputStream(file)).use { output ->
            entries.forEach { (name, bytes) ->
                val entry = ZipEntry(name)
                if (name == "mimetype") {
                    entry.method = ZipEntry.STORED
                    entry.size = bytes.size.toLong()
                    entry.compressedSize = bytes.size.toLong()
                    entry.crc = CRC32().apply { update(bytes) }.value
                }
                output.putNextEntry(entry)
                output.write(bytes)
                output.closeEntry()
            }
        }
    }

    private fun writeZip(file: File, entries: Map<String, ByteArray>) {
        ZipOutputStream(FileOutputStream(file)).use { output ->
            entries.forEach { (name, bytes) ->
                output.putNextEntry(ZipEntry(name))
                output.write(bytes)
                output.closeEntry()
            }
        }
    }
}
