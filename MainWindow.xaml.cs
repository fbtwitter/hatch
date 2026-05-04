using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TodoWinUI3.Models;
using TodoWinUI3.Services;
using TodoWinUI3.Views;
using Windows.Graphics;

namespace TodoWinUI3;

public sealed partial class MainWindow : Window
{
    private SystemTrayService? _trayService;
    private bool _isExiting;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;
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

        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        _trayService = new SystemTrayService();
        _trayService.Initialize(hwnd);
        _trayService.ShowRequested += RestoreWindow;
        _trayService.ExitRequested += OnExitRequested;

        if (settings.MinimizeToTray)
            _trayService.ShowIcon();

        AppWindow.Closing += OnWindowClosing;
        RootFrame.Navigate(typeof(MainPage));
    }

    public void ApplyTheme(AppTheme theme)
    {
        RootFrame.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode
        };
    }

    public void ApplyBackdrop(AppBackdrop backdrop)
    {
        SystemBackdrop = backdrop switch
        {
            AppBackdrop.Mica => new MicaBackdrop(),
            AppBackdrop.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            AppBackdrop.DesktopAcrylic => new DesktopAcrylicBackdrop(),
            _ => null
        };
    }

    public void UpdateTrayBehavior(bool minimizeToTray)
    {
        if (minimizeToTray)
            _trayService?.ShowIcon();
        else
            _trayService?.HideIcon();
    }

    private void RestoreWindow()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ShowWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id), SW_RESTORE);
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
            ShowWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id), SW_HIDE);
            return;
        }
        _trayService?.Dispose();
        _trayService = null;
    }
}
