package com.mandu.reader.data

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import com.mandu.reader.core.BookRecord
import com.mandu.reader.core.ReadingState

internal class LibraryDatabase(context: Context) :
    SQLiteOpenHelper(context, "library.sqlite", null, VERSION) {

    companion object { private const val VERSION = 2 }

    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL("CREATE TABLE books (id TEXT PRIMARY KEY NOT NULL, snapshot TEXT NOT NULL, opened INTEGER NOT NULL DEFAULT 0)")
        db.execSQL("CREATE TABLE reading_states (book_id TEXT PRIMARY KEY NOT NULL, snapshot TEXT NOT NULL, updated INTEGER NOT NULL)")
        db.execSQL("CREATE TABLE source_indices (book_id TEXT NOT NULL, locator_key TEXT NOT NULL, unit_index INTEGER NOT NULL, PRIMARY KEY(book_id, locator_key))")
    }

    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) {
        if (oldVersion < 2) {
            db.execSQL("CREATE TABLE source_indices (book_id TEXT NOT NULL, locator_key TEXT NOT NULL, unit_index INTEGER NOT NULL, PRIMARY KEY(book_id, locator_key))")
        }
    }

    fun books(): List<BookRecord> = readableDatabase.query(
        "books", arrayOf("snapshot"), null, null, null, null, "opened DESC, id ASC"
    ).use { cursor ->
        buildList {
            while (cursor.moveToNext()) {
                runCatching { DataJson.parseBook(cursor.getString(0)) }.getOrNull()?.let(::add)
            }
        }
    }

    fun save(book: BookRecord) {
        val values = ContentValues().apply {
            put("id", book.id)
            put("snapshot", DataJson.book(book))
            put("opened", book.lastOpened)
        }
        writableDatabase.insertWithOnConflict("books", null, values, SQLiteDatabase.CONFLICT_REPLACE)
    }

    fun book(id: String): BookRecord? = readableDatabase.query(
        "books", arrayOf("snapshot"), "id = ?", arrayOf(id), null, null, null, "1"
    ).use { cursor -> if (cursor.moveToFirst()) DataJson.parseBook(cursor.getString(0)) else null }

    fun updateIndex(id: String, locatorKeys: List<String>, cover: String?, openedAt: Long? = null): BookRecord? {
        val db = writableDatabase
        db.beginTransaction()
        try {
            val current = book(id) ?: return null
            replaceIndices(id, locatorKeys)
            val updated = current.copy(pageCount = locatorKeys.size, coverPath = cover ?: current.coverPath,
                lastOpened = openedAt ?: current.lastOpened, missing = false)
            save(updated)
            db.setTransactionSuccessful()
            return updated
        } finally { db.endTransaction() }
    }

    fun remove(id: String) {
        writableDatabase.delete("books", "id = ?", arrayOf(id))
    }

    fun loadState(id: String): ReadingState = readableDatabase.query(
        "reading_states", arrayOf("snapshot"), "book_id = ?", arrayOf(id), null, null, null
    ).use { cursor ->
        if (cursor.moveToFirst()) runCatching { DataJson.parseState(cursor.getString(0)) }.getOrDefault(ReadingState())
        else ReadingState()
    }

    fun saveState(id: String, state: ReadingState) {
        val values = ContentValues().apply {
            put("book_id", id)
            put("snapshot", DataJson.state(state))
            put("updated", System.currentTimeMillis())
        }
        writableDatabase.insertWithOnConflict("reading_states", null, values, SQLiteDatabase.CONFLICT_REPLACE)
    }

    /** Atomically saves the locator snapshot and derives progress from the current book row. */
    fun saveStateAndProgress(id: String, state: ReadingState, completed: Boolean) {
        val database = writableDatabase
        val now = System.currentTimeMillis()
        database.beginTransaction()
        try {
            database.insertWithOnConflict("reading_states", null, ContentValues().apply {
                put("book_id", id)
                put("snapshot", DataJson.state(state))
                put("updated", now)
            }, SQLiteDatabase.CONFLICT_REPLACE)

            val book = database.query(
                "books", arrayOf("snapshot"), "id = ?", arrayOf(id), null, null, null, "1"
            ).use { cursor ->
                if (cursor.moveToFirst()) runCatching { DataJson.parseBook(cursor.getString(0)) }.getOrNull() else null
            }
            if (book != null) {
                val locatedPosition = state.locator?.let { locator ->
                    database.query(
                        "source_indices", arrayOf("unit_index"),
                        "book_id = ? AND locator_key = ?", arrayOf(id, locator.key),
                        null, null, null, "1"
                    ).use { cursor ->
                        if (cursor.moveToFirst()) cursor.getInt(0) + 1 else null
                    }
                } ?: book.progress
                val position = if (completed && book.pageCount > 0) book.pageCount else locatedPosition
                val status = when {
                    completed || book.pageCount > 0 && position >= book.pageCount -> "已读"
                    position > 0 -> "在读"
                    else -> book.readStatus
                }
                val updated = book.copy(
                    progress = if (book.pageCount > 0) position.coerceAtMost(book.pageCount) else position,
                    readStatus = status,
                    lastOpened = now
                )
                database.insertWithOnConflict("books", null, ContentValues().apply {
                    put("id", updated.id)
                    put("snapshot", DataJson.book(updated))
                    put("opened", updated.lastOpened)
                }, SQLiteDatabase.CONFLICT_REPLACE)
            }
            database.setTransactionSuccessful()
        } finally {
            database.endTransaction()
        }
    }

    fun replaceIndices(bookId: String, locatorKeys: List<String>) {
        writableDatabase.beginTransaction()
        try {
            writableDatabase.delete("source_indices", "book_id = ?", arrayOf(bookId))
            locatorKeys.forEachIndexed { index, key ->
                writableDatabase.insertOrThrow("source_indices", null, ContentValues().apply {
                    put("book_id", bookId)
                    put("locator_key", key)
                    put("unit_index", index)
                })
            }
            writableDatabase.setTransactionSuccessful()
        } finally {
            writableDatabase.endTransaction()
        }
    }

    fun indexOf(bookId: String, locatorKey: String): Int? = readableDatabase.query(
        "source_indices", arrayOf("unit_index"), "book_id = ? AND locator_key = ?",
        arrayOf(bookId, locatorKey), null, null, null, "1"
    ).use { cursor -> if (cursor.moveToFirst()) cursor.getInt(0) else null }
}
