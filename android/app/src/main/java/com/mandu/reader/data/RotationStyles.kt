package com.mandu.reader.data

import org.jsoup.nodes.Element

/** Conservative CSS cascade subset used only to surface a possible page rotation hint. */
internal object RotationStyles {
    private data class Rule(val selector: String, val body: String, val condition: String)
    private data class Value(
        val rotation: Int?, val important: Int, val inline: Int, val specificity: Int, val order: Int
    ) : Comparable<Value> {
        override fun compareTo(other: Value): Int {
            val mine = intArrayOf(important, inline, specificity, order)
            val theirs = intArrayOf(other.important, other.inline, other.specificity, other.order)
            for (index in mine.indices) if (mine[index] != theirs[index]) return mine[index].compareTo(theirs[index])
            return 0
        }
    }

    fun hint(css: String, image: Element): Int? {
        val clean = css.replace(Regex("/\\*[\\s\\S]*?\\*/"), "")
        if (Regex("@(supports|layer|container|import|scope)\\b", RegexOption.IGNORE_CASE).containsMatchIn(clean)) return null
        val rules = parse(clean)
        val conditions = linkedSetOf("").apply { addAll(rules.map { it.condition }) }
        val winners = mutableListOf<Int>()
        for (condition in conditions) {
            val values = mutableListOf<Value>()
            rules.forEachIndexed { order, rule ->
                if (!(rule.condition.isEmpty() || rule.condition == condition || condition.startsWith(rule.condition + "&&"))) return@forEachIndexed
                for (selector in rule.selector.split(',').map { it.trim() }) {
                    if (declaration(rule.body, 0, 0, 0) == null) continue
                    val specificity = specificity(selector) ?: return null
                    val matches = runCatching { image.`is`(selector) }.getOrElse { return null }
                    if (matches) declaration(rule.body, 0, specificity, order)?.let(values::add)
                }
            }
            declaration(image.attr("style"), 1, 0, rules.size)?.let(values::add)
            values.maxOrNull()?.let { winners += it.rotation ?: return null }
        }
        val nonzero = winners.filter { it != 0 }.toSet()
        return when {
            nonzero.size == 1 -> nonzero.first()
            nonzero.isEmpty() && winners.isNotEmpty() -> 0
            else -> null
        }
    }

    private fun declaration(body: String, inline: Int, specificity: Int, order: Int): Value? {
        var result: Value? = null
        for (entry in body.split(';')) {
            val parts = entry.split(':', limit = 2)
            if (parts.size != 2 || !parts[0].trim().equals("transform", ignoreCase = true)) continue
            val raw = parts[1].trim().lowercase()
            val important = if ("!important" in raw) 1 else 0
            val value = raw.replace("!important", "").trim()
            val rotation = when {
                value == "none" -> 0
                else -> Regex("^rotate\\(\\s*(-?[0-9]+)(?:\\.0+)?deg\\s*\\)$")
                    .matchEntire(value)?.groupValues?.get(1)?.toIntOrNull()
                    ?.takeIf { it % 90 == 0 }?.let { ((it % 360) + 360) % 360 }
            }
            val candidate = Value(rotation, important, inline, specificity, order)
            if (result == null || important >= result.important) result = candidate
        }
        return result
    }

    private fun specificity(selector: String): Int? {
        if (':' in selector || '|' in selector || '\\' in selector) return null
        fun count(regex: Regex) = regex.findAll(selector).count()
        return count(Regex("#[\\w-]+")) * 1_000_000 +
            count(Regex("\\.[\\w-]+|\\[[^]]+]")) * 1_000 +
            count(Regex("(?:^|[\\s>+~])(?:[a-zA-Z][\\w-]*)"))
    }

    private fun parse(css: String, condition: String = "", depth: Int = 0): List<Rule> {
        if (depth >= 12) return emptyList()
        var cursor = 0
        val result = mutableListOf<Rule>()
        while (cursor < css.length) {
            val brace = css.indexOf('{', cursor)
            if (brace < 0) break
            val selector = css.substring(cursor, brace).trim()
            cursor = brace + 1
            val bodyStart = cursor
            var braces = 1
            var quote: Char? = null
            while (cursor < css.length && braces > 0) {
                val character = css[cursor]
                if (quote != null) {
                    if (character == quote && (cursor == 0 || css[cursor - 1] != '\\')) quote = null
                } else when (character) {
                    '\'', '"' -> quote = character
                    '{' -> braces++
                    '}' -> braces--
                }
                cursor++
            }
            if (braces != 0) break
            val body = css.substring(bodyStart, cursor - 1)
            val lower = selector.lowercase()
            when {
                lower.startsWith("@media") && "print" !in lower && "not screen" !in lower ->
                    result += parse(body, "$condition&&$lower", depth + 1)
                !lower.startsWith('@') -> result += Rule(selector, body, condition)
            }
        }
        return result
    }
}
