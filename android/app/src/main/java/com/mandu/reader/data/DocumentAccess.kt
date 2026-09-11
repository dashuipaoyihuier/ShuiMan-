package com.mandu.reader.data

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract

/** One provider query per directory, rather than a Binder call for each sort comparison. */
internal object DocumentAccess {
    data class Entry(val uri: Uri, val name: String, val directory: Boolean)

    fun documentUri(uri: Uri): Uri =
        if (DocumentsContract.isTreeUri(uri) && uri.pathSegments.size == 2)
            DocumentsContract.buildDocumentUriUsingTree(uri, DocumentsContract.getTreeDocumentId(uri))
        else uri

    fun children(context: Context, directory: Uri): List<Entry> {
        val parent = documentUri(directory)
        val children = DocumentsContract.buildChildDocumentsUriUsingTree(parent, DocumentsContract.getDocumentId(parent))
        val columns = arrayOf(
            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_MIME_TYPE
        )
        return context.contentResolver.query(children, columns, null, null, null)?.use { cursor ->
            buildList {
                while (cursor.moveToNext()) {
                    add(Entry(
                        DocumentsContract.buildDocumentUriUsingTree(parent, cursor.getString(0)),
                        cursor.getString(1).orEmpty(),
                        cursor.getString(2) == DocumentsContract.Document.MIME_TYPE_DIR
                    ))
                }
            }
        } ?: throw IllegalArgumentException("无法列出目录内容，请检查文件访问权限。")
    }
}
