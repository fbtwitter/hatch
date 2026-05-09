using System.ComponentModel;
using System.Runtime.InteropServices;
using CommunityToolkit.WinUI.Lottie;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE          = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    public MascotWindow()
    {
        InitializeComponent();

        ViewModel = new MascotViewModel(DispatcherQueue);

        _idleAnimation = MascotGrid.Resources["IdleAnimation"] as Storyboard;
        // Wiggle is built in code so it targets MascotGridTransform (works for both Canvas and Lottie)
        MascotGridTransform.CenterX = MascotViewModel.WindowSize / 2.0;
        MascotGridTransform.CenterY = MascotViewModel.WindowSize / 2.0;
        _wiggleAnimation = BuildWiggleStoryboard();

        // Defer idle animation until after the window is shown to avoid startup lag
        Activated += OnFirstActivated;

        // True desktop transparency — TransparentTintBackdrop makes the compositor
        // surface fully transparent so XAML's Transparent background shows the desktop.
        SystemBackdrop = new TransparentTintBackdrop();

        // Borderless, non-resizable, no title bar chrome
        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable   = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);

        // Suppress Alt+Tab / taskbar entry — mascot is ambient UI
        AppWindow.IsShownInSwitchers = false;

        AppWindow.Resize(new SizeInt32(MascotViewModel.WindowSize, MascotViewModel.WindowSize));

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

        // Always-on-top via P/Invoke
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        // Elliptical region mask for the default egg mascot; removed when Lottie is active.
        SetEggRegion();

        // Restore persisted position (already clamped to work area by ViewModel)
        AppWindow.Move(new PointInt32(ViewModel.X, ViewModel.Y));

        ApplyLottieSource();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MascotViewModel.IsBubbleOpen))
            {
                if (ViewModel.IsBubbleOpen)
                {
                    _wigglePlayed = false;
                    _bubbleWindow = new QuickAddBubbleWindow();
                    _bubbleWindow.PositionRelativeToMascot(ViewModel.X, ViewModel.Y, MascotViewModel.WindowSize);
                    _bubbleWindow.Closed += (_, _) =>
                    {
                        _bubbleWindow = null;
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
        };
        Closed += (_, _) =>
        {
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
                if (!ViewModel.IsVisible) _lottieStarted = false;
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
                var memStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var outputStream = memStream.GetOutputStreamAt(0);
                await fileStream.CopyToAsync(outputStream.AsStreamForWrite());
                await outputStream.FlushAsync();
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

    // OS takes ownership of hRgn after SetWindowRgn — do not DeleteObject.
    private void SetEggRegion()
    {
        var hRgn = NativeMethods.CreateEllipticRgn(
            0, 0, MascotViewModel.WindowSize, MascotViewModel.WindowSize);
        NativeMethods.SetWindowRgn(_hwnd, hRgn, true);
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

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        UpdateAnimationState();
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
}
