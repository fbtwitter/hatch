using Hatch.Models;
using Microsoft.UI.Xaml.Controls;

namespace Hatch.Animations;

internal static class MascotAnimationSources
{
    // Only the selected source creates a Composition visual; other states are not preloaded.
    internal static IAnimatedVisualSource Create(MascotAnimationState state) => state switch
    {
        MascotAnimationState.Greeting => new Hatch_greeting(),
        MascotAnimationState.Thinking => new Hatch_thinking(),
        MascotAnimationState.Celebrate => new Hatch_celebrate(),
        MascotAnimationState.Focus => new Hatch_focus(),
        MascotAnimationState.Sleep => new Hatch_sleep(),
        MascotAnimationState.Drag => new Hatch_drag(),
        _ => new Hatch_idle()
    };

    internal static bool IsLooping(MascotAnimationState state) =>
        state is MascotAnimationState.Idle or MascotAnimationState.Focus or MascotAnimationState.Drag;

    internal static double StillProgress(MascotAnimationState state) =>
        state is MascotAnimationState.Thinking or MascotAnimationState.Sleep ? 1 : 0;
}
