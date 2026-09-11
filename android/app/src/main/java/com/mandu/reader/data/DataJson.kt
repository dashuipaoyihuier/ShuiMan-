package com.mandu.reader.data

import com.mandu.reader.core.BookKind
import com.mandu.reader.core.BookRecord
import com.mandu.reader.core.PageOverride
import com.mandu.reader.core.PageTurnMode
import com.mandu.reader.core.ReaderPreferences
import com.mandu.reader.core.ReadingDirection
import com.mandu.reader.core.ReadingLayout
import com.mandu.reader.core.ReadingState
import com.mandu.reader.core.ScaleMode
import com.mandu.reader.core.SourceLocator
import org.json.JSONArray
import org.json.JSONObject

internal object DataJson {
    fun book(value: BookRecord): String = JSONObject().apply {
        put("id", value.id)
        put("uri", value.uri)
        put("title", value.title)
        put("kind", value.kind.name)
        put("series", value.series)
        value.volume?.let { put("volume", it) }
        put("tags", JSONArray(value.tags))
        put("favorite", value.favorite)
        put("readStatus", value.readStatus)
        put("lastOpened", value.lastOpened)
        put("pageCount", value.pageCount)
        put("progress", value.progress)
        value.coverPath?.let { put("coverPath", it) }
        put("missing", value.missing)
    }.toString()

    fun parseBook(raw: String): BookRecord = JSONObject(raw).let { json ->
        BookRecord(
            id = json.getString("id"),
            uri = json.getString("uri"),
            title = json.optString("title"),
            kind = enumOr(json.optString("kind"), BookKind.IMAGES),
            series = json.optString("series"),
            volume = if (json.has("volume") && !json.isNull("volume")) json.optDouble("volume") else null,
            tags = json.optJSONArray("tags").strings(),
            favorite = json.optBoolean("favorite"),
            readStatus = json.optString("readStatus", "未读"),
            lastOpened = json.optLong("lastOpened"),
            pageCount = json.optInt("pageCount"),
            progress = json.optInt("progress"),
            coverPath = json.nullableString("coverPath"),
            missing = json.optBoolean("missing")
        )
    }

    fun state(value: ReadingState): String = JSONObject().apply {
        value.locator?.let { put("locator", locator(it)) }
        put("preferences", preferences(value.preferences))
        put("overrides", JSONObject().also { result ->
            value.overrides.forEach { (key, item) -> result.put(key, override(item)) }
        })
        put("bookmarks", JSONArray().also { array -> value.bookmarks.forEach { array.put(locator(it)) } })
        put("scrollOffset", value.scrollOffset)
        put("scrollFraction", normalizedScrollFraction(value.scrollFraction))
    }.toString()

    fun parseState(raw: String): ReadingState = JSONObject(raw).let { json ->
        val overrides = linkedMapOf<String, PageOverride>()
        json.optJSONObject("overrides")?.let { values ->
            for (key in values.keys()) values.optJSONObject(key)?.let { overrides[key] = parseOverride(it) }
        }
        val bookmarks = buildList {
            val values = json.optJSONArray("bookmarks") ?: JSONArray()
            for (index in 0 until values.length()) values.optJSONObject(index)?.let { add(parseLocator(it)) }
        }
        ReadingState(
            locator = json.optJSONObject("locator")?.let(::parseLocator),
            preferences = json.optJSONObject("preferences")?.let(::parsePreferences) ?: ReaderPreferences(),
            overrides = overrides,
            bookmarks = bookmarks,
            scrollOffset = json.optInt("scrollOffset"),
            scrollFraction = normalizedScrollFraction(json.optDouble("scrollFraction", 0.0))
        )
    }

    private fun normalizedScrollFraction(value: Double): Double =
        if (value.isFinite()) value.coerceIn(0.0, 1.0) else 0.0

    private fun locator(value: SourceLocator) = JSONObject().apply {
        put("resource", value.resource)
        put("occurrence", value.occurrence)
        put("imageIndex", value.imageIndex)
    }

    private fun parseLocator(json: JSONObject) = SourceLocator(
        resource = json.optString("resource"),
        occurrence = json.optInt("occurrence"),
        imageIndex = json.optInt("imageIndex")
    )

    private fun preferences(value: ReaderPreferences) = JSONObject().apply {
        put("direction", value.direction.name)
        put("layout", value.layout.name)
        put("pageTurn", value.pageTurn.name)
        put("scaleMode", value.scaleMode.name)
        put("coverAlone", value.coverAlone)
        put("smartSpreads", value.smartSpreads)
        put("automaticPairs", value.automaticPairs)
        put("aggressivePairs", value.aggressivePairs)
        put("automaticOrientation", value.automaticOrientation)
        put("autoHideControls", value.autoHideControls)
    }

    private fun parsePreferences(json: JSONObject): ReaderPreferences {
        val layout = enumOr(json.optString("layout"), ReadingLayout.AUTO)
        // The continuous layout predates the explicit page-turn mode; keep saved books scrollable.
        val pageTurn = json.optString("pageTurn").takeIf(String::isNotEmpty)?.let {
            enumOr(it, PageTurnMode.TAP)
        } ?: if (layout == ReadingLayout.SCROLL) PageTurnMode.SCROLL else PageTurnMode.TAP
        return ReaderPreferences(
            direction = enumOr(json.optString("direction"), ReadingDirection.RTL),
            layout = layout,
            pageTurn = pageTurn,
            scaleMode = enumOr(json.optString("scaleMode"), ScaleMode.FIT),
            coverAlone = json.optBoolean("coverAlone", true),
            smartSpreads = json.optBoolean("smartSpreads", true),
            automaticPairs = json.optBoolean("automaticPairs", true),
            aggressivePairs = json.optBoolean("aggressivePairs", true),
            automaticOrientation = json.optBoolean("automaticOrientation", true),
            autoHideControls = json.optBoolean("autoHideControls", true)
        )
    }

    private fun override(value: PageOverride) = JSONObject().apply {
        value.rotation?.let { put("rotation", it) }
        value.standalone?.let { put("standalone", it) }
        value.joinNext?.let { put("joinNext", it) }
        value.earlierOnRight?.let { put("earlierOnRight", it) }
        put("pairOffset", value.pairOffset)
        put("pairScale", value.pairScale)
        put("pairingBreak", value.pairingBreak)
    }

    private fun parseOverride(json: JSONObject) = PageOverride(
        rotation = json.nullableInt("rotation"),
        standalone = json.nullableBoolean("standalone"),
        joinNext = json.nullableBoolean("joinNext"),
        earlierOnRight = json.nullableBoolean("earlierOnRight"),
        pairOffset = json.optDouble("pairOffset", 0.0),
        pairScale = json.optDouble("pairScale", 1.0),
        pairingBreak = json.optBoolean("pairingBreak")
    )

    private inline fun <reified T : Enum<T>> enumOr(raw: String, fallback: T): T =
        enumValues<T>().firstOrNull { it.name == raw } ?: fallback

    private fun JSONArray?.strings(): List<String> {
        if (this == null) return emptyList()
        return buildList { for (index in 0 until length()) optString(index).takeIf(String::isNotEmpty)?.let(::add) }
    }

    private fun JSONObject.nullableString(key: String): String? =
        if (has(key) && !isNull(key)) optString(key).takeIf(String::isNotEmpty) else null

    private fun JSONObject.nullableInt(key: String): Int? =
        if (has(key) && !isNull(key)) optInt(key) else null

    private fun JSONObject.nullableBoolean(key: String): Boolean? =
        if (has(key) && !isNull(key)) optBoolean(key) else null
}
