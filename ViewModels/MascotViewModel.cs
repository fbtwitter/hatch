using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;

namespace Hatch.ViewModels;

public sealed class MascotViewModel : INotifyPropertyChanged, IDisposable
{
    internal const int WindowSize = 120;
    private const int EdgePadding = 20;

    private readonly DispatcherQueue _dispatcher;
    private PeriodicTimer? _pollTimer;
    private CancellationTokenSource? _cts;
    private bool _isVisible = true;

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
        }
    }

    public int X
    {
        get => App.Settings.MascotX;
        set
        {
            if (App.Settings.MascotX == value) return;
            App.Settings.MascotX = value;
            _ = App.SettingsService.SaveAsync();
            OnPropertyChanged();
        }
    }

    public int Y
    {
        get => App.Settings.MascotY;
        set
        {
            if (App.Settings.MascotY == value) return;
            App.Settings.MascotY = value;
            _ = App.SettingsService.SaveAsync();
            OnPropertyChanged();
        }
    }

    public MascotViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        InitializePosition();
        StartFullscreenPolling();
    }

    private void InitializePosition()
    {
        if (App.Settings.MascotX < 0 || App.Settings.MascotY < 0)
        {
            var workArea = DisplayArea.Primary.WorkArea;
            App.Settings.MascotX = workArea.X + workArea.Width  - WindowSize - EdgePadding;
            App.Settings.MascotY = workArea.Y + workArea.Height - WindowSize - EdgePadding;
            _ = App.SettingsService.SaveAsync();
        }
        else
        {
            ClampToWorkArea();
        }
    }

    // Clamps stored position so the window stays fully inside its monitor's work area.
    private static void ClampToWorkArea()
    {
        var pt = new NativeMethods.POINT { X = App.Settings.MascotX, Y = App.Settings.MascotY };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return;

        var w = mi.rcWork;
        App.Settings.MascotX = Math.Clamp(App.Settings.MascotX, w.left, w.right  - WindowSize);
        App.Settings.MascotY = Math.Clamp(App.Settings.MascotY, w.top,  w.bottom - WindowSize);
    }

    private void StartFullscreenPolling()
    {
        _cts = new CancellationTokenSource();
        _pollTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _ = PollFullscreenAsync(_cts.Token);
    }

    private async Task PollFullscreenAsync(CancellationToken ct)
    {
        try
        {
            while (await _pollTimer!.WaitForNextTickAsync(ct))
            {
                var isFull = IsForegroundWindowFullscreen();
                _dispatcher.TryEnqueue(() => IsVisible = !isFull);
            }
        }
        catch (OperationCanceledException) { }
    }

    // Returns true when a non-Hatch window covers an entire monitor (games, video, presentations).
    private static bool IsForegroundWindowFullscreen()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if ((int)pid == Environment.ProcessId) return false;

        var hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return false;

        NativeMethods.GetWindowRect(hwnd, out var wr);
        var mr = mi.rcMonitor;
        return wr.left <= mr.left && wr.top  <= mr.top &&
               wr.right >= mr.right && wr.bottom >= mr.bottom;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _pollTimer?.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
