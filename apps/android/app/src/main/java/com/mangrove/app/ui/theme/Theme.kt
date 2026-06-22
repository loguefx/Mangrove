package com.mangrove.app.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val Teal = Color(0xFF14A88C)
val TealMint = Color(0xFF7FE9D2)
val TealDeep = Color(0xFF0B3B36)
val BgDark = Color(0xFF0A0A0A)
val SurfaceDark = Color(0xFF161616)

private val MangroveColors = darkColorScheme(
    primary = Teal,
    onPrimary = Color.White,
    secondary = TealMint,
    onSecondary = Color(0xFF06231F),
    background = BgDark,
    onBackground = Color(0xFFF2F2F2),
    surface = SurfaceDark,
    onSurface = Color(0xFFEDEDED),
    surfaceVariant = Color(0xFF222222),
    onSurfaceVariant = Color(0xFFB5B5B5),
)

@Composable
fun MangroveTheme(content: @Composable () -> Unit) {
    // App is dark-first to match the web UI; we ignore the system light theme.
    @Suppress("UNUSED_EXPRESSION")
    isSystemInDarkTheme()
    MaterialTheme(
        colorScheme = MangroveColors,
        content = content,
    )
}
