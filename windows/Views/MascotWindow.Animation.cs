using Hatch.Animations;
using Hatch.Models;
using CommunityToolkit.WinUI.Lottie;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Hatch.Views;

public sealed partial class MascotWindow
{
    private bool _usingCustomAnimation;
    private bool _animationClosed;
    private int _sourceLoadVersion;
    private int _animationPlaybackVersion;
    private MascotAnimationState? _builtInSourceState;

    private void ApplyLottieSource() => _ = ApplyLottieSourceAsync();

    private async Task ApplyLottieSourceAsync()
    {
        var version = ++_sourceLoadVersion;
        ++_animationPlaybackVersion;
        _lottieStarted = false;
        _usingCustomAnimation = false;
        _builtInSourceState = null;
        LottiePlayer.Stop();
        var path = ViewModel.LottieFilePath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            LottiePlayer.Visibility = Visibility.Collapsed;
            MascotLoadingRing.Visibility = Visibility.Visible;
            MascotLoadingRing.IsActive = true;
            try
            {
                var source = new LottieVisualSource();
                using var fileStream = File.OpenRead(path);
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var output = stream.GetOutputStreamAt(0))
                {
                    await fileStream.CopyToAsync(output.AsStreamForWrite());
                    await output.FlushAsync();
                }
                stream.Seek(0);
                await source.SetSourceAsync(stream);
                if (_animationClosed || version != _sourceLoadVersion) return;
                _usingCustomAnimation = true;
                LottiePlayer.Source = source;
                if (!LottiePlayer.IsAnimatedVisualLoaded) _usingCustomAnimation = false;
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceWarning($"Custom mascot animation could not load: {error.GetType().Name}");
            }
        }
        if (_animationClosed || version != _sourceLoadVersion) return;
        MascotLoadingRing.IsActive = false;
        MascotLoadingRing.Visibility = Visibility.Collapsed;
        LottiePlayer.Visibility = Visibility.Visible;
        if (!_usingCustomAnimation) ApplyBuiltInAnimation();
        TryPlayEntrance();
        UpdateAnimationState();
        ShowPendingProactiveTip();
    }

    private void ApplyBuiltInAnimation()
    {
        if (_animationClosed || _usingCustomAnimation || MascotLoadingRing.IsActive) return;
        var state = ViewModel.AnimationState;
        if (_builtInSourceState == state && LottiePlayer.Source != null) return;
        ++_animationPlaybackVersion;
        _lottieStarted = false;
        _builtInSourceState = state;
        LottiePlayer.Stop();
        LottiePlayer.Source = MascotAnimationSources.Create(state);
        LottiePlayer.Visibility = Visibility.Visible;
        UpdateAnimationState();
    }

    private void UpdateAnimationState()
    {
        if (_animationClosed || !LottiePlayer.IsAnimatedVisualLoaded) return;
        if (!ViewModel.CanAnimate)
        {
            LottiePlayer.Pause();
            if (!ViewModel.IsVisible || ViewModel.IsMascotHidden) return;
            // An explicit motion preference uses a predictable still frame.
            ++_animationPlaybackVersion;
            LottiePlayer.Stop();
            LottiePlayer.SetProgress(_usingCustomAnimation ? 0 : MascotAnimationSources.StillProgress(ViewModel.AnimationState));
            _lottieStarted = false;
            _hoverIn?.Stop();
            _hoverOut?.Stop();
            _idleFadeOut?.Stop();
            _idleFadeIn?.Stop();
            MascotGrid.Opacity = 1;
            MascotGridTransform.ScaleX = MascotGridTransform.ScaleY = 1;
            return;
        }
        if (_lottieStarted)
        {
            LottiePlayer.Resume();
            return;
        }
        _lottieStarted = true;
        if (_usingCustomAnimation || MascotAnimationSources.IsLooping(ViewModel.AnimationState))
            _ = LottiePlayer.PlayAsync(0, 1, looped: true);
        else
            _ = PlayBuiltInOnceAsync(ViewModel.AnimationState, _animationPlaybackVersion);
    }

    private async Task PlayBuiltInOnceAsync(MascotAnimationState state, int version)
    {
        try
        {
            await LottiePlayer.PlayAsync(0, 1, looped: false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (System.Runtime.InteropServices.COMException) when (_animationClosed || version != _animationPlaybackVersion)
        {
            return;
        }
        if (!_animationClosed && version == _animationPlaybackVersion && ViewModel.CanAnimate)
            ViewModel.CompleteReaction(state);
    }

    private void CloseAnimationPlayback()
    {
        _animationClosed = true;
        ++_sourceLoadVersion;
        ++_animationPlaybackVersion;
        LottiePlayer.Stop();
        LottiePlayer.Source = null;
    }
}
