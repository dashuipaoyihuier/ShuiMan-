package com.mandu.reader.core

object LayoutPlanner {
    fun rotation(unit: ReadingUnit, state: ReadingState, decisions: Map<String, SpreadDecision>): Int =
        state.overrides[unit.id]?.rotation ?: if (state.preferences.automaticOrientation && !unit.isCover)
            decisions[unit.id]?.rotation ?: unit.rotationHint ?: 0 else 0

    fun groups(publication: Publication, state: ReadingState, wideScreen: Boolean,
               decisions: Map<String, SpreadDecision> = emptyMap(), pairs: Map<Int, PairDecision> = emptyMap()): List<DisplayGroup> {
        val units = publication.units
        val prefs = state.preferences
        val double = prefs.layout == ReadingLayout.DOUBLE || prefs.layout == ReadingLayout.AUTO && wideScreen
        fun alone(i: Int): Boolean {
            val u = units[i]
            if (u.complex || u.error != null) return true
            state.overrides[u.id]?.standalone?.let { return it }
            if (prefs.coverAlone && u.isCover) return true
            val angle = Math.floorMod(rotation(u, state, decisions), 360)
            val w = if (angle % 180 == 90) u.height else u.width
            val h = if (angle % 180 == 90) u.width else u.height
            return prefs.smartSpreads && (decisions[u.id]?.standalone == true || h > 0 && w.toDouble() / h >= 1.2) || angle != 0
        }
        val automatic = mutableMapOf<Int, PairDecision>()
        if (prefs.smartSpreads && prefs.automaticPairs) pairs.forEach { (i, p) ->
            if (i !in 0 until units.lastIndex || !(p.automatic || prefs.aggressivePairs && p.suggested)) return@forEach
            val a = units[i]; val b = units[i + 1]
            if (alone(i) || alone(i + 1) || a.isCover || b.isCover || state.overrides[a.id]?.joinNext != null ||
                state.overrides[b.id]?.joinNext == true || state.overrides[b.id]?.pairingBreak == true ||
                state.overrides[a.id]?.rotation != null || state.overrides[b.id]?.rotation != null ||
                decisions[a.id]?.uncertain != false || decisions[b.id]?.uncertain != false ||
                rotation(a, state, decisions) != 0 || rotation(b, state, decisions) != 0) return@forEach
            val rival = maxOf(pairs[i - 1]?.score ?: 0.0, pairs[i + 1]?.score ?: 0.0)
            if (p.score - rival >= if (prefs.aggressivePairs) 0.04 else 0.08) automatic[i] = p
        }
        val result = mutableListOf<DisplayGroup>()
        var i = 0
        while (i < units.size) {
            val correction = state.overrides[units[i].id]
            if (correction?.joinNext == true && i < units.lastIndex && !units[i].complex && !units[i + 1].complex) {
                // Physical placement is captured at confirmation, independent of subsequent navigation preferences.
                result += DisplayGroup(if (correction.earlierOnRight == true) listOf(i + 1, i) else listOf(i, i + 1), true, correction.pairOffset, correction.pairScale)
                i += 2
            } else if (automatic[i] != null) {
                val pair = automatic.getValue(i)
                result += DisplayGroup(if (pair.swapped) listOf(i + 1, i) else listOf(i, i + 1), true, pair.verticalOffset, pair.rightScale)
                i += 2
            } else if (alone(i) || !double || i == units.lastIndex || alone(i + 1) ||
                state.overrides[units[i + 1].id]?.joinNext == true || state.overrides[units[i + 1].id]?.pairingBreak == true || automatic[i + 1] != null) {
                result += DisplayGroup(listOf(i), alone(i)); i++
            } else {
                result += DisplayGroup(if (prefs.direction == ReadingDirection.RTL) listOf(i + 1, i) else listOf(i, i + 1)); i += 2
            }
        }
        return result
    }
}
