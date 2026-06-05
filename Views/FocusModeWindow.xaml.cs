using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Hatch.Helpers;
using Hatch.Models;
using Hatch.ViewModels;

namespace Hatch.Views;

public sealed partial class FocusModeWindow : Window
{
    public FocusModeViewModel ViewModel { get; }

    private readonly IntPtr _hwnd;
    private bool _closing;

    public FocusModeWindow(TodoItem task)
    {
        ViewModel = new FocusModeViewModel(task);
        InitializeComponent();

        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);

        Title = "Hatch Focus";
        ExtendsContentIntoTitleBar = true;
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Resize(new SizeInt32(320, 100));
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable   = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        PositionNearMascot();

        ViewModel.ExitRequested += () => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closing)
            {
                _closing = true;
                Close();
            }
        });

        Closed += (_, _) =>
        {
            _closing = true;
            ViewModel.Dispose();
            App.FocusModeWindowInstance = null;
        };

        Activated += OnFirstActivated;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        SetTitleBar(TitleBar);
        try
        {
            SystemBackdrop = OsVersionHelper.CreateBackdrop(App.Settings.Backdrop);
        }
        catch { }
    }

    private void PositionNearMascot()
    {
        var s = App.Settings;
        var workArea = DisplayArea.Primary.WorkArea;

        // Centre horizontally on the mascot, place above it with an 8 px gap.
        int x = s.MascotX + s.MascotSize / 2 - 160;
        int y = s.MascotY - 100 - 8;

        x = Math.Clamp(x, workArea.X, workArea.X + workArea.Width  - 320);
        y = Math.Clamp(y, workArea.Y, workArea.Y + workArea.Height - 100);

        AppWindow.Move(new PointInt32(x, y));
    }
}
