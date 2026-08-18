package dev.hatch.android

import androidx.compose.material3.Typography
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

// The M3 type scale with one systematic change: headlines and titles are SemiBold rather than
// the scale's Regular/Medium. Hatch's screens are dense lists where the only hierarchy is
// weight, and a Regular headline over Regular body reads as one undifferentiated block.
//
// This exists because the weight was previously set at each call site — the app bar title,
// the drawer wordmark, the sheet header, the sync headings — which is how two of them ended up
// disagreeing (one Bold, the rest SemiBold). Sizes, line heights and letter spacing are
// untouched defaults, so this stays the Material scale rather than becoming a private one.
private val Default = Typography()

// The second systematic change is optical tracking: the Material scale ships display and
// headline sizes at 0sp letter spacing, which is set for Regular. At SemiBold and 24sp+ the
// same tracking reads loose and slightly cheap, so the large sizes are pulled in. Body and
// label sizes are untouched — tightening those costs legibility at a glance, which is the
// whole job of a task row.
internal val HatchTypography = Typography(
    displayLarge = Default.displayLarge.copy(fontWeight = FontWeight.SemiBold, letterSpacing = (-1).sp),
    displayMedium = Default.displayMedium.copy(fontWeight = FontWeight.SemiBold, letterSpacing = (-0.75).sp),
    displaySmall = Default.displaySmall.copy(fontWeight = FontWeight.SemiBold, letterSpacing = (-0.5).sp),
    headlineLarge = Default.headlineLarge.copy(fontWeight = FontWeight.SemiBold, letterSpacing = (-0.5).sp),
    headlineMedium = Default.headlineMedium.copy(fontWeight = FontWeight.SemiBold, letterSpacing = (-0.4).sp),
    // MediumTopAppBar's title style, so this one carries every screen's name.
    headlineSmall = Default.headlineSmall.copy(fontWeight = FontWeight.SemiBold, letterSpacing = (-0.3).sp),
    // TopAppBar's title style.
    titleLarge = Default.titleLarge.copy(fontWeight = FontWeight.SemiBold, letterSpacing = (-0.2).sp),
    titleMedium = Default.titleMedium.copy(fontWeight = FontWeight.SemiBold),
    titleSmall = Default.titleSmall.copy(fontWeight = FontWeight.SemiBold),
)
