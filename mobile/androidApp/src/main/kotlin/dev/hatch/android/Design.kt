package dev.hatch.android

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.DateRange
import androidx.compose.material.icons.rounded.Warning
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import java.time.LocalDate

// The app's shared visual vocabulary: the few decisions every screen has to agree on, in one
// place. Colour is expressed as theme roles rather than hex so the palette stays swappable —
// the same code has to work under Hatch's own scheme and under a wallpaper-derived one.

// Rows, tiles and grouped containers all speak the same corner language.
internal val CardCorner = 20.dp
internal val CardCornerInner = 8.dp
internal val ScreenPadding = 12.dp

// How far a task row's leading edge sits from the container edge, and how tall a row's
// tonal icon container is. Shared so the Lists rows and the task rows line up vertically.
internal val AvatarSize = 40.dp

// ── Due dates ────────────────────────────────────────────────────────────────────────────
//
// Transcription of windows/Converters/DueDateToChip{Brush,Glyph,Foreground}Converter.cs:
// overdue reads as an error, due-today as the accent, anything later as neutral, and a task
// a full week past its date escalates from the calendar glyph to a warning.

internal enum class DueTone { Overdue, Today, Upcoming }

internal data class DueChip(val label: String, val tone: DueTone, val severe: Boolean)

internal fun dueChipFor(iso: String?): DueChip? {
    val date = localDateOf(iso) ?: return null
    val today = LocalDate.now()
    val overdueBy = today.toEpochDay() - date.toEpochDay()

    val tone = when {
        overdueBy > 0 -> DueTone.Overdue
        overdueBy == 0L -> DueTone.Today
        else -> DueTone.Upcoming
    }
    return DueChip(dueDateLabel(iso) ?: return null, tone, severe = overdueBy >= 7)
}

@Composable
internal fun dueContainerColor(tone: DueTone): Color = when (tone) {
    DueTone.Overdue -> MaterialTheme.colorScheme.errorContainer
    DueTone.Today -> MaterialTheme.colorScheme.primaryContainer
    DueTone.Upcoming -> MaterialTheme.colorScheme.surfaceContainerHighest
}

@Composable
internal fun dueContentColor(tone: DueTone): Color = when (tone) {
    DueTone.Overdue -> MaterialTheme.colorScheme.onErrorContainer
    DueTone.Today -> MaterialTheme.colorScheme.onPrimaryContainer
    DueTone.Upcoming -> MaterialTheme.colorScheme.onSurfaceVariant
}

// The due date as a tonal pill rather than a run of grey text. On a list of twenty rows the
// one that is overdue should be findable without reading any of them.
@Composable
internal fun DueDateChip(chip: DueChip, modifier: Modifier = Modifier) {
    val content = dueContentColor(chip.tone)
    Surface(
        color = dueContainerColor(chip.tone),
        shape = RoundedCornerShape(6.dp),
        modifier = modifier,
    ) {
        Row(
            Modifier.padding(horizontal = 6.dp, vertical = 2.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            Icon(
                if (chip.severe) Icons.Rounded.Warning else Icons.Rounded.DateRange,
                contentDescription = null,
                tint = content,
                modifier = Modifier.size(12.dp),
            )
            Text(chip.label, style = MaterialTheme.typography.labelSmall, color = content)
        }
    }
}

// ── Priority ─────────────────────────────────────────────────────────────────────────────
//
// windows/Converters/PriorityToForegroundConverter.cs draws High red, Medium orange and Low
// blue. Mapped onto theme roles here: error, tertiary (the palette's gold) and primary.
// Carried by the checkbox rather than by another chip — the row already has enough pills,
// and colouring the control you are about to tap says the same thing for free.

@Composable
internal fun priorityColor(priority: Int): Color? = when (priority) {
    3 -> MaterialTheme.colorScheme.error
    2 -> MaterialTheme.colorScheme.tertiary
    1 -> MaterialTheme.colorScheme.primary
    else -> null
}

// The same three roles, one step lighter — for a selected priority segment in the detail
// sheet's picker, which needs a fillable container rather than a foreground tint.
@Composable
internal fun priorityContainerColor(priority: Int): Color? = when (priority) {
    3 -> MaterialTheme.colorScheme.errorContainer
    2 -> MaterialTheme.colorScheme.tertiaryContainer
    1 -> MaterialTheme.colorScheme.primaryContainer
    else -> null
}

@Composable
internal fun priorityOnContainerColor(priority: Int): Color? = when (priority) {
    3 -> MaterialTheme.colorScheme.onErrorContainer
    2 -> MaterialTheme.colorScheme.onTertiaryContainer
    1 -> MaterialTheme.colorScheme.onPrimaryContainer
    else -> null
}

// ── Tonal icon ───────────────────────────────────────────────────────────────────────────

// A circular tinted container holding an icon — the pattern Material uses for list leading
// content, and what makes a screen of rows read as a set of places rather than a table.
@Composable
internal fun TonalIcon(
    icon: ImageVector,
    container: Color,
    content: Color,
    modifier: Modifier = Modifier,
    size: Dp = AvatarSize,
) {
    Surface(color = container, shape = CircleShape, modifier = modifier.size(size)) {
        Box(contentAlignment = Alignment.Center) {
            Icon(
                icon,
                contentDescription = null,
                tint = content,
                modifier = Modifier.size(size * 0.5f),
            )
        }
    }
}

// The same container for a custom list, whose colour arrives as free text on the wire.
@Composable
internal fun ListAvatar(
    accentHex: String,
    customIcon: String?,
    modifier: Modifier = Modifier,
    size: Dp = AvatarSize,
) {
    val accent = parseAccent(accentHex)
    Surface(
        color = accent.copy(alpha = 0.16f),
        shape = CircleShape,
        modifier = modifier.size(size),
    ) {
        Box(contentAlignment = Alignment.Center) {
            if (customIcon.isNullOrBlank()) {
                Box(Modifier.size(size * 0.35f)) {
                    Surface(color = accent, shape = CircleShape) { Box(Modifier.size(size * 0.35f)) }
                }
            } else {
                Text(customIcon, style = MaterialTheme.typography.titleMedium)
            }
        }
    }
}

// AccentColor is free text on the wire, so a bad value must not take the screen down.
internal fun parseAccent(hex: String): Color =
    runCatching { Color(("ff" + hex.removePrefix("#")).toLong(16)) }.getOrDefault(Color(0xFF0078D4))
