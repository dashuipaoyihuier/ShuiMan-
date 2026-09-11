package com.mandu.reader.data

import androidx.test.ext.junit.runners.AndroidJUnit4
import com.mandu.reader.core.PageTurnMode
import com.mandu.reader.core.ReaderPreferences
import com.mandu.reader.core.ReadingDirection
import com.mandu.reader.core.ReadingLayout
import com.mandu.reader.core.ReadingState
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

/** Saved books must survive the page-turn setting being introduced. */
@RunWith(AndroidJUnit4::class)
class ReadingPreferencesPersistenceDeviceTest {
    @Test fun pageTurnAndAutoHideRoundTrip() {
        val original = ReaderPreferences(
            direction = ReadingDirection.LTR,
            layout = ReadingLayout.DOUBLE,
            pageTurn = PageTurnMode.SWIPE,
            autoHideControls = false
        )
        val restored = DataJson.parseState(DataJson.state(ReadingState(preferences = original))).preferences
        assertEquals(PageTurnMode.SWIPE, restored.pageTurn)
        assertEquals(ReadingDirection.LTR, restored.direction)
        assertEquals(ReadingLayout.DOUBLE, restored.layout)
        assertEquals(false, restored.autoHideControls)
    }

    @Test fun legacyScrollLayoutBecomesTheVerticalPageTurnMode() {
        // A book saved before pageTurn existed carries only the continuous layout.
        val legacy = """{"preferences":{"layout":"SCROLL"}}"""
        val restored = DataJson.parseState(legacy).preferences
        assertEquals(PageTurnMode.SCROLL, restored.pageTurn)
        assertEquals(true, restored.scrollsVertically)
    }

    @Test fun stateWithoutPageTurnDefaultsToTapAndMangaOrder() {
        val restored = DataJson.parseState("""{"preferences":{}}""").preferences
        assertEquals(PageTurnMode.TAP, restored.pageTurn)
        assertEquals(ReadingDirection.RTL, restored.direction)
        assertEquals(true, restored.autoHideControls)
        assertEquals(false, restored.scrollsVertically)
    }

    @Test fun legacyScrollOffsetSurvivesWithoutFraction() {
        val restored = DataJson.parseState("""{"scrollOffset":17}""")
        assertEquals(17, restored.scrollOffset)
        assertEquals(0.0, restored.scrollFraction, 0.0)
    }

    @Test fun scrollFractionAndLegacyOffsetRoundTrip() {
        val restored = DataJson.parseState(DataJson.state(ReadingState(
            scrollOffset = 9,
            scrollFraction = 0.625
        )))
        assertEquals(9, restored.scrollOffset)
        assertEquals(0.625, restored.scrollFraction, 0.0)
    }

    @Test fun unsafeScrollFractionsAreNormalized() {
        assertEquals(0.0, DataJson.parseState("""{"scrollFraction":"NaN"}""").scrollFraction, 0.0)
        assertEquals(0.0, DataJson.parseState("""{"scrollFraction":-0.25}""").scrollFraction, 0.0)
        assertEquals(1.0, DataJson.parseState("""{"scrollFraction":1.25}""").scrollFraction, 0.0)
        val serializedNaN = DataJson.parseState(DataJson.state(ReadingState(scrollFraction = Double.NaN)))
        assertEquals(0.0, serializedNaN.scrollFraction, 0.0)
    }
}
