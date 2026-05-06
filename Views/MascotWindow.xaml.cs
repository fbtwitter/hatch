using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Hatch.ViewModels;

namespace Hatch.Views;

public sealed partial class MascotWindow : Window
{
    public MascotViewModel ViewModel { get; }

    private readonly IntPtr _hwnd;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE         = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    public MascotWindow()
    {
        InitializeComponent();

        ViewModel = new MascotViewModel(DispatcherQueue);

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

        // Phase 1 — always-on-top via P/Invoke
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        // Phase 1 — elliptical region mask: clicks outside the circle hit the desktop
        // OS takes ownership of hRgn after SetWindowRgn — do not DeleteObject
        var hRgn = NativeMethods.CreateEllipticRgn(
            0, 0, MascotViewModel.WindowSize, MascotViewModel.WindowSize);
        NativeMethods.SetWindowRgn(_hwnd, hRgn, true);

        // Phase 2 — restore persisted position (already clamped to work area by ViewModel)
        AppWindow.Move(new PointInt32(ViewModel.X, ViewModel.Y));

        // Phase 3 — react to fullscreen auto-hide
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        Closed += (_, _) => ViewModel.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MascotViewModel.IsVisible)) return;
        ShowWindow(_hwnd, ViewModel.IsVisible ? SW_SHOWNOACTIVATE : SW_HIDE);
    }
}
