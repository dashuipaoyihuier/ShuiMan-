package com.mandu.reader.core

import org.junit.Assert.*
import org.junit.Test

class LayoutPlannerTest {
    private fun publication(count: Int = 7) = Publication("test", "test", BookKind.EPUB,
        (0 until count).map { ReadingUnit(SourceLocator("repeated.xhtml", it), "Page $it", width = 600, height = 900, isCover = it == 0) })
    @Test fun duplicateSpineReferencesRemainDistinct() { assertEquals(7, publication().units.map { it.id }.toSet().size) }
    @Test fun phoneAndTabletPreserveEverySourceExactlyOnce() {
        val p = publication()
        for (direction in ReadingDirection.entries) for (layout in ReadingLayout.entries) for (wide in listOf(false,true)) {
            val state = ReadingState(preferences = ReaderPreferences(direction = direction, layout = layout))
            assertEquals((0..6).toList(), LayoutPlanner.groups(p,state,wide).flatMap { it.indices }.sorted())
        }
        assertEquals(7, LayoutPlanner.groups(p,ReadingState(),false).size)
        assertEquals(4, LayoutPlanner.groups(p,ReadingState(),true).size)
    }
    @Test fun confirmedSpreadRetainsPhysicalPlacementAfterDirectionChange() {
        val p = publication()
        val state = ReadingState(overrides = mapOf(p.units[1].id to PageOverride(joinNext=true,earlierOnRight=true)))
        for (direction in ReadingDirection.entries) {
            val groups = LayoutPlanner.groups(p,state.copy(preferences=state.preferences.copy(direction=direction)),true)
            assertEquals(listOf(2,1),groups.first { 1 in it.indices }.indices)
            assertEquals((0..6).toList(), groups.flatMap { it.indices }.sorted())
        }
    }
    @Test fun missingAndWidePagesKeepTheirSlotAndDoNotConsumeNeighbors() {
        val p = publication().let { it.copy(units=it.units.mapIndexed { i,u -> when(i){2->u.copy(error="missing");4->u.copy(width=1800);else->u} }) }
        val groups = LayoutPlanner.groups(p,ReadingState(),true)
        assertTrue(groups.any { it.indices==listOf(2) }); assertTrue(groups.any { it.indices==listOf(4) })
        assertEquals((0..6).toList(),groups.flatMap { it.indices }.sorted())
    }
    @Test fun manualVetoWinsAndConflictingCandidatesCannotDoubleConsume() {
        val p=publication(); val d=p.units.associate { it.id to SpreadDecision() }
        val pairs=mapOf(1 to PairDecision(.95,false,true),2 to PairDecision(.95,true,true),4 to PairDecision(.92,true,true))
        val s=ReadingState(overrides=mapOf(p.units[4].id to PageOverride(joinNext=false)))
        val g=LayoutPlanner.groups(p,s,false,d,pairs)
        assertEquals(7,g.size)
        assertEquals((0..6).toList(),g.flatMap { it.indices }.sorted())
    }
    @Test fun manualRotationWinsOverBackgroundAnalysis() {
        val u=publication().units[2]
        val s=ReadingState(overrides=mapOf(u.id to PageOverride(rotation=270)))
        assertEquals(270,LayoutPlanner.rotation(u,s,mapOf(u.id to SpreadDecision(rotation=90))))
    }
    @Test fun mixedCorrectionsNeverDuplicateOrDropSourcePositions() {
        val random = kotlin.random.Random(417)
        repeat(1_000) {
            val p = publication(random.nextInt(1, 40)).let { base -> base.copy(units = base.units.map { unit ->
                when (random.nextInt(9)) {
                    0 -> unit.copy(error = "Unreadable")
                    1 -> unit.copy(complex = true)
                    2 -> unit.copy(width = 1800)
                    else -> unit
                }
            }) }
            val overrides = p.units.mapNotNull { unit ->
                val correction = when (random.nextInt(7)) {
                    0 -> PageOverride(joinNext = true, earlierOnRight = random.nextBoolean())
                    1 -> PageOverride(joinNext = false, pairingBreak = true)
                    2 -> PageOverride(rotation = 90)
                    3 -> PageOverride(standalone = true)
                    else -> null
                }
                correction?.let { unit.id to it }
            }.toMap()
            val state = ReadingState(preferences = ReaderPreferences(
                direction = ReadingDirection.entries.random(random),
                layout = ReadingLayout.entries.random(random)
            ), overrides = overrides)
            val decisions = p.units.associate { it.id to SpreadDecision() }
            val pairs = (0 until p.units.lastIndex).associateWith {
                PairDecision(random.nextDouble(), random.nextBoolean(), true)
            }
            val groups = LayoutPlanner.groups(p, state, random.nextBoolean(), decisions, pairs)
            assertEquals(p.units.indices.toList(), groups.flatMap { it.indices }.sorted())
            assertTrue(groups.all { it.indices.size in 1..2 })
            assertTrue(groups.all { it.indices.size == 1 || kotlin.math.abs(it.indices[0] - it.indices[1]) == 1 })
        }
    }
}
