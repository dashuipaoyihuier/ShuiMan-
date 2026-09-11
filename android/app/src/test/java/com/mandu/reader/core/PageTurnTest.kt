package com.mandu.reader.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * The manga reading order must be decided without a device: the next page always sits on the
 * side the swipe or tap brings in, and RTL must mirror LTR exactly.
 */
class PageTurnTest {
    private val width = 1000f

    @Test fun ltrTapTurnsFromThePhysicalSides() {
        assertEquals(TapAction.PREVIOUS, PageTurn.tapAction(10f, width, ReadingDirection.LTR))
        assertEquals(TapAction.PREVIOUS, PageTurn.tapAction(349f, width, ReadingDirection.LTR))
        assertEquals(TapAction.TOGGLE_CONTROLS, PageTurn.tapAction(350f, width, ReadingDirection.LTR))
        assertEquals(TapAction.TOGGLE_CONTROLS, PageTurn.tapAction(500f, width, ReadingDirection.LTR))
        assertEquals(TapAction.TOGGLE_CONTROLS, PageTurn.tapAction(650f, width, ReadingDirection.LTR))
        assertEquals(TapAction.NEXT, PageTurn.tapAction(651f, width, ReadingDirection.LTR))
        assertEquals(TapAction.NEXT, PageTurn.tapAction(990f, width, ReadingDirection.LTR))
    }

    @Test fun rtlTapMirrorsLtrBecauseMangaReadsRightToLeft() {
        assertEquals(TapAction.NEXT, PageTurn.tapAction(10f, width, ReadingDirection.RTL))
        assertEquals(TapAction.NEXT, PageTurn.tapAction(349f, width, ReadingDirection.RTL))
        assertEquals(TapAction.TOGGLE_CONTROLS, PageTurn.tapAction(500f, width, ReadingDirection.RTL))
        assertEquals(TapAction.PREVIOUS, PageTurn.tapAction(651f, width, ReadingDirection.RTL))
        assertEquals(TapAction.PREVIOUS, PageTurn.tapAction(990f, width, ReadingDirection.RTL))
    }

    @Test fun ltrSwipeLeftAdvancesAndSwipeRightGoesBack() {
        assertEquals(TapAction.NEXT, PageTurn.swipeAction(-400f, width, ReadingDirection.LTR))
        assertEquals(TapAction.PREVIOUS, PageTurn.swipeAction(400f, width, ReadingDirection.LTR))
    }

    @Test fun rtlSwipeRightAdvancesAndSwipeLeftGoesBack() {
        assertEquals(TapAction.NEXT, PageTurn.swipeAction(400f, width, ReadingDirection.RTL))
        assertEquals(TapAction.PREVIOUS, PageTurn.swipeAction(-400f, width, ReadingDirection.RTL))
    }

    @Test fun shortDragsDoNotTurnThePage() {
        assertNull(PageTurn.swipeAction(0f, width, ReadingDirection.RTL))
        assertNull(PageTurn.swipeAction(99f, width, ReadingDirection.RTL))
        assertNull(PageTurn.swipeAction(-99f, width, ReadingDirection.LTR))
        assertEquals(TapAction.NEXT, PageTurn.swipeAction(100f, width, ReadingDirection.RTL))
    }

    @Test fun degenerateWidthNeverTurnsThePage() {
        assertEquals(TapAction.TOGGLE_CONTROLS, PageTurn.tapAction(5f, 0f, ReadingDirection.RTL))
        assertNull(PageTurn.swipeAction(-500f, 0f, ReadingDirection.LTR))
    }

    @Test fun continuousModeAbsorbsTheLegacyScrollLayout() {
        val legacy = ReaderPreferences(layout = ReadingLayout.SCROLL)
        assertEquals(true, legacy.scrollsVertically)
        assertEquals(false, ReaderPreferences(layout = ReadingLayout.SINGLE).scrollsVertically)
        assertEquals(
            true,
            ReaderPreferences(layout = ReadingLayout.SINGLE, pageTurn = PageTurnMode.SCROLL).scrollsVertically
        )
    }

    @Test fun defaultPreferencesFollowMangaReadingOrder() {
        val defaults = ReaderPreferences()
        assertEquals(ReadingDirection.RTL, defaults.direction)
        assertEquals(PageTurnMode.TAP, defaults.pageTurn)
        assertEquals(true, defaults.autoHideControls)
    }
}
