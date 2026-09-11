package com.mandu.reader.data

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test
import java.io.File
import java.nio.file.Files
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

class ZipArchiveTest {
    @Test
    fun exactEntriesAreReadOnDemand() {
        val file = zip(listOf("OPS/a.txt" to "one".toByteArray(), "OPS/A.txt" to "two".toByteArray()))
        try {
            ZipArchive(file).use { archive ->
                assertArrayEquals("one".toByteArray(), archive.bytes("OPS/a.txt"))
                assertArrayEquals("two".toByteArray(), archive.bytes("OPS/A.txt"))
                assertFalse(archive.contains("ops/a.txt"))
            }
        } finally { file.delete() }
    }

    @Test(expected = ArchiveException::class)
    fun normalizedDuplicateEntriesAreRejected() {
        val file = zip(listOf("same.txt" to byteArrayOf(1), "folder/../same.txt" to byteArrayOf(2)))
        try { ZipArchive(file).close() } finally { file.delete() }
    }

    @Test(expected = ArchiveException::class)
    fun entryTraversalIsRejectedAtOpen() {
        val file = zip(listOf("../outside.txt" to byteArrayOf(1)))
        try { ZipArchive(file).close() } finally { file.delete() }
    }

    @Test
    fun queryAndFragmentDoNotChangeResolvedResource() {
        assertEquals("OPS/image/页 1.png", ZipArchive.resolve("../image/%E9%A1%B5%201.png?v=2#xy", "OPS/text/p.xhtml"))
        assertEquals("xy", ZipArchive.fragment("p.xhtml#xy"))
    }

    private fun zip(entries: List<Pair<String, ByteArray>>): File {
        val file = Files.createTempFile("bounded-archive-", ".zip").toFile()
        ZipOutputStream(file.outputStream()).use { output ->
            entries.forEach { (name, bytes) ->
                output.putNextEntry(ZipEntry(name))
                output.write(bytes)
                output.closeEntry()
            }
        }
        return file
    }
}

