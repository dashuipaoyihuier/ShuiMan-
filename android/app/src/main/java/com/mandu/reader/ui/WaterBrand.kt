package com.mandu.reader.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.mandu.reader.R

/** Exact macOS WaterCover artwork, with native accessible text above it. */
@Composable
internal fun WaterCover(modifier: Modifier = Modifier) {
    BoxWithConstraints(modifier.fillMaxWidth().height(170.dp).clip(RoundedCornerShape(18.dp))
        .background(Color(0xFF102D3A))) {
        Image(painterResource(R.drawable.water_cover), contentDescription = null,
            modifier = Modifier.align(Alignment.CenterEnd).width((maxWidth * .65f).coerceAtMost(560.dp)).height(170.dp),
            contentScale = ContentScale.Crop, alignment = Alignment.BottomCenter)
        Box(Modifier.fillMaxSize().background(Brush.horizontalGradient(
            listOf(Color(0xF20A212E), Color.Transparent))))
        Column(Modifier.align(Alignment.CenterStart).padding(24.dp)) {
            Text(stringResource(R.string.app_name), color = Color.White,
                style = MaterialTheme.typography.headlineLarge, fontWeight = FontWeight.SemiBold)
            Text(stringResource(R.string.brand_tagline), Modifier.padding(top = 10.dp),
                color = Color.White, style = MaterialTheme.typography.bodyMedium)
        }
    }
}
