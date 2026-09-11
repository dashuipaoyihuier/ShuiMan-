package com.mandu.reader.core

import kotlin.math.roundToInt

enum class VolumeViewMode(val label: String) { GRID("网格"), LIST("列表") }

enum class VolumeCoverSize(val label: String, val targetWidth: Int, val listWidth: Int) {
    SMALL("小", 96, 40), MEDIUM("中", 136, 52), LARGE("大", 192, 64);

    fun columns(availableWidth: Float): Int =
        if (!availableWidth.isFinite()) 1
        else ((availableWidth.coerceAtLeast(0f) + 10f) / (targetWidth + 10f)).roundToInt().coerceAtLeast(1)
}

data class VolumeDisplayPreferences(
    val mode: VolumeViewMode = VolumeViewMode.GRID,
    val coverSize: VolumeCoverSize = VolumeCoverSize.SMALL
) {
    companion object {
        fun decode(mode: String?, size: String?) = VolumeDisplayPreferences(
            VolumeViewMode.entries.firstOrNull { it.name == mode } ?: VolumeViewMode.GRID,
            VolumeCoverSize.entries.firstOrNull { it.name == size } ?: VolumeCoverSize.SMALL
        )
    }
}
