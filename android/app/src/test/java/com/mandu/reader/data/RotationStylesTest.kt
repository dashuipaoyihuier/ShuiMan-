package com.mandu.reader.data

import org.jsoup.Jsoup
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class RotationStylesTest {
    @Test
    fun specificityImportantAndInlineCascadeAreAppliedSelectively() {
        val image = Jsoup.parse("<img id='page' class='turn' style='transform: rotate(270deg)'>").selectFirst("img")!!
        assertEquals(
            90,
            RotationStyles.hint(
                ".turn { transform: rotate(180deg) } #page { transform: rotate(90deg) !important }",
                image
            )
        )
    }

    @Test
    fun contradictoryPossibleScreenMediaDoesNotForceRotation() {
        val image = Jsoup.parse("<img class='turn'>").selectFirst("img")!!
        val css = """
            @media screen and (min-width: 600px) { .turn { transform: rotate(90deg); } }
            @media screen and (max-width: 599px) { .turn { transform: rotate(270deg); } }
        """.trimIndent()
        assertNull(RotationStyles.hint(css, image))
    }

    @Test
    fun unsupportedWinningTransformFallsBackToWebLayout() {
        val image = Jsoup.parse("<img class='turn'>").selectFirst("img")!!
        assertNull(RotationStyles.hint(
            ".turn { transform: rotate(90deg); transform: translateX(2px) !important; }",
            image
        ))
    }
}
