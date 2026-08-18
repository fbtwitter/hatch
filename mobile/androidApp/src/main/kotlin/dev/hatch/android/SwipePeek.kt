package dev.hatch.android

import androidx.compose.animation.core.animate
import androidx.compose.animation.core.tween
import androidx.compose.foundation.gestures.Orientation
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.draggable
import androidx.compose.foundation.gestures.rememberDraggableState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalViewConfiguration
import androidx.compose.ui.platform.ViewConfiguration
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import kotlin.math.abs
import kotlin.math.roundToInt
import kotlinx.coroutines.launch

// My Day and Lists stay separate NavHost destinations — bottom-bar highlighting, Summary's
// jump-to-a-list, everything about the nav graph is untouched. This only adds a live,
// finger-tracked preview of whichever screen a drag would land on, springing back if it
// doesn't cross the commit threshold. It is not a real shared pager (that would need both
// screens to be pages of one HorizontalPager, which is a bigger change than this pass took
// on) — it is two still-independent screens, each faking the other's reveal on its own side.
//
// The peek is a real instance of the sibling screen, not a mockup — same composables, same
// data, a second live copy composed alongside the current one for as long as the drag is in
// progress. It carries its own independent Compose state (its own scroll position, its own
// folder-pager page if it's Lists) rather than the one NavHost keeps saved for that
// destination, so a peek always starts from a neutral position rather than wherever the real
// screen was last left — acceptable for something visible only while a finger is still down.
@Composable
fun SwipePeekHost(
    // true: the peek slides in from the right on a leftward drag (My Day -> Lists).
    // false: the peek slides in from the left on a rightward drag (Lists -> My Day). Lists
    // owns a HorizontalPager for its own folder strip, which claims horizontal drags across
    // its full width, so this direction only starts from a slim strip at the very left edge
    // instead — see the edge-catcher branch below for why that is enough.
    peekFromRight: Boolean,
    onCommit: () -> Unit,
    peekContent: @Composable () -> Unit,
    content: @Composable () -> Unit,
) {
    val haptics = LocalHapticFeedback.current
    val scope = rememberCoroutineScope()
    var widthPx by remember { mutableStateOf(0f) }
    // The single source of truth for both screens' live position — written synchronously
    // during the drag (no coroutine needed, the callbacks below aren't suspend) and animated
    // by `animate(...)` only once the gesture ends.
    var dragPx by remember { mutableStateOf(0f) }
    var armed by remember { mutableStateOf(false) }
    val thresholdPx = with(LocalDensity.current) { SwipeCommitDistance.toPx() }

    fun settle(target: Float, onDone: () -> Unit = {}) {
        scope.launch {
            animate(dragPx, target, animationSpec = tween(MotionMedium, easing = EmphasizedDecelerate)) { value, _ ->
                dragPx = value
            }
            onDone()
        }
    }

    fun onDragFinished() {
        val committing = armed
        armed = false
        if (committing) {
            settle(if (peekFromRight) -widthPx else widthPx) {
                onCommit()
                // Reset after the real destination has taken over, not before — resetting
                // first would flash the current screen back into place for a frame while
                // NavHost is still mid-navigation.
                dragPx = 0f
            }
        } else {
            settle(0f)
        }
    }

    fun onDrag(dragAmount: Float) {
        val next = (dragPx + dragAmount).let {
            if (peekFromRight) it.coerceIn(-widthPx, 0f) else it.coerceIn(0f, widthPx)
        }
        if (next != dragPx) {
            dragPx = next
            val shouldArm = abs(next) >= thresholdPx
            if (shouldArm != armed) {
                armed = shouldArm
                if (shouldArm) haptics.performHapticFeedback(HapticFeedbackType.GestureThresholdActivate)
            }
        }
    }

    // draggable, not a raw pointerInput/detectHorizontalDragGestures overlay sitting on top of
    // everything: an early version did that, and an unconditional full-screen sibling Box —
    // even an empty one — sits in the hit-test path for every touch on the screen, which
    // blocked My Day's own vertical scrolling outright rather than just competing with it.
    // draggable's orientation lock is what a plain pointerInput detector does not give for
    // free: applied here as an ancestor of the real content rather than a topmost overlay, it
    // performs axis-aware slop detection against the descendant LazyColumn's own vertical
    // scrollable, so a vertical drag is never even claimed — the list scrolls exactly as if
    // this modifier were not here, and only a drag that is unambiguously horizontal, starting
    // from rest, ever reaches onDrag at all.
    val horizontalDragModifier = if (peekFromRight) {
        Modifier.draggable(
            state = rememberDraggableState { delta -> onDrag(delta) },
            orientation = Orientation.Horizontal,
            onDragStopped = { onDragFinished() },
        )
    } else {
        Modifier
    }

    Box(
        Modifier
            .fillMaxSize()
            .onSizeChanged { widthPx = it.width.toFloat() }
            .then(horizontalDragModifier),
    ) {
        Box(Modifier.offset { IntOffset(dragPx.roundToInt(), 0) }) { content() }
        // Composed only while a drag is actually moving it — an off-screen live screen sitting
        // fully composed at rest would be a second Scaffold, LazyColumn and everything else
        // for nothing.
        if (dragPx != 0f) {
            val peekRestX = if (peekFromRight) widthPx else -widthPx
            Box(Modifier.offset { IntOffset((peekRestX + dragPx).roundToInt(), 0) }) { peekContent() }
        }

        if (!peekFromRight) {
            // Lists' own folder pager is horizontal too, so orientation-locking cannot settle
            // this one the way it does My Day's vertical list — both this and the pager want
            // the same axis. A drag starting here still has to win the very first pixels of
            // movement against the pager underneath, which sees the identical down event;
            // being narrow doesn't exempt it from that race. Giving only this strip a much
            // smaller touch slop is what actually wins it: this detector decides "this is my
            // gesture" and consumes almost immediately, before the pager's own (unscaled) slop
            // has accumulated enough travel to claim it instead. Once consumed, the same
            // pointer keeps reporting to this handler for the rest of the gesture no matter
            // how far across the screen it then travels, so the strip itself only ever needs
            // to catch the opening moment, not the whole drag — and being only 24dp wide, it
            // costs Lists nothing outside that sliver, including its own vertical scrolling.
            val baseViewConfiguration = LocalViewConfiguration.current
            val edgeViewConfiguration = remember(baseViewConfiguration) {
                object : ViewConfiguration by baseViewConfiguration {
                    override val touchSlop: Float = baseViewConfiguration.touchSlop * EdgeTouchSlopFactor
                }
            }
            CompositionLocalProvider(LocalViewConfiguration provides edgeViewConfiguration) {
                Box(
                    Modifier
                        .fillMaxHeight()
                        .width(EdgeSwipeWidth)
                        .align(Alignment.CenterStart)
                        .pointerInput(Unit) {
                            detectHorizontalDragGestures(
                                onDragEnd = ::onDragFinished,
                                onDragCancel = ::onDragFinished,
                                onHorizontalDrag = { change, dragAmount ->
                                    change.consume()
                                    onDrag(dragAmount)
                                },
                            )
                        },
                )
            }
        }
    }
}

// Absolute travel, not a fraction of screen width: there is no natural "row width" for a
// page-level gesture to be a fraction of, and this is a bigger commitment (leaving the screen)
// than a row's own delete threshold, so it asks for a deliberate amount of travel either way.
private val SwipeCommitDistance = 96.dp

// Matches the width Android's own predictive-back edge zone uses — wide enough for a thumb to
// reliably land a drag on, narrow enough to stay out of the folder pager's own way everywhere
// else on screen.
private val EdgeSwipeWidth = 24.dp

// Not zero: an exactly-zero slop fires on the first reported pixel of any touch, which would
// make even a near-vertical scroll that grazes this strip register as a claimed horizontal
// drag. A fifth of the system default is small enough to win the race against the pager's
// unscaled slop while still requiring the drag to be recognizably sideways.
private const val EdgeTouchSlopFactor = 0.2f
