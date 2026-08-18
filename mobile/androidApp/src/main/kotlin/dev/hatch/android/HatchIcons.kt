package dev.hatch.android

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.path
import androidx.compose.ui.unit.dp

// material-icons-core ships 49 icons and no chart among them, and material-icons-extended is
// a whole artifact to pull in for one glyph — it was removed from this module once already
// for shipping in every variant. Summary's icon is built here instead: three bars, straight
// lines only, which is also why it is written as an ImageVector rather than as vector-drawable
// XML that could only fail at inflation time.
//
// Black fill, like every Material icon: Icon() tints the whole vector, so the colour comes
// from the call site.
internal object HatchIcons {
    val SummaryFilled: ImageVector by lazy { barChart(filled = true) }
    val SummaryOutlined: ImageVector by lazy { barChart(filled = false) }
}

private const val BarWidth = 3.2f
private const val BarBottom = 19.5f

private fun barChart(filled: Boolean): ImageVector =
    ImageVector.Builder(
        name = if (filled) "SummaryFilled" else "SummaryOutlined",
        defaultWidth = 24.dp,
        defaultHeight = 24.dp,
        viewportWidth = 24f,
        viewportHeight = 24f,
    ).apply {
        // Rising left to right, so the glyph reads as progress rather than as a barcode.
        bar(x = 4.2f, top = 13f, filled = filled)
        bar(x = 10.4f, top = 8.5f, filled = filled)
        bar(x = 16.6f, top = 4.5f, filled = filled)
    }.build()

private fun ImageVector.Builder.bar(x: Float, top: Float, filled: Boolean) {
    val right = x + BarWidth
    if (filled) {
        path(fill = SolidColor(Color.Black)) {
            moveTo(x, top)
            lineTo(right, top)
            lineTo(right, BarBottom)
            lineTo(x, BarBottom)
            close()
        }
    } else {
        path(stroke = SolidColor(Color.Black), strokeLineWidth = 1.7f) {
            moveTo(x, top)
            lineTo(right, top)
            lineTo(right, BarBottom)
            lineTo(x, BarBottom)
            close()
        }
    }
}
