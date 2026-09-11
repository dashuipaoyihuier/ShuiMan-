package com.mandu.reader.core

import org.junit.Assert.*
import org.junit.Test

class VolumeDisplayTest {
    @Test fun phonesOfferFourThreeAndTwoColumns() {
        assertEquals(listOf(4, 3, 2), VolumeCoverSize.entries.map { it.columns(375f) })
    }

    @Test fun narrowAndWideWindowsRemainValidAndSizesAreOrdered() {
        for (width in listOf(0f, 100f, 284f, 375f, 720f, 1156f)) {
            val columns = VolumeCoverSize.entries.map { it.columns(width) }
            assertTrue(columns.all { it >= 1 })
            assertTrue(columns.zipWithNext().all { (small, large) -> small >= large })
        }
        assertEquals(1, VolumeCoverSize.SMALL.columns(Float.NaN))
        assertTrue(VolumeCoverSize.SMALL.columns(1156f) > VolumeCoverSize.SMALL.columns(375f))
    }

    @Test fun settingsRoundTripAndUnknownValuesUseSafeDefaults() {
        assertEquals(VolumeDisplayPreferences(), VolumeDisplayPreferences.decode(null, "future-size"))
        for (mode in VolumeViewMode.entries) for (size in VolumeCoverSize.entries) {
            assertEquals(VolumeDisplayPreferences(mode, size), VolumeDisplayPreferences.decode(mode.name, size.name))
        }
    }
}
