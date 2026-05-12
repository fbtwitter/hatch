using System.ComponentModel;
using System.Runtime.InteropServices;
using CommunityToolkit.WinUI.Lottie;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using System.Numerics;
using Windows.Graphics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;
using WinUIEx;
using Hatch.ViewModels;

namespace Hatch.Views;

public sealed partial class MascotWindow : Window
{
    public MascotViewModel ViewModel { get; }

    private readonly IntPtr _hwnd;
    private Storyboard? _idleAnimation;
    private Storyboard? _wiggleAnimation;
    private QuickAddBubbleWindow? _bubbleWindow;
    private bool _wigglePlayed = false;
    private bool _hasDragged = false;

    // Tracks whether LottiePlayer.PlayAsync has been called at least once for the
    // current source. Resume() is only valid after a Pause(); before first play we
    // must call PlayAsync so the source actually starts loading and rendering.
    private bool _lottieStarted = false;

    // Kept alive to prevent GC — the native subclass holds a function pointer to it
    private NativeMethods.SUBCLASSPROC? _subclassProc;

    private Storyboard? _idleFadeOut;
    private Storyboard? _idleFadeIn;
    private Storyboard? _hoverIn;
    private Storyboard? _hoverOut;
    private DispatcherTimer? _inactivityTimer;
    private bool _isFaded = false;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE          = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    public MascotWindow()
    {
        ViewModel = new MascotViewModel(DispatcherQueue);

        InitializeComponent();

        _idleAnimation = MascotGrid.Resources["IdleAnimation"] as Storyboard;
        _idleFadeOut   = MascotGrid.Resources["IdleFadeOut"]   as Storyboard;
        _idleFadeIn    = MascotGrid.Resources["IdleFadeIn"]    as Storyboard;
        _hoverIn       = MascotGrid.Resources["HoverIn"]       as Storyboard;
        _hoverOut      = MascotGrid.Resources["HoverOut"]      as Storyboard;

        // Inactivity timer — fades the mascot after 30 s of no interaction
        _inactivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _inactivityTimer.Tick += (_, _) =>
        {
            _inactivityTimer.Stop();
            _isFaded = true;
            _idleFadeOut?.Begin();
        };
        // Wiggle is built in code so it targets MascotGridTransform (works for both Canvas and Lottie)
        MascotGridTransform.CenterX = ViewModel.WindowSize / 2.0;
        MascotGridTransform.CenterY = ViewModel.WindowSize / 2.0;
        _wiggleAnimation = BuildWiggleStoryboard();

        // Defer idle animation until after the window is shown to avoid startup lag
        Activated += OnFirstActivated;

        // True desktop transparency — deferred to first Activated so the compositor
        // is fully initialised before TransparentTintBackdrop acquires DComp interfaces.
        // Setting SystemBackdrop in the constructor races against DWM/WarpPal setup and
        // causes a null vtable dereference inside Microsoft.UI.Xaml.dll on startup.
        Activated += OnFirstActivatedSetBackdrop;

        // Borderless, non-resizable, no title bar chrome
        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable   = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);

        // Suppress Alt+Tab / taskbar entry — mascot is ambient UI
        AppWindow.IsShownInSwitchers = false;

        AppWindow.Resize(new SizeInt32(ViewModel.WindowSize, ViewModel.WindowSize));

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);

        // Suppress the 1 px accent-coloured frame Windows 11 draws on every window.
        var noBorder = NativeMethods.DWMWA_COLOR_NONE;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref noBorder, sizeof(uint));

        // Kill the DWM drop shadow — it trails the window rectangle, not the region clip.
        var noShadow = NativeMethods.DWMNCRP_DISABLED;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_NCRENDERING_POLICY, ref noShadow, sizeof(uint));

        // Opt out of Windows 11 automatic rounded corners so the circle clip stays crisp.
        var noRound = NativeMethods.DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(
            _hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(uint));

        // Always-on-top via P/Invoke — respects the persisted setting
        ApplyAlwaysOnTop(App.Settings.MascotAlwaysOnTop);

        // Elliptical region mask for the default egg mascot; removed when Lottie is active.
        SetEggRegion();

        // Restore persisted position (already clamped to work area by ViewModel)
        AppWindow.Move(new PointInt32(ViewModel.X, ViewModel.Y));

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MascotViewModel.IsBubbleOpen))
            {
                if (ViewModel.IsBubbleOpen)
                {
                    _wigglePlayed = false;
                    _bubbleWindow = new QuickAddBubbleWindow();
                    App.BubbleWindowInstance = _bubbleWindow;
                    _bubbleWindow.PositionRelativeToMascot(ViewModel.X, ViewModel.Y, ViewModel.WindowSize);
                    _bubbleWindow.Closed += (_, _) =>
                    {
                        _bubbleWindow = null;
                        App.BubbleWindowInstance = null;
                        ViewModel.CloseBubble();
                    };
                    _bubbleWindow.Activate();
                }
                else if (_bubbleWindow != null)
                {
                    var closing = _bubbleWindow;
                    _bubbleWindow = null;
                    try { closing.Close(); } catch { /* window may already be closing */ }
                }
            }
            else if (e.PropertyName == nameof(MascotViewModel.IsMascotHidden))
            {
                ShowWindow(_hwnd, ViewModel.IsMascotHidden ? SW_HIDE : SW_SHOWNOACTIVATE);
            }
            else if (e.PropertyName == nameof(MascotViewModel.WindowSize))
            {
                ApplyWindowResize();
            }
            else if (e.PropertyName == nameof(MascotViewModel.ShowDailyTipIndicator))
            {
                DailyTipIndicator.Visibility = ViewModel.ShowDailyTipIndicator
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        };
        Closed += (_, _) =>
        {
            _inactivityTimer?.Stop();
            UnregisterHotKey();
            _bubbleWindow?.Close();
            ViewModel.Dispose();
        };

        RegisterHotKey();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MascotViewModel.IsVisible):
                ShowWindow(_hwnd, ViewModel.IsVisible ? SW_SHOWNOACTIVATE : SW_HIDE);
                // Treat coming back from fullscreen hide as a fresh start for Lottie
                if (!ViewModel.IsVisible)
                {
                    _lottieStarted = false;
                    _inactivityTimer?.Stop();
                }
                else
                {
                    ResetInactivity();
                }
                UpdateAnimationState();
                break;
            case nameof(MascotViewModel.MuteAnimation):
                UpdateAnimationState();
                break;
            case nameof(MascotViewModel.LottieFilePath):
                ApplyLottieSource();
                break;
            case nameof(MascotViewModel.X):
            case nameof(MascotViewModel.Y):
                AppWindow.Move(new PointInt32(ViewModel.X, ViewModel.Y));
                break;
        }
    }

    private async void ApplyLottieSource()
    {
        _lottieStarted = false;

        var path = ViewModel.LottieFilePath;
        var hasFile = !string.IsNullOrEmpty(path) && File.Exists(path);

        if (hasFile)
        {
            try
            {
                // LottieVisualSource.UriSource does not support file:// URIs — the internal
                // UriLoader falls through to HttpClient which rejects file:// schemes.
                // StorageFile.GetFileFromPathAsync requires package identity which is not
                // available in unpackaged apps. Instead, read the file into a memory stream
                // and supply it via SetSourceAsync(IRandomAccessStream).
                var source = new LottieVisualSource();
                using var fileStream = File.OpenRead(path!);
                using var memStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var outputStream = memStream.GetOutputStreamAt(0);
                await fileStream.CopyToAsync(outputStream.AsStreamForWrite());
                await outputStream.FlushAsync();
                outputStream.Dispose();
                memStream.Seek(0);
                await source.SetSourceAsync(memStream);
                LottiePlayer.Source = source;
                LottiePlayer.Visibility = Visibility.Visible;
                MascotRoot.Visibility = Visibility.Collapsed;
                _idleAnimation?.Stop();

                // Remove the elliptical clip so the Lottie renders in the full window rect.
                // SetWindowRgn(null) restores the default rectangular hit-area.
                NativeMethods.SetWindowRgn(_hwnd, IntPtr.Zero, true);
            }
            catch
            {
                LottiePlayer.Source = null;
                LottiePlayer.Visibility = Visibility.Collapsed;
                MascotRoot.Visibility = Visibility.Visible;
                SetEggRegion();
            }
        }
        else
        {
            LottiePlayer.Source = null;
            LottiePlayer.Visibility = Visibility.Collapsed;
            MascotRoot.Visibility = Visibility.Visible;
            SetEggRegion();
        }

        UpdateAnimationState();
    }

    private void UpdateAnimationState()
    {
        var shouldPlay = ViewModel.IsVisible && !ViewModel.MuteAnimation;

        if (LottiePlayer.Visibility == Visibility.Visible)
        {
            if (!shouldPlay)
            {
                LottiePlayer.Pause();
            }
            else if (_lottieStarted)
            {
                // Resume from where Pause() left off (e.g. unmute)
                LottiePlayer.Resume();
            }
            else
            {
                // First play for this source — PlayAsync triggers source loading
                // and then starts playback. Resume() cannot do this.
                _lottieStarted = true;
                _ = LottiePlayer.PlayAsync(0, 1, looped: true);
            }
        }
        else
        {
            if (!shouldPlay) _idleAnimation?.Pause();
            else             _idleAnimation?.Begin();
        }
    }

    private void SetEggRegion()
    {
        SetEggRegion(ViewModel.WindowSize);
    }

    private void SetEggRegion(int size)
    {
        var hRgn = NativeMethods.CreateEllipticRgn(0, 0, size, size);
        NativeMethods.SetWindowRgn(_hwnd, hRgn, true);
    }

    private void ApplyWindowResize()
    {
        var newSize = ViewModel.WindowSize;
        AppWindow.Resize(new SizeInt32(newSize, newSize));
        MascotGridTransform.CenterX = newSize / 2.0;
        MascotGridTransform.CenterY = newSize / 2.0;

        // Only re-apply the elliptical region when the egg mascot is active.
        // When Lottie is active SetWindowRgn was cleared to IntPtr.Zero and
        // must stay that way so the rectangular Lottie canvas receives input.
        if (LottiePlayer.Visibility != Visibility.Visible)
            SetEggRegion(newSize);

        // Reposition the window to the (already clamped) stored coordinates.
        // ResizeByValue → ClampToWorkArea updates X/Y in settings but the
        // PropertyChanged for X/Y fires before the window is resized, so we
        // apply the final move here once the new size is committed.
        AppWindow.Move(new PointInt32(ViewModel.X, ViewModel.Y));
    }

    public void ApplyAlwaysOnTop(bool alwaysOnTop)
    {
        var insert = alwaysOnTop ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST;
        NativeMethods.SetWindowPos(_hwnd, insert,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void ResetInactivity()
    {
        if (!ViewModel.IsVisible) return;
        _inactivityTimer?.Stop();
        if (_isFaded)
        {
            _isFaded = false;
            _idleFadeIn?.Begin();
        }
        _inactivityTimer?.Start();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ResetInactivity();
        _hoverIn?.Begin();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hoverOut?.Begin();
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        // Suppress tap if the pointer moved (drag just ended)
        if (_hasDragged)
        {
            _hasDragged = false;
            return;
        }

        ViewModel.ToggleBubbleCommand.Execute(null);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        _hasDragged = false;
        ResetInactivity();
        ViewModel.BeginDrag(AppWindow.Position.X, AppWindow.Position.Y);
        ((UIElement)sender).CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.IsDragging)
            _hasDragged = true;
        ViewModel.ContinueDrag();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.EndDrag();
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
    }

    internal void RegisterHotKey()
    {
        var settings = App.Settings;
        NativeMethods.RegisterHotKey(
            _hwnd,
            NativeMethods.HOTKEY_ID,
            settings.HotkeyModifiers | NativeMethods.MOD_NOREPEAT,
            settings.HotkeyVirtualKey);

        // Install a native subclass to intercept WM_HOTKEY without polling
        _subclassProc = SubclassProc;
        NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, 1, 0);
    }

    internal void UnregisterHotKey()
    {
        NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_ID);
        if (_subclassProc != null)
        {
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassProc, 1);
            _subclassProc = null;
        }
    }

    private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == NativeMethods.WM_HOTKEY && (int)wParam == NativeMethods.HOTKEY_ID)
        {
            DispatcherQueue.TryEnqueue(() => ViewModel.ToggleBubbleCommand.Execute(null));
            return IntPtr.Zero;
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void OnFirstActivatedSetBackdrop(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivatedSetBackdrop;
        SystemBackdrop = new TransparentTintBackdrop();
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        // ApplyLottieSource is called here (deferred from the constructor) so the
        // DComp/WarpPal compositor is fully initialised before any Storyboard or
        // AnimatedVisualPlayer touches it. Calling it in the constructor races
        // against DWM setup and causes a null vtable dereference (0xC0000005).
        ApplyLottieSource();

        // Auto-open bubble on first run with intro copy, but not on startup launch
        if (!App.Settings.FirstRunComplete && !App.IsStartupLaunch)
        {
            DispatcherQueue.TryEnqueue(() => ViewModel.ToggleBubbleCommand.Execute(null));
        }
    }

    private Storyboard BuildWiggleStoryboard()
    {
        // Key frames: 0 → -7 → 7 → -7 → 7 → -7 → 0  over 480 ms
        // Targets MascotGridTransform so both the Canvas mascot and Lottie player wiggle.
        double[] angles = [0, -7, 7, -7, 7, -7, 0];
        double[] times  = [0, 0.08, 0.16, 0.24, 0.32, 0.40, 0.48];
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var anim = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(anim, MascotGridTransform);
        Storyboard.SetTargetProperty(anim, "Rotation");

        for (int i = 0; i < angles.Length; i++)
        {
            anim.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime        = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(times[i])),
                Value          = angles[i],
                EasingFunction = ease
            });
        }

        var sb = new Storyboard();
        sb.Children.Add(anim);
        return sb;
    }

    public void PlayWiggleAnimation()
    {
        if (_wigglePlayed || ViewModel.MuteAnimation) return;
        _wigglePlayed = true;
        _wiggleAnimation?.Stop();
        _wiggleAnimation?.Begin();
    }

    private void OnMascotSettingsMenuItemClick(object sender, RoutedEventArgs e)
    {
        // Show the main window and navigate straight to Settings.
        // XamlRoot for any dialog is guaranteed correct there.
        var win = App.MainWindowInstance;
        if (win == null) return;

        MascotViewModel.PositionMainWindowNearMascot(win);
        win.AppWindow.Show();

        var mainHwnd = Win32Interop.GetWindowFromWindowId(win.AppWindow.Id);
        NativeMethods.SetWindowPos(
            mainHwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        win.NavigateToSettings();
    }
}
