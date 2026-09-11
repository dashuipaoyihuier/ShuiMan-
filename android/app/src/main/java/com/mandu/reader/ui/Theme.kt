package com.mandu.reader.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// Shared brand anchors from macOS WaterTheme.swift. Wallpaper-derived dynamic
// colors are intentionally disabled so both apps retain the Water identity.
internal val WaterLightColors = lightColorScheme(
    primary = Color(0xFF0F6E82), onPrimary = Color.White,
    primaryContainer = Color(0xFFC5EBEB), onPrimaryContainer = Color(0xFF073944),
    secondary = Color(0xFF45666A), onSecondary = Color.White,
    secondaryContainer = Color(0xFFD7EAE7), onSecondaryContainer = Color(0xFF203C40),
    tertiary = Color(0xFF526C5A), onTertiary = Color.White,
    tertiaryContainer = Color(0xFFD6EBDD), onTertiaryContainer = Color(0xFF263E30),
    background = Color(0xFFF0F7F5), onBackground = Color(0xFF152F39),
    surface = Color(0xFFF0F7F5), onSurface = Color(0xFF152F39),
    surfaceVariant = Color(0xFFDEEAE8), onSurfaceVariant = Color(0xFF496166),
    surfaceDim = Color(0xFFD4E2DF), surfaceBright = Color(0xFFF7FBF9),
    surfaceContainerLowest = Color.White, surfaceContainerLow = Color(0xFFF3F8F6),
    surfaceContainer = Color(0xFFE8F1EE), surfaceContainerHigh = Color(0xFFE1ECE9),
    surfaceContainerHighest = Color(0xFFD9E6E3),
    outline = Color(0xFF718784), outlineVariant = Color(0xFFBDCECA),
    inverseSurface = Color(0xFF263C43), inverseOnSurface = Color(0xFFE8F2EF),
    inversePrimary = Color(0xFF61CCD4), surfaceTint = Color(0xFF0F6E82)
)

internal val WaterDarkColors = darkColorScheme(
    primary = Color(0xFF61CCD4), onPrimary = Color(0xFF00363E),
    primaryContainer = Color(0xFF174D58), onPrimaryContainer = Color(0xFFC5EBEB),
    secondary = Color(0xFFB0D0CE), onSecondary = Color(0xFF173638),
    secondaryContainer = Color(0xFF314D50), onSecondaryContainer = Color(0xFFD7EAE7),
    tertiary = Color(0xFFB8D3BD), onTertiary = Color(0xFF233B2C),
    tertiaryContainer = Color(0xFF3A5242), onTertiaryContainer = Color(0xFFD6EBDD),
    background = Color(0xFF121F26), onBackground = Color(0xFFE0ECE9),
    surface = Color(0xFF121F26), onSurface = Color(0xFFE0ECE9),
    surfaceVariant = Color(0xFF32474D), onSurfaceVariant = Color(0xFFBDCECA),
    surfaceDim = Color(0xFF0D1920), surfaceBright = Color(0xFF34474D),
    surfaceContainerLowest = Color(0xFF09151C), surfaceContainerLow = Color(0xFF17272E),
    surfaceContainer = Color(0xFF1C2D34), surfaceContainerHigh = Color(0xFF26383F),
    surfaceContainerHighest = Color(0xFF30434A),
    outline = Color(0xFF899E9C), outlineVariant = Color(0xFF40575B),
    inverseSurface = Color(0xFFE0ECE9), inverseOnSurface = Color(0xFF263C43),
    inversePrimary = Color(0xFF0F6E82), surfaceTint = Color(0xFF61CCD4)
)

@Composable
fun ManduTheme(content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = if (isSystemInDarkTheme()) WaterDarkColors else WaterLightColors, content = content)
}
