package com.mandu.reader.core

import com.mandu.reader.data.NaturalOrder
import java.text.Normalizer
import java.util.Locale

/** A presentation-only hierarchy: never merges source records or reading positions. */
data class ComicSeries(val key: String, val title: String, val volumes: List<BookRecord>) {
    val cover: BookRecord get() = volumes.firstOrNull { it.coverPath != null } ?: volumes.first()
    val resume: BookRecord? get() = volumes.filter { it.lastOpened > 0 && !it.missing && it.readStatus != "已读" }
        .maxByOrNull { it.lastOpened }
}

object ComicLibrary {
    fun seriesKey(book: BookRecord): String? = book.series.trim().takeIf { it.isNotEmpty() }?.let {
        Normalizer.normalize(it, Normalizer.Form.NFKC).lowercase(Locale.ROOT)
    }

    val volumeOrder: Comparator<BookRecord> = Comparator { a, b ->
        val first = a.volume?.takeIf { it.isFinite() }
        val second = b.volume?.takeIf { it.isFinite() }
        val number = when {
            first != null && second != null -> first.compareTo(second)
            first != null -> -1
            second != null -> 1
            else -> 0
        }
        if (number != 0) number else NaturalOrder.compare(a.title, b.title).takeIf { it != 0 }
            ?: a.id.compareTo(b.id)
    }

    fun series(books: List<BookRecord>): List<ComicSeries> = books.filter { seriesKey(it) != null }
        .groupBy { seriesKey(it)!! }.map { (key, members) ->
            val ordered = members.sortedWith(volumeOrder)
            ComicSeries(key, ordered.first().series.trim(), ordered)
        }.sortedWith { a, b -> NaturalOrder.compare(a.title, b.title) }
}
