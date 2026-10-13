using Hatch.Models;
using Windows.Foundation.Metadata;
using Windows.UI.ViewManagement;

namespace Hatch.ViewModels;

public sealed partial class MascotViewModel
{
    private bool _isResting;
    private bool _isFocusActive;
    private bool _isFocusPaused;
    private bool _isPlanning;
    private bool _hasQuickAddDraft;
    private bool _hasMainTaskDraft;
    private bool _systemAnimationsEnabled = true;
    private MascotAnimationState? _animationReaction;
    private MascotAnimationState _animationState;
    private readonly UISettings _animationSettings = new();
    private bool _listeningForAnimationSettings;

    public MascotAnimationState AnimationState => _animationState;
    public bool CanAnimate => IsVisible && !IsMascotHidden && !MuteAnimation && _systemAnimationsEnabled;
    public bool IsTaskDraftActive => (IsBubbleOpen && _hasQuickAddDraft) || _hasMainTaskDraft;

    private void InitializeAnimationSettings()
    {
        _systemAnimationsEnabled = _animationSettings.AnimationsEnabled;
        // The event was introduced in 2004; Hatch also runs on Windows 10 1809.
        _listeningForAnimationSettings = ApiInformation.IsEventPresent(
            "Windows.UI.ViewManagement.UISettings", "AnimationsEnabledChanged");
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && _listeningForAnimationSettings)
            _animationSettings.AnimationsEnabledChanged += OnSystemAnimationsChanged;
    }

    private void OnSystemAnimationsChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
        => _dispatcher.TryEnqueue(() => SetSystemAnimationsEnabled(sender.AnimationsEnabled));

    private void DisposeAnimationSettings()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && _listeningForAnimationSettings)
            _animationSettings.AnimationsEnabledChanged -= OnSystemAnimationsChanged;
    }

    public void SetSystemAnimationsEnabled(bool enabled)
    {
        if (_systemAnimationsEnabled == enabled) return;
        _systemAnimationsEnabled = enabled;
        RefreshAnimationState();
    }

    public void SetResting(bool resting)
    {
        if (_isResting == resting) return;
        _isResting = resting;
        RefreshAnimationState();
    }

    public void SetFocusActive(bool active, bool paused = false)
    {
        if (_isFocusActive == active && _isFocusPaused == (active && paused)) return;
        _isFocusActive = active;
        _isFocusPaused = active && paused;
        RefreshAnimationState();
    }

    public void SetPlanning(bool planning)
    {
        if (_isPlanning == planning) return;
        _isPlanning = planning;
        RefreshAnimationState();
    }

    public void SetQuickAddDraftActive(bool active)
    {
        if (_hasQuickAddDraft == active) return;
        _hasQuickAddDraft = active;
        RefreshDraftAnimation();
    }

    public void SetMainTaskDraftActive(bool active)
    {
        if (_hasMainTaskDraft == active) return;
        _hasMainTaskDraft = active;
        RefreshDraftAnimation();
    }

    private void RefreshDraftAnimation()
    {
        if (IsTaskDraftActive)
        {
            _isResting = false;
            if (_animationReaction == MascotAnimationState.Greeting) _animationReaction = null;
        }
        RefreshAnimationState();
    }

    public void PlayReaction(MascotAnimationState state)
    {
        if (!CanAnimate || IsDragging || state is not (MascotAnimationState.Greeting or MascotAnimationState.Celebrate)) return;
        if (state == MascotAnimationState.Greeting && IsTaskDraftActive) return;
        _animationReaction = state;
        RefreshAnimationState();
    }

    public void CompleteReaction(MascotAnimationState state)
    {
        if (_animationReaction != state) return;
        _animationReaction = null;
        RefreshAnimationState();
    }

    private void RefreshAnimationState()
    {
        if (!CanAnimate || IsDragging) _animationReaction = null;
        var state = IsDragging ? MascotAnimationState.Drag
            : _animationReaction ?? (IsTaskDraftActive ? MascotAnimationState.Thinking
                : _isFocusActive ? (_isFocusPaused ? MascotAnimationState.Sleep : MascotAnimationState.Focus)
                : _isPlanning ? MascotAnimationState.Thinking
                : _isResting ? MascotAnimationState.Sleep
                : MascotAnimationState.Idle);
        if (_animationState != state)
        {
            _animationState = state;
            OnPropertyChanged(nameof(AnimationState));
        }
        OnPropertyChanged(nameof(CanAnimate));
    }
}
