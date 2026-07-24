using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Hatch.Models;
using Hatch.Services;
using Hatch.ViewModels;
using Hatch.Views;
using Windows.Graphics;

namespace Hatch;

public sealed partial class MainWindow : Window
{
    private SystemTrayService? _trayService;
    private NativeMethods.SUBCLASSPROC? _subclassProc;
    private IntPtr _hwnd;
    private bool _isExiting;

    public MainViewModel ViewModel { get; } = new MainViewModel();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE    = 0;
    private const int SW_RESTORE = 9;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Hatch";
        AppWindow.Resize(new SizeInt32(620, 640));
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);

        // Apply the app icon to the taskbar button, title bar, and Alt+Tab switcher.
        // The ICO is generated from logo.svg by AssetGen at build time.
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Hatch.ico");
        if (File.Exists(icoPath))
            AppWindow.SetIcon(icoPath);

        // SetWindowSubclass intercepts only at the native level — no managed overhead
        // per message. WindowMessageMonitor routes every WM_* through EventArgs which
        // added noticeable lag during resize/interaction.
        _subclassProc = MinSizeSubclassProc;
        NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, 1, 0);

        _trayService = new SystemTrayService();
        _trayService.Initialize(_hwnd);
        _trayService.ShowRequested += RestoreWindow;
        _trayService.ExitRequested += OnExitRequested;
        _trayService.RestoreMascotRequested += RestoreMascot;
        _trayService.HideMascotRequested += OnHideMascotRequested;

        if (App.Settings.MinimizeToTray)
            _trayService.ShowIcon();

        AppWindow.Closing += OnWindowClosing;
        if (RootFrame is null)
            throw new InvalidOperationException(
                "RootFrame was not initialized by InitializeComponent. " +
                "Clean and rebuild the solution to regenerate XAML code-behind files.");
        if (!App.Settings.FirstRunComplete)
            RootFrame.Navigate(typeof(OnboardingPage), ViewModel);
        else
            RootFrame.Navigate(typeof(MainPage), ViewModel);
        RootFrame.Navigated += OnFrameNavigated;

        // Defer backdrop and theme application to first Activated so the compositor
        // (DWM/WarpPal) is fully initialised. Setting SystemBackdrop in the constructor
        // races against DWM setup and causes a null vtable dereference in
        // Microsoft.UI.Xaml.dll on startup (access violation 0xC0000005).
        Activated += OnFirstActivated;

        Activated += OnPositionNearMascot;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        var settings = App.Settings;
        try
        {
            ApplyBackdrop(settings.Backdrop);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ApplyBackdrop failed: {ex}");
        }
        ApplyTheme(settings.Theme);
    }

    private void OnPositionNearMascot(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnPositionNearMascot;
        if (App.Settings.MascotX < 0 || App.Settings.MascotY < 0 || !App.Settings.FirstRunComplete)
        {
            try
            {
                MascotViewModel.PositionMainWindowNearMascot(this);
                App.Settings.FirstRunComplete = true;
                _ = App.SettingsService.SaveAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Position error: {ex}");
            }
        }
    }

    // 480×490 keeps the 48px compact nav rail plus a usable content area.
    private const int MinW = 480;
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
        SystemBackdrop = Helpers.OsVersionHelper.CreateBackdrop(backdrop);
        (RootFrame?.Content as Views.MainPage)?.ApplyContentBackdrop(backdrop);
    }

    public void UpdateTrayBehavior(bool minimizeToTray)
    {
        if (minimizeToTray) _trayService?.ShowIcon();
        else                _trayService?.HideIcon();
    }

    public void NavigateToSettings()
    {
        // RootFrame holds MainPage; ask it to navigate its ContentFrame to SettingsPage
        if (RootFrame.Content is MainPage mainPage)
            mainPage.NavigateToSettingsPage();
    }

    public void NavigateTo(string tag)
    {
        if (RootFrame.Content is MainPage mainPage)
            mainPage.NavigateTo(tag);
    }

    public void NavigateToTask(TodoItem task)
    {
        if (RootFrame.Content is MainPage mainPage)
            mainPage.NavigateToTask(task);
    }

    public void ShowAndSelectTask(Guid taskId)
    {
        ShowWindow(_hwnd, SW_RESTORE);
        AppWindow.Show(true);
        Activate();
        NavigateTo("alltasks");
        var task = ViewModel.FindTaskById(taskId);
        if (task != null)
            ViewModel.SelectedTask = task;
    }

    public SystemTrayService? GetTrayService() => _trayService;

    private void RestoreWindow()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Do not reposition the window when restoring from tray; just show it
            ShowWindow(_hwnd, SW_RESTORE);
            AppWindow.Show(true);
        });
    }

    private void RestoreMascot()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            App.MascotWindowInstance?.ViewModel.RestoreFromHideCommand.Execute(null);
        });
    }

    private void OnHideMascotRequested(SystemTrayService.HideDuration duration)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var vm = App.MascotWindowInstance?.ViewModel;
            if (vm == null) return;
            ICommand cmd = duration switch
            {
                SystemTrayService.HideDuration.OneHour      => vm.HideFor1HourCommand,
                SystemTrayService.HideDuration.ThreeHours   => vm.HideFor3HoursCommand,
                SystemTrayService.HideDuration.UntilTomorrow => vm.HideUntilTomorrowCommand,
                SystemTrayService.HideDuration.UntilRestart  => vm.HideUntilRestartCommand,
                _ => vm.HideFor1HourCommand
            };
            cmd.Execute(null);
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
            ViewModel.DismissUndoBar();
            ShowWindow(_hwnd, SW_HIDE);
            try
            {
                NativeMethods.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch
            {
                // P/Invoke may fail in some environments; not critical for functionality
            }
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

    private void OnFrameNavigated(object? sender, NavigationEventArgs e)
    {
        if (RootFrame?.Content is MainPage mainPage)
        {
            if (mainPage.FindName("MainTitleBar") is TitleBar titleBar)
            {
                SetTitleBar(titleBar);
            }
        }
    }
}
