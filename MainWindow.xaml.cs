using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Hatch.Models;
using Hatch.Services;
using Hatch.Views;
using Windows.Graphics;

namespace Hatch;

public sealed partial class MainWindow : Window
{
    private SystemTrayService? _trayService;
    private NativeMethods.SUBCLASSPROC? _subclassProc;
    private IntPtr _hwnd;
    private bool _isExiting;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE    = 0;
    private const int SW_RESTORE = 9;

    public MainWindow()
    {
        InitializeComponent();
        Title = "To-Do";
        AppWindow.Resize(new SizeInt32(520, 640));
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

        var settings = App.Settings;
        ApplyBackdrop(settings.Backdrop);
        ApplyTheme(settings.Theme);

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);

        // SetWindowSubclass intercepts only at the native level — no managed overhead
        // per message. WindowMessageMonitor routes every WM_* through EventArgs which
        // added noticeable lag during resize/interaction.
        _subclassProc = MinSizeSubclassProc;
        NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, 1, 0);

        _trayService = new SystemTrayService();
        _trayService.Initialize(_hwnd);
        _trayService.ShowRequested += RestoreWindow;
        _trayService.ExitRequested += OnExitRequested;

        if (settings.MinimizeToTray)
            _trayService.ShowIcon();

        AppWindow.Closing += OnWindowClosing;
        RootFrame.Navigate(typeof(MainPage));
        ApplyTheme(settings.Theme); // RootFrame is guaranteed non-null here
    }

    // 400×490 ≈ 77% of 520×640, maintaining the window's aspect ratio.
    private const int MinW = 400;
    private const int MinH = 490;

    private IntPtr MinSizeSubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        nuint uIdSubclass, nuint dwRefData)
    {
        const uint WM_GETMINMAXINFO = 0x0024;
        if (uMsg == WM_GETMINMAXINFO)
        {
            var info = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
            info.ptMinTrackSize.X = Math.Max(info.ptMinTrackSize.X, MinW);
            info.ptMinTrackSize.Y = Math.Max(info.ptMinTrackSize.Y, MinH);
            Marshal.StructureToPtr(info, lParam, true);
            return IntPtr.Zero;
        }
        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void ApplyTheme(AppTheme theme)
    {
        if (RootFrame is null) return;

        RootFrame.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark  => ElementTheme.Dark,
            _              => ElementTheme.Default
        };

        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark  => TitleBarTheme.Dark,
            _              => TitleBarTheme.UseDefaultAppMode
        };
    }

    public void ApplyBackdrop(AppBackdrop backdrop)
    {
        SystemBackdrop = backdrop switch
        {
            AppBackdrop.Mica          => new MicaBackdrop(),
            AppBackdrop.MicaAlt       => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            AppBackdrop.DesktopAcrylic => new DesktopAcrylicBackdrop(),
            _                         => null
        };
    }

    public void UpdateTrayBehavior(bool minimizeToTray)
    {
        if (minimizeToTray) _trayService?.ShowIcon();
        else                _trayService?.HideIcon();
    }

    private void RestoreWindow()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ShowWindow(_hwnd, SW_RESTORE);
            AppWindow.Show(true);
        });
    }

    private void OnExitRequested()
    {
        _isExiting = true;
        Application.Current.Exit();
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_isExiting && App.Settings.MinimizeToTray)
        {
            args.Cancel = true;
            ShowWindow(_hwnd, SW_HIDE);
            return;
        }
        if (_subclassProc is not null)
        {
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassProc, 1);
            _subclassProc = null;
        }
        _trayService?.Dispose();
        _trayService = null;
    }
}
