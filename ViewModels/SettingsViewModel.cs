using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Hatch.Models;
using Hatch.Services;
using Hatch.Views;

namespace Hatch.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings = App.SettingsService;
    private readonly StartupRegistryService _startupRegistry = new();
    private bool _wasSignedIn;

    // Raised on the UI thread when both local and server have tasks after a fresh sign-in.
    // Subscriber (SettingsPage) shows the conflict dialog and calls ResolveConflictAsync.
    public event Action<SyncConflict>? ConflictDetected;

    public SettingsViewModel()
    {
        _wasSignedIn = App.SyncService.IsSignedIn;
        App.SyncService.StateChanged += OnSyncStateChanged;
    }

    // ── Sync ─────────────────────────────────────────────────────────────────

    public bool IsSyncSignedIn => App.SyncService.IsSignedIn;

    public string SyncUserEmail => App.SyncService.UserEmail ?? "";

    public string SyncLastSyncedText
    {
        get
        {
            var t = _settings.Current.LastSyncedAt;
            if (t == null) return "Never synced";
            var diff = DateTime.UtcNow - t.Value;
            if (diff.TotalMinutes < 1)  return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min ago";
            if (diff.TotalHours   < 24) return $"{(int)diff.TotalHours} hr ago";
            return t.Value.ToLocalTime().ToString("MMM d, h:mm tt");
        }
    }

    private bool _isSyncing;
    public bool IsSyncing
    {
        get => _isSyncing;
        private set { _isSyncing = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotSyncing)); }
    }

    public bool IsNotSyncing => !_isSyncing;
    public bool IsSyncNotSignedIn => !IsSyncSignedIn;

    private string? _syncError;
    public string? SyncError
    {
        get => _syncError;
        private set { _syncError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSyncError)); }
    }

    public bool HasSyncError => !string.IsNullOrEmpty(_syncError);

    private string _syncEmail = "";
    public string SyncEmail
    {
        get => _syncEmail;
        set { _syncEmail = value; OnPropertyChanged(); }
    }

    public async Task SignInAsync(string password)
    {
        if (string.IsNullOrWhiteSpace(SyncEmail) || string.IsNullOrWhiteSpace(password)) return;
        IsSyncing = true;
        SyncError = null;
        var error = await App.SyncService.SignInAsync(SyncEmail.Trim(), password);
        IsSyncing = false;
        if (error != null) { SyncError = error; return; }
    }

    public async Task SignUpAsync(string password)
    {
        if (string.IsNullOrWhiteSpace(SyncEmail) || string.IsNullOrWhiteSpace(password)) return;
        IsSyncing = true;
        SyncError = null;
        var msg = await App.SyncService.SignUpAsync(SyncEmail.Trim(), password);
        IsSyncing = false;
        if (msg != null) SyncError = msg;
    }

    public ICommand SignOutCommand => new RelayCommand(async _ =>
    {
        await App.SyncService.SignOutAsync();
        SyncError = null;
    });

    private void OnSyncStateChanged()
    {
        var queue = App.MainWindowInstance?.DispatcherQueue;
        if (queue == null) return;
        queue.TryEnqueue(async () =>
        {
            bool isNowSignedIn = App.SyncService.IsSignedIn;
            bool justSignedIn  = isNowSignedIn && !_wasSignedIn;
            _wasSignedIn = isNowSignedIn;

            OnPropertyChanged(nameof(IsSyncSignedIn));
            OnPropertyChanged(nameof(IsSyncNotSignedIn));
            OnPropertyChanged(nameof(SyncUserEmail));
            OnPropertyChanged(nameof(SyncLastSyncedText));

            if (justSignedIn)
                await CheckAndHandleConflictAsync();
        });
    }

    private async Task CheckAndHandleConflictAsync()
    {
        IsSyncing = true;
        SyncError = null;
        try
        {
            var conflict = await App.SyncService.CheckConflictAsync();
            if (conflict == null)
            {
                // No conflict: pull if there's newer data on the server, then start the timer.
                await App.SyncService.PullIfNewerAsync();
                App.SyncService.StartAutoSync();
                OnPropertyChanged(nameof(SyncLastSyncedText));
                return;
            }

            if (ConflictDetected != null)
            {
                // Hand off to the View — StartAutoSync called after user resolves.
                ConflictDetected.Invoke(conflict);
            }
            else
            {
                // No UI subscriber (e.g. OAuth callback with Settings closed): safe fallback.
                await App.SyncService.ResolveConflictUseServerAsync();
                App.SyncService.StartAutoSync();
                OnPropertyChanged(nameof(SyncLastSyncedText));
            }
        }
        catch { App.SyncService.StartAutoSync(); }
        finally
        {
            IsSyncing = false;
            OnPropertyChanged(nameof(SyncLastSyncedText));
        }
    }

    public async Task ResolveConflictAsync(bool useLocal)
    {
        IsSyncing = true;
        SyncError = null;
        try
        {
            var error = useLocal
                ? await App.SyncService.ResolveConflictUseLocalAsync()
                : await App.SyncService.ResolveConflictUseServerAsync();
            SyncError = error;
        }
        finally
        {
            IsSyncing = false;
            OnPropertyChanged(nameof(SyncLastSyncedText));
            App.SyncService.StartAutoSync();
        }
    }

    public bool MinimizeToTray
    {
        get => _settings.Current.MinimizeToTray;
        set
        {
            if (_settings.Current.MinimizeToTray == value) return;
            _settings.Current.MinimizeToTray = value;
            App.MainWindowInstance?.UpdateTrayBehavior(value);
            _ = _settings.SaveAsync();
            OnPropertyChanged();
        }
    }

    public int ThemeIndex
    {
        get => (int)_settings.Current.Theme;
        set
        {
            if ((int)_settings.Current.Theme == value) return;
            _settings.Current.Theme = (AppTheme)value;
            App.MainWindowInstance?.ApplyTheme(_settings.Current.Theme);
            App.MainWindowInstance?.ViewModel.NotifyThemeChanged();
            App.BubbleWindowInstance?.ApplyCurrentTheme();
            _ = _settings.SaveAsync();
            OnPropertyChanged();
        }
    }

    public int BackdropIndex
    {
        get => (int)_settings.Current.Backdrop;
        set
        {
            if ((int)_settings.Current.Backdrop == value) return;
            _settings.Current.Backdrop = (AppBackdrop)value;
            App.MainWindowInstance?.ApplyBackdrop(_settings.Current.Backdrop);
            _ = _settings.SaveAsync();
            OnPropertyChanged();
        }
    }

    public bool MuteAnimation
    {
        get => _settings.Current.MuteAnimation;
        set
        {
            if (_settings.Current.MuteAnimation == value) return;
            _settings.Current.MuteAnimation = value;
            _ = _settings.SaveAsync();
            App.MascotWindowInstance?.ViewModel.RaiseMuteChanged();
            OnPropertyChanged();
        }
    }

    public bool LockMascotPosition
    {
        get => _settings.Current.LockMascotPosition;
        set
        {
            if (_settings.Current.LockMascotPosition == value) return;
            _settings.Current.LockMascotPosition = value;
            _ = _settings.SaveAsync();
            App.MascotWindowInstance?.ViewModel.RaiseLockPositionChanged();
            OnPropertyChanged();
        }
    }

    public string? LottieFilePath
    {
        get => _settings.Current.LottieFilePath;
        private set
        {
            if (_settings.Current.LottieFilePath == value) return;
            _settings.Current.LottieFilePath = value;
            _ = _settings.SaveAsync();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LottieFileDisplay));
            OnPropertyChanged(nameof(HasLottieFile));
        }
    }

    public string LottieFileDisplay =>
        string.IsNullOrEmpty(LottieFilePath) ? "(none selected)" : Path.GetFileName(LottieFilePath);

    public bool HasLottieFile => !string.IsNullOrEmpty(LottieFilePath);

    public void SetLottieFilePath(string? path)
    {
        if (path != null && !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            return;

        LottieFilePath = path;
        App.MascotWindowInstance?.ViewModel.RaiseLottieFileChanged();
    }

    public bool ShowTipsAutomatically
    {
        get => _settings.Current.ShowTipsAutomatically;
        set
        {
            if (_settings.Current.ShowTipsAutomatically == value) return;
            _settings.Current.ShowTipsAutomatically = value;
            _ = _settings.SaveAsync();
            OnPropertyChanged();
        }
    }

    public bool HideWhenFullscreen
    {
        get => _settings.Current.HideWhenFullscreen;
        set
        {
            if (_settings.Current.HideWhenFullscreen == value) return;
            _settings.Current.HideWhenFullscreen = value;
            _ = _settings.SaveAsync();
            OnPropertyChanged();
        }
    }

    public bool RunAtStartup
    {
        get => _settings.Current.RunAtStartup;
        set
        {
            if (_settings.Current.RunAtStartup == value) return;
            _settings.Current.RunAtStartup = value;
            _startupRegistry.SetStartupEnabled(value);
            _ = _settings.SaveAsync();
            OnPropertyChanged();
        }
    }

    public int MascotSize
    {
        get => _settings.Current.MascotSize;
        set
        {
            var clamped = Math.Clamp(value, 60, 200);
            if (_settings.Current.MascotSize == clamped) return;
            _settings.Current.MascotSize = clamped;
            _ = _settings.SaveAsync();
            App.MascotWindowInstance?.ViewModel.RaiseWindowSizeChanged();
            OnPropertyChanged();
        }
    }

    public bool MascotAlwaysOnTop
    {
        get => _settings.Current.MascotAlwaysOnTop;
        set
        {
            if (_settings.Current.MascotAlwaysOnTop == value) return;
            _settings.Current.MascotAlwaysOnTop = value;
            _ = _settings.SaveAsync();
            App.MascotWindowInstance?.ApplyAlwaysOnTop(value);
            OnPropertyChanged();
        }
    }

    public uint HotkeyModifiers
    {
        get => _settings.Current.HotkeyModifiers;
        set
        {
            if (_settings.Current.HotkeyModifiers == value) return;
            _settings.Current.HotkeyModifiers = value;
            ReRegisterHotKey();
            _ = _settings.SaveAsync();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyDescription));
            OnPropertyChanged(nameof(HotkeyCtrl));
            OnPropertyChanged(nameof(HotkeyShift));
            OnPropertyChanged(nameof(HotkeyAlt));
        }
    }

    public bool HotkeyCtrl
    {
        get => (HotkeyModifiers & NativeMethods.MOD_CONTROL) != 0;
        set => HotkeyModifiers = value
            ? HotkeyModifiers | NativeMethods.MOD_CONTROL
            : HotkeyModifiers & ~NativeMethods.MOD_CONTROL;
    }

    public bool HotkeyShift
    {
        get => (HotkeyModifiers & NativeMethods.MOD_SHIFT) != 0;
        set => HotkeyModifiers = value
            ? HotkeyModifiers | NativeMethods.MOD_SHIFT
            : HotkeyModifiers & ~NativeMethods.MOD_SHIFT;
    }

    public bool HotkeyAlt
    {
        get => (HotkeyModifiers & NativeMethods.MOD_ALT) != 0;
        set => HotkeyModifiers = value
            ? HotkeyModifiers | NativeMethods.MOD_ALT
            : HotkeyModifiers & ~NativeMethods.MOD_ALT;
    }

    public uint HotkeyVirtualKey
    {
        get => _settings.Current.HotkeyVirtualKey;
        set
        {
            if (_settings.Current.HotkeyVirtualKey == value) return;
            _settings.Current.HotkeyVirtualKey = value;
            ReRegisterHotKey();
            _ = _settings.SaveAsync();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyDescription));
        }
    }

    public string HotkeyDescription
    {
        get
        {
            var parts = new System.Text.StringBuilder();
            if ((HotkeyModifiers & NativeMethods.MOD_CONTROL) != 0) parts.Append("Ctrl+");
            if ((HotkeyModifiers & NativeMethods.MOD_SHIFT)   != 0) parts.Append("Shift+");
            if ((HotkeyModifiers & NativeMethods.MOD_ALT)     != 0) parts.Append("Alt+");
            if ((HotkeyModifiers & NativeMethods.MOD_WIN)     != 0) parts.Append("Win+");
            parts.Append(VkToLabel(HotkeyVirtualKey));
            return parts.ToString();
        }
    }

    private static string VkToLabel(uint vk) => vk switch
    {
        0x20 => "Space",
        0xBB => "+",
        0xBC => ",",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        _ => $"0x{vk:X2}"
    };

    private static void ReRegisterHotKey()
    {
        var mascot = App.MascotWindowInstance;
        if (mascot is null) return;
        mascot.UnregisterHotKey();
        mascot.RegisterHotKey();
    }

    public ICommand OpenDataFolderCommand { get; } =
        new RelayCommand(_ =>
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hatch");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        });

    public ICommand OpenGitHubCommand { get; } =
        new RelayCommand(_ => Process.Start(new ProcessStartInfo
            { FileName = "https://github.com/fbtwitter/hatch", UseShellExecute = true }));

    public ICommand OpenGitHubIssuesCommand { get; } =
        new RelayCommand(_ => Process.Start(new ProcessStartInfo
            { FileName = "https://github.com/fbtwitter/hatch/issues", UseShellExecute = true }));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
