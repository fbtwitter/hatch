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
using Hatch.Models;
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
    private FocusModeViewModel? _focusViewModel;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE          = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    public MascotWindow()
    {
        ViewModel = new MascotViewModel(DispatcherQueue);

        // Set WS_EX_NOREDIRECTIONBITMAP before InitializeComponent so it takes effect
        // before the XAML island allocates a GDI redirection bitmap. Setting it after
        // InitializeComponent is too late on SDR — the bitmap is already created and
        // the white backing bleeds through transparent areas.
        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);

        // Hide before Activate() runs so the compositor's default background fill
        // (black on dark theme) is never painted. Activate() will still trigger
        // OnFirstActivated via WM_ACTIVATE; we show the window there after
        // TransparentTintBackdrop is applied and styles are settled.
        ShowWindow(_hwnd, SW_HIDE);

        ApplyWindowStyles();

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

        // Non-resizable, no title bar chrome. hasBorder=true to avoid DWM injecting
        // WS_DLGFRAME on Windows 10 22H2 (which produces a thin white border).
        // The border is stripped entirely via Win32 style bits in ApplyWindowStyles().
        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable   = false;
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);

        // Required for TransparentTintBackdrop to cover the full window surface.
        // Without this, WinUI 3 does not extend the XAML content root to fill the
        // entire HWND, leaving the underlying GDI window background (white) exposed
        // behind the XAML island — visible as a white rectangle around the mascot.
        ExtendsContentIntoTitleBar = true;

        // Suppress Alt+Tab / taskbar entry — mascot is ambient UI
        AppWindow.IsShownInSwitchers = false;

        AppWindow.Resize(new SizeInt32(ViewModel.WindowSize, ViewModel.WindowSize));

        // Re-apply window styles after all AppWindow.* calls — SetPresenter and
        // ExtendsContentIntoTitleBar both modify GWL_EXSTYLE and can clear
        // WS_EX_NOREDIRECTIONBITMAP that was set before InitializeComponent.
        ApplyWindowStyles();

        // Always-on-top via P/Invoke — respects the persisted setting
        ApplyAlwaysOnTop(App.Settings.MascotAlwaysOnTop);

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
                    if (_bubbleWindow == null)
                    {
                        // First open: create the window once and keep it alive.
                        // Subsequent opens reuse the same window (hide/show) to avoid
                        // paying the XAML island + Mica + DComp initialization cost.
                        _bubbleWindow = new QuickAddBubbleWindow();
                        App.BubbleWindowInstance = _bubbleWindow;
                        _bubbleWindow.Dismissed += () => ViewModel.CloseBubble();
                        _bubbleWindow.PositionRelativeToMascot(ViewModel.X, ViewModel.Y, ViewModel.WindowSize);
                        _bubbleWindow.Activate();
                    }
                    else
                    {
                        _bubbleWindow.ShowAndReset(ViewModel.X, ViewModel.Y, ViewModel.WindowSize);
                    }
                }
                else
                {
                    _bubbleWindow?.HideWindow();
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

    private void ApplyLottieSource() => _ = ApplyLottieSourceAsync();

    private async Task ApplyLottieSourceAsync()
    {
        _lottieStarted = false;

        var path = ViewModel.LottieFilePath;
        var hasFile = !string.IsNullOrEmpty(path) && File.Exists(path);

        if (hasFile)
        {
            // Hide canvas immediately before the first await — prevents the canvas mascot
            // from flashing on screen while the Lottie file is being read and decoded.
            MascotRoot.Visibility = Visibility.Collapsed;
            LottiePlayer.Visibility = Visibility.Collapsed;
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
                _idleAnimation?.Stop();
            }
            catch
            {
                LottiePlayer.Source = null;
                LottiePlayer.Visibility = Visibility.Collapsed;
                MascotRoot.Visibility = Visibility.Visible;
            }
        }
        else
        {
            LottiePlayer.Source = null;
            LottiePlayer.Visibility = Visibility.Collapsed;
            MascotRoot.Visibility = Visibility.Visible;
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
    
    private void ApplyWindowResize()
    {
        var newSize = ViewModel.WindowSize;
        AppWindow.Resize(new SizeInt32(newSize, newSize));
        MascotGridTransform.CenterX = newSize / 2.0;
        MascotGridTransform.CenterY = newSize / 2.0;

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
        if (!FocusPopup.IsOpen) _hoverIn?.Begin();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!FocusPopup.IsOpen) _hoverOut?.Begin();
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
        if (uMsg == NativeMethods.WM_ERASEBKGND)
            return new IntPtr(1);
        if (uMsg == NativeMethods.WM_DISPLAYCHANGE)
        {
            DispatcherQueue.TryEnqueue(ApplyWindowStyles);
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ApplyWindowStyles()
    {
        // Prevent GDI from painting white behind the DComp visual tree.
        NativeMethods.SetClassLongPtr(
            _hwnd,
            NativeMethods.GCLP_HBRBACKGROUND,
            NativeMethods.GetStockObject(NativeMethods.NULL_BRUSH));

        // Flush presenter changes to DWM.
        NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_FRAMECHANGED);

        // Strip all border style bits. SetBorderAndTitleBar(true, false) prevents DWM from
        // injecting WS_DLGFRAME (thin white border artifact on Windows 10 22H2); stripping
        // all three here ensures no visible frame remains.
        var style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_STYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_STYLE,
            style & ~(NativeMethods.WS_BORDER | NativeMethods.WS_DLGFRAME | NativeMethods.WS_THICKFRAME));

        // WS_EX_NOREDIRECTIONBITMAP: DWM composites from the DComp visual tree instead of
        // a GDI bitmap, enabling per-pixel alpha. Must be set before InitializeComponent.
        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE,
            (exStyle | NativeMethods.WS_EX_NOREDIRECTIONBITMAP)
            & ~NativeMethods.WS_EX_WINDOWEDGE);

        if (Helpers.OsVersionHelper.IsWindows11OrGreater)
        {
            var noBorder = NativeMethods.DWMWA_COLOR_NONE;
            NativeMethods.DwmSetWindowAttribute(
                _hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref noBorder, sizeof(uint));

            var noRound = NativeMethods.DWMWCP_DONOTROUND;
            NativeMethods.DwmSetWindowAttribute(
                _hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(uint));
        }

        // Extend DWM frame across the entire client area — required to eliminate the
        // thin top-edge artifact DWM leaves when the frame is not fully extended.
        var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(_hwnd, ref margins);
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;

        // Set transparent backdrop after the compositor is ready. Deferred here because
        // setting SystemBackdrop in the constructor races against DWM/WarpPal
        // initialisation and causes a null vtable crash (0xC0000005).
        SystemBackdrop = new TransparentTintBackdrop();

        // Re-apply all window styles after backdrop — the backdrop controller can reset
        // GWL_EXSTYLE (clearing WS_EX_NOREDIRECTIONBITMAP) and DwmExtendFrameIntoClientArea.
        ApplyWindowStyles();

        // Patch XAML island child HWNDs: WinUI 3 creates internal child windows
        // (ContentIsland host, input source, etc.) that each carry the white HWND
        // class background. They are created during XAML initialisation, so this
        // must run after the first Activated event — not in the constructor.
        NativeMethods.EnumChildWindows(_hwnd, (childHwnd, _) =>
        {
            NativeMethods.SetClassLongPtr(
                childHwnd,
                NativeMethods.GCLP_HBRBACKGROUND,
                NativeMethods.GetStockObject(NativeMethods.NULL_BRUSH));
            return true;
        }, IntPtr.Zero);

        // Backdrop and styles are settled — reveal the window and start Lottie.
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        ResetInactivity();
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

    public void ShowFocusMode(TodoItem task)
    {
        _focusViewModel?.Dispose();
        _focusViewModel = new FocusModeViewModel(task);
        _focusViewModel.ExitRequested += () =>
            DispatcherQueue.TryEnqueue(() => FocusPopup.IsOpen = false);
        FocusTaskTitle.Text = task.Title;
        ToolTipService.SetToolTip(FocusTaskTitle, task.Title);
        // One-shot: position popup once on first measure, then stop listening so
        // the hover scale animation can't shift it on subsequent layout passes.
        FocusPopupBorder.SizeChanged -= OnFocusPopupFirstMeasure;
        FocusPopupBorder.SizeChanged += OnFocusPopupFirstMeasure;
        FocusPopup.IsOpen = true;
    }

    private void OnFocusPopupFirstMeasure(object sender, SizeChangedEventArgs e)
    {
        FocusPopupBorder.SizeChanged -= OnFocusPopupFirstMeasure;
        FocusPopup.HorizontalOffset = (ViewModel.WindowSize - e.NewSize.Width) / 2.0;
        FocusPopup.VerticalOffset   = -(e.NewSize.Height + 8);
    }

    private void FocusPopup_Opened(object? sender, object e)
    {
        // Fade in + slide up from 6 px below final position
        FocusPopupBorder.Opacity = 0;
        FocusPopupTranslate.Y = 6;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur  = new Duration(TimeSpan.FromMilliseconds(180));

        var fade = new DoubleAnimation { From = 0, To = 1, Duration = dur, EasingFunction = ease };
        Storyboard.SetTarget(fade, FocusPopupBorder);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation { From = 6, To = 0, Duration = dur, EasingFunction = ease };
        Storyboard.SetTarget(slide, FocusPopupTranslate);
        Storyboard.SetTargetProperty(slide, "Y");

        var sb = new Storyboard();
        sb.Children.Add(fade);
        sb.Children.Add(slide);
        sb.Begin();
    }

    private void FocusMarkDone_Click(object sender, RoutedEventArgs e)
        => _focusViewModel?.MarkDoneCommand.Execute(null);

    private void FocusExit_Click(object sender, RoutedEventArgs e)
    {
        FocusPopup.IsOpen = false;
        _focusViewModel?.Dispose();
        _focusViewModel = null;
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

        var mainHwnd = Win32Interop.GetWindowFromWindowId(win.AppWindow.Id);

        if (!win.AppWindow.IsVisible)
        {
            // Coming from tray — restore default size and position near mascot.
            MascotViewModel.PositionMainWindowNearMascot(win, resetSize: true);
            win.AppWindow.Show();
        }

        // Flash TOPMOST→NOTOPMOST to raise above other windows, then activate
        // so the user can immediately interact with Settings.
        NativeMethods.SetWindowPos(mainHwnd, NativeMethods.HWND_TOPMOST,   0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        NativeMethods.SetWindowPos(mainHwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        win.Activate();

        win.NavigateToSettings();
    }
}

