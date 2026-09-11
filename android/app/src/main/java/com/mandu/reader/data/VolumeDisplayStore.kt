package com.mandu.reader.data

import android.content.Context
import com.mandu.reader.core.VolumeDisplayPreferences

/** App-wide presentation only; never alters any book or reading preferences. */
class VolumeDisplayStore(context: Context) {
    private val context = context.applicationContext
    private val preferences get() = context.getSharedPreferences("volume_display", Context.MODE_PRIVATE)

    fun load(): VolumeDisplayPreferences = preferences.let {
        VolumeDisplayPreferences.decode(it.getString("mode", null), it.getString("cover_size", null))
    }

    fun save(value: VolumeDisplayPreferences) {
        preferences.edit().putString("mode", value.mode.name).putString("cover_size", value.coverSize.name).apply()
    }
}
