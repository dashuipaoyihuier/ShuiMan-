package com.mandu.reader.data

import com.mandu.reader.core.ReadingDirection
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.nio.file.Files
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

class EpubParserTest {
    @Test
    fun spineOccurrencesMissingSlotsCssCoverAndNavigationStayStable() {
        val file = Files.createTempFile("epub-order-", ".epub").toFile()
        try {
            zip(file, linkedMapOf(
                "META-INF/container.xml" to """
                    <container><rootfiles><rootfile full-path="OPS/package.opf"/></rootfiles></container>
                """.trimIndent(),
                "OPS/package.opf" to """
                    <package version="3.0" xmlns:dc="http://purl.org/dc/elements/1.1/">
                      <metadata><dc:title>顺序测试</dc:title><meta name="cover" content="image"/></metadata>
                      <manifest>
                        <item id="p" href="pages/p.xhtml" media-type="application/xhtml+xml"/>
                        <item id="missing" href="pages/missing.xhtml" media-type="application/xhtml+xml"/>
                        <item id="aux" href="pages/aux.xhtml" media-type="application/xhtml+xml"/>
                        <item id="image" href="images/cover.jpg" media-type="image/jpeg" properties="cover-image"/>
                        <item id="css" href="styles/main.css" media-type="text/css"/>
                        <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                      </manifest>
                      <spine page-progression-direction="rtl">
                        <itemref idref="p"/><itemref idref="p"/><itemref idref="unknown"/>
                        <itemref idref="missing"/><itemref idref="aux" linear="no"/>
                      </spine>
                    </package>
                """.trimIndent(),
                "OPS/pages/p.xhtml" to """
                    <html><head><link rel="stylesheet" href="../styles/main.css"/></head>
                    <body><img class="turn" src="../images/cover.jpg"/></body></html>
                """.trimIndent(),
                "OPS/pages/aux.xhtml" to "<html><body>auxiliary</body></html>",
                "OPS/styles/main.css" to ".turn { transform: rotate(90deg); }",
                "OPS/images/cover.jpg" to "not-decoded-in-local-unit-test",
                "OPS/nav.xhtml" to """
                    <html xmlns:epub="http://www.idpf.org/2007/ops"><body>
                    <nav epub:type="toc"><a href="pages/p.xhtml#panel">第一章</a></nav>
                    </body></html>
                """.trimIndent()
            ))
            ZipArchive(file).use { archive ->
                val result = EpubParser.parse(archive)
                assertEquals("顺序测试", result.title)
                assertEquals(ReadingDirection.RTL, result.direction)
                assertEquals(4, result.units.size)
                assertEquals(listOf(0, 1, 2, 3), result.units.map { it.locator.occurrence })
                assertEquals("OPS/pages/p.xhtml", result.units[0].locator.resource)
                assertEquals("OPS/pages/p.xhtml", result.units[1].locator.resource)
                assertTrue(result.units[0].id != result.units[1].id)
                assertTrue(result.units[0].isCover)
                assertEquals(90, result.units[0].rotationHint)
                assertNotNull(result.units[2].error)
                assertEquals("OPS/pages/missing.xhtml", result.units[3].locator.resource)
                assertNotNull(result.units[3].error)
                assertEquals(0, result.navigation.single().index)
                assertEquals("panel", result.navigation.single().fragment)
            }
        } finally {
            file.delete()
        }
    }

    @Test(expected = ArchiveException::class)
    fun traversalOutsideArchiveIsRejected() {
        ZipArchive.resolve("../../../secret", "OPS/pages/page.xhtml")
    }

    private fun zip(file: File, entries: Map<String, String>) {
        ZipOutputStream(file.outputStream()).use { output ->
            entries.forEach { (name, value) ->
                output.putNextEntry(ZipEntry(name))
                output.write(value.toByteArray())
                output.closeEntry()
            }
        }
    }
}
