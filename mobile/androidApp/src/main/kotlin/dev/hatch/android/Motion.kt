package dev.hatch.android

import androidx.compose.animation.ContentTransform
import androidx.compose.animation.SizeTransform
import androidx.compose.animation.core.CubicBezierEasing
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith

// Material 3 motion tokens, transcribed rather than imported: MotionScheme and the expressive
// easing tokens are Kotlin-internal in material3 1.4.0, same constraint Theme.kt documents.
// One file so every animation in the app agrees on how fast "fast" is.
internal const val MotionShort = 200
internal const val MotionMedium = 300

// The M3 emphasized pair. Decelerate for what is arriving, accelerate for what is leaving —
// the asymmetry is what makes two elements read as one movement instead of a cross-dissolve.
internal val EmphasizedDecelerate = CubicBezierEasing(0.05f, 0.7f, 0.1f, 1f)
internal val EmphasizedAccelerate = CubicBezierEasing(0.3f, 0f, 0.8f, 0.15f)

// Shared-axis X, Material's pattern for moving between peer screens: a fifth of the width, not
// the whole of it, because a full-width slide on a phone reads as a page turn and costs more
// time than the navigation is worth.
internal fun screenTransition(forward: Boolean): ContentTransform {
    val sign = if (forward) 1 else -1
    val transform =
        (
            slideInHorizontally(tween(MotionMedium, easing = EmphasizedDecelerate)) { width ->
                sign * width / 5
            } + fadeIn(tween(MotionMedium))
        ) togetherWith (
            slideOutHorizontally(tween(MotionMedium, easing = EmphasizedAccelerate)) { width ->
                -sign * width / 5
            } + fadeOut(tween(MotionShort))
        )

    // clip = false: the two screens overlap during the slide, and clipping to the animated
    // bounds would cut the outgoing one in half mid-transition.
    return ContentTransform(
        targetContentEnter = transform.targetContentEnter,
        initialContentExit = transform.initialContentExit,
        sizeTransform = SizeTransform(clip = false),
    )
}

// Body swaps that are not lateral navigation — an empty state replacing a list. A fade only:
// nothing moved, so nothing should slide.
internal fun contentFade(): ContentTransform =
    fadeIn(tween(MotionMedium)) togetherWith fadeOut(tween(MotionShort))

// Material's fade-through, for switching between peer destinations that share no elements —
// the bottom bar's four tabs. Deliberately not the cross-fade this used to be: a cross-fade
// draws both screens at half opacity through the middle of the transition, so two different
// lists are legible on top of each other and the switch reads as a smear. Fade-through gets
// the outgoing screen out first (90ms), then brings the incoming one up from 92% over 210ms,
// so only one screen is ever really readable.
private const val FadeThroughOut = 90
private const val FadeThroughIn = 210

internal fun fadeThrough(): ContentTransform =
    (
        fadeIn(tween(FadeThroughIn, delayMillis = FadeThroughOut)) +
            scaleIn(
                tween(FadeThroughIn, delayMillis = FadeThroughOut, easing = EmphasizedDecelerate),
                initialScale = 0.92f,
            )
        ) togetherWith fadeOut(tween(FadeThroughOut))
