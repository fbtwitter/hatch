package dev.hatch.android

import androidx.compose.material3.Typography
import androidx.compose.ui.text.font.FontWeight

// The M3 type scale with one systematic change: headlines and titles are SemiBold rather than
// the scale's Regular/Medium. Hatch's screens are dense lists where the only hierarchy is
// weight, and a Regular headline over Regular body reads as one undifferentiated block.
//
// This exists because the weight was previously set at each call site — the app bar title,
// the drawer wordmark, the sheet header, the sync headings — which is how two of them ended up
// disagreeing (one Bold, the rest SemiBold). Sizes, line heights and letter spacing are
// untouched defaults, so this stays the Material scale rather than becoming a private one.
private val Default = Typography()

internal val HatchTypography = Typography(
    displayLarge = Default.displayLarge.copy(fontWeight = FontWeight.SemiBold),
    displayMedium = Default.displayMedium.copy(fontWeight = FontWeight.SemiBold),
    displaySmall = Default.displaySmall.copy(fontWeight = FontWeight.SemiBold),
    headlineLarge = Default.headlineLarge.copy(fontWeight = FontWeight.SemiBold),
    headlineMedium = Default.headlineMedium.copy(fontWeight = FontWeight.SemiBold),
    // MediumTopAppBar's title style, so this one carries every screen's name.
    headlineSmall = Default.headlineSmall.copy(fontWeight = FontWeight.SemiBold),
    // TopAppBar's title style.
    titleLarge = Default.titleLarge.copy(fontWeight = FontWeight.SemiBold),
    titleMedium = Default.titleMedium.copy(fontWeight = FontWeight.SemiBold),
    titleSmall = Default.titleSmall.copy(fontWeight = FontWeight.SemiBold),
)
