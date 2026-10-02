using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Hatch.Helpers;
using Hatch.Models;
using Hatch.Services;
using Hatch.Views;

namespace Hatch.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings;
    private readonly SyncAccountService _syncAccountService;
    private readonly SyncService _syncService;
    private readonly TaskStorageService _taskStorage;
    private readonly UpdateService _updateService;
    private readonly IHotkeyRegistration? _hotkeyRegistration;
    private bool _isHotkeyRegistered;
    private bool _isRecordingHotkey;

    public SyncAccountViewModel SyncAccount { get; }
    private readonly StartupRegistryService _startupRegistry = new();
    private Uri? _appInstallerUri;
    private bool _isCheckingForUpdates;
    private bool _isApplyingUpdate;
    private bool _hasAvailableUpdate;
    private string _updateStatus = Strings.Settings_Update_Description;

    public SettingsViewModel(
        SettingsService settings,
        SyncAccountService syncAccountService,
        SyncService syncService,
        TaskStorageService taskStorage,
        UpdateService updateService,
        IReadOnlyList<TaskList> customLists,
        IHotkeyRegistration? hotkeyRegistration,
        DispatcherQueue dispatcherQueue)
    {
        _settings = settings;
        _syncAccountService = syncAccountService;
        _syncService = syncService;
        _taskStorage = taskStorage;
        _updateService = updateService;
        _hotkeyRegistration = hotkeyRegistration;
        // Registration happens at mascot startup, before the Settings page is created.
        _isHotkeyRegistered = hotkeyRegistration?.IsRegistered ?? true;
        CheckForUpdatesCommand = new RelayCommand(async _ => await CheckForUpdatesAsync());
        InstallUpdateCommand = new RelayCommand(async _ => await InstallUpdateAsync());
        SyncAccount = new SyncAccountViewModel(
            syncAccountService, _syncService, _settings, dispatcherQueue);

        foreach (var line in _settings.Current.CustomTips)
            CustomTips.Add(line);

        InitializeMascotOpenPageOptions(customLists);
    }

    // ── Sync ─────────────────────────────────────────────────────────────────

    public bool MinimizeToTray
    {
        get => _settings.Current.MinimizeToTray;
        set
        {
            if (_settings.Current.MinimizeToTray == value) return;
            _settings.Current.MinimizeToTray = value;
            App.MainWindowInstance?.UpdateTrayBehavior(value);
            _settings.SaveDebounced();
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
            App.ApplyThemeToWindows(_settings.Current.Theme);
            _settings.SaveDebounced();
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
            _settings.SaveDebounced();
            OnPropertyChanged();
        }
    }

    public bool ShowMascot
    {
        get => _settings.Current.ShowMascot;
        set
        {
            if (_settings.Current.ShowMascot == value) return;
            _settings.Current.ShowMascot = value;
            _settings.SaveDebounced();
            App.MascotWindowInstance?.ViewModel.ApplyShowMascotChanged();
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
            _settings.SaveDebounced();
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
            _settings.SaveDebounced();
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
            _settings.SaveDebounced();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LottieFileDisplay));
            OnPropertyChanged(nameof(HasLottieFile));
        }
    }

    public string LottieFileDisplay =>
        string.IsNullOrEmpty(LottieFilePath) ? Strings.Settings_NoFileSelected : Path.GetFileName(LottieFilePath);

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
            _settings.SaveDebounced();
            OnPropertyChanged();
        }
    }

    // ComboBox index maps 1:1 onto TipTimePreference (Anytime=0, Morning=1, Afternoon=2,
    // Evening=3) — keep SettingsPage item order in sync with the enum.
    public int ProactiveTipTimeIndex
    {
        get => (int)_settings.Current.ProactiveTipTime;
        set
        {
            if (value < 0 || (int)_settings.Current.ProactiveTipTime == value) return;
            _settings.Current.ProactiveTipTime = (TipTimePreference)value;
            _settings.SaveDebounced();
            OnPropertyChanged();
        }
    }

    // Read at runtime rather than hardcoded in XAML: the About card had drifted to
    // v0.14.0 while the app shipped 0.17.0, because nothing tied the two together.
    // Packaged builds carry the manifest version; unpackaged Debug builds have no
    // package identity, so they fall back to the assembly version from the csproj.
    public string AppVersion
    {
        get
        {
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                return $"v{v.Major}.{v.Minor}.{v.Build}";
            }
            catch
            {
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return v is null ? "v0.0.0" : $"v{v.Major}.{v.Minor}.{v.Build}";
            }
        }
    }

    public ICommand CheckForUpdatesCommand { get; }
    public ICommand InstallUpdateCommand { get; }

    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set
        {
            if (_isCheckingForUpdates == value) return;
            _isCheckingForUpdates = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCheckForUpdates));
        }
    }

    public bool IsApplyingUpdate
    {
        get => _isApplyingUpdate;
        private set
        {
            if (_isApplyingUpdate == value) return;
            _isApplyingUpdate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanCheckForUpdates));
            OnPropertyChanged(nameof(CanInstallUpdate));
        }
    }

    public bool HasAvailableUpdate
    {
        get => _hasAvailableUpdate;
        private set
        {
            if (_hasAvailableUpdate == value) return;
            _hasAvailableUpdate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanInstallUpdate));
        }
    }

    public bool CanCheckForUpdates => !IsCheckingForUpdates && !IsApplyingUpdate;
    public bool CanInstallUpdate => HasAvailableUpdate && !IsApplyingUpdate;

    public string UpdateStatus
    {
        get => _updateStatus;
        private set
        {
            if (_updateStatus == value) return;
            _updateStatus = value;
            OnPropertyChanged();
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        HasAvailableUpdate = false;
        _appInstallerUri = null;
        UpdateStatus = Strings.Settings_Update_Checking;

        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
            _appInstallerUri = result.AppInstallerUri;
            HasAvailableUpdate = result.Status == UpdateCheckStatus.Available;
            UpdateStatus = result.Status switch
            {
                UpdateCheckStatus.Available => Strings.Settings_Update_Available,
                UpdateCheckStatus.UpToDate => Strings.Settings_Update_UpToDate,
                UpdateCheckStatus.ManagedBySource => Strings.Settings_Update_ManagedBySource,
                _ => Strings.Settings_Update_CheckFailed
            };
        }
        catch
        {
            UpdateStatus = Strings.Settings_Update_CheckFailed;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_appInstallerUri is not { } appInstallerUri) return;

        IsApplyingUpdate = true;
        UpdateStatus = Strings.Settings_Update_Installing;
        try
        {
            await _updateService.InstallUpdateAsync(appInstallerUri);
            UpdateStatus = Strings.Settings_Update_Restarting;
        }
        catch
        {
            UpdateStatus = Strings.Settings_Update_InstallFailed;
        }
        finally
        {
            IsApplyingUpdate = false;
        }
    }

    // Maps 1:1 onto MascotChattiness (Quiet=0, Balanced=1, Chatty=2).
    public int MascotChattinessIndex
    {
        get => (int)_settings.Current.MascotChattiness;
        set
        {
            if (value < 0 || (int)_settings.Current.MascotChattiness == value) return;
            _settings.Current.MascotChattiness = (MascotChattiness)value;
            _settings.SaveDebounced();
            OnPropertyChanged();
        }
    }

    public ObservableCollection<MascotOpenPageOption> MascotOpenPageOptions { get; } = [];

    private MascotOpenPageOption? _selectedMascotOpenPageOption;
    public MascotOpenPageOption? SelectedMascotOpenPageOption
    {
        get => _selectedMascotOpenPageOption;
        set
        {
            if (value == null || _selectedMascotOpenPageOption == value) return;
            _selectedMascotOpenPageOption = value;
            _settings.Current.MascotOpenPageTag = value.Tag;
            _settings.SaveDebounced();
            OnPropertyChanged();
        }
    }

    private void InitializeMascotOpenPageOptions(IReadOnlyList<TaskList> customLists)
    {
        MascotOpenPageOptions.Add(new(null, Strings.Settings_OpenPage_RememberLast));
        MascotOpenPageOptions.Add(new(MascotOpenPageHelper.SummaryFallbackTag, Strings.Settings_OpenPage_Summary));
        MascotOpenPageOptions.Add(new("myday", Strings.Settings_OpenPage_MyDay));
        MascotOpenPageOptions.Add(new("important", Strings.Settings_OpenPage_Important));
        MascotOpenPageOptions.Add(new("planned", Strings.Settings_OpenPage_Planned));
        MascotOpenPageOptions.Add(new("alltasks", Strings.Settings_OpenPage_AllTasks));

        foreach (var list in customLists)
            MascotOpenPageOptions.Add(new(list.Id.ToString(), list.Name));

        var resolvedTag = MascotOpenPageHelper.Resolve(_settings.Current.MascotOpenPageTag, customLists);
        _selectedMascotOpenPageOption =
            MascotOpenPageOptions.FirstOrDefault(o => o.Tag == resolvedTag) ?? MascotOpenPageOptions[0];
    }

    // The user's own message pool, merged with the built-in lines by TipEngine.
    public ObservableCollection<string> CustomTips { get; } = [];

    public bool HasCustomTips => CustomTips.Count > 0;

    public void AddCustomTip(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return;
        if (CustomTips.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) return;

        CustomTips.Add(trimmed);
        PersistCustomTips();
    }

    public void RemoveCustomTip(string text)
    {
        if (!CustomTips.Remove(text)) return;
        PersistCustomTips();
    }

    private void PersistCustomTips()
    {
        _settings.Current.CustomTips = [.. CustomTips];
        _settings.SaveDebounced();
        OnPropertyChanged(nameof(HasCustomTips));
    }

    public bool HideWhenFullscreen
    {
        get => _settings.Current.HideWhenFullscreen;
        set
        {
            if (_settings.Current.HideWhenFullscreen == value) return;
            _settings.Current.HideWhenFullscreen = value;
            _settings.SaveDebounced();
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
            _settings.SaveDebounced();
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
            _settings.SaveDebounced();
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
            _settings.SaveDebounced();
            App.MascotWindowInstance?.ApplyAlwaysOnTop(value);
            OnPropertyChanged();
        }
    }

    public uint HotkeyModifiers => _settings.Current.HotkeyModifiers;
    public uint HotkeyVirtualKey => _settings.Current.HotkeyVirtualKey;

    public bool IsRecordingHotkey
    {
        get => _isRecordingHotkey;
        private set
        {
            if (_isRecordingHotkey == value) return;
            _isRecordingHotkey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyRecordButtonText));
            OnPropertyChanged(nameof(HotkeyRecordDescription));
        }
    }

    public string HotkeyRecordButtonText => IsRecordingHotkey
        ? Strings.Get("Settings_HotkeyRecording")
        : HotkeyDescription;

    public string HotkeyRecordDescription => Strings.Get(IsRecordingHotkey
        ? "Settings_HotkeyRecordingDescription"
        : "Settings_HotkeyRecordDescription");

    public void BeginHotkeyRecording() => IsRecordingHotkey = true;

    public void CancelHotkeyRecording() => IsRecordingHotkey = false;

    public bool HandleHotkeyRecordingKey(uint virtualKey, uint modifiers)
    {
        if (!IsRecordingHotkey) return false;
        if (virtualKey == 0x1B)
        {
            CancelHotkeyRecording();
            return true;
        }
        if (virtualKey == 0 || IsHotkeyModifierKey(virtualKey) || modifiers == 0)
            return true;

        if (HotkeyModifiers != modifiers || HotkeyVirtualKey != virtualKey)
        {
            _settings.Current.HotkeyModifiers = modifiers;
            _settings.Current.HotkeyVirtualKey = virtualKey;
            ReRegisterHotKey();
            _settings.SaveDebounced();
            OnPropertyChanged(nameof(HotkeyModifiers));
            OnPropertyChanged(nameof(HotkeyVirtualKey));
            OnPropertyChanged(nameof(HotkeyDescription));
        }

        IsRecordingHotkey = false;
        return true;
    }

    private static bool IsHotkeyModifierKey(uint virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

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
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        0xBB => "+",
        0xBC => ",",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        0xBD => "-",
        0xBA => ";",
        >= 0x60 and <= 0x69 => $"Num{vk - 0x60}",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        _ => $"0x{vk:X2}"
    };

    private void ReRegisterHotKey()
    {
        if (_hotkeyRegistration is null) return;
        IsHotkeyRegistered = _hotkeyRegistration.ReRegister(
            HotkeyModifiers, HotkeyVirtualKey);
    }

    // Windows reports a taken combination only through RegisterHotKey's return value; the
    // key then silently does nothing. Previously that result was discarded, so Settings
    // showed a hotkey that had never actually been claimed.
    public bool IsHotkeyRegistered
    {
        get => _isHotkeyRegistered;
        private set
        {
            if (_isHotkeyRegistered == value) return;
            _isHotkeyRegistered = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHotkeyConflict));
        }
    }

    public bool HasHotkeyConflict => !_isHotkeyRegistered;

    public ICommand OpenDataFolderCommand { get; } =
        new RelayCommand(_ =>
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hatch");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        });

    // ── Export ───────────────────────────────────────────────────────────────

    private string? _exportError;
    public string? ExportError
    {
        get => _exportError;
        private set { _exportError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasExportError)); }
    }

    public bool HasExportError => !string.IsNullOrEmpty(_exportError);

    // Reads directly from disk (not the live in-memory MainViewModel) so the export always
    // reflects the last-saved state, and Settings stays decoupled from MainViewModel.
    public async Task ExportAsync(string path)
    {
        ExportError = null;
        try
        {
            var data = await _taskStorage.LoadAsync();
            await File.WriteAllTextAsync(path, TaskExportFormatter.ToJson(data));
        }
        catch (Exception ex) { ExportError = ex.Message; }
    }

    // ── Import ───────────────────────────────────────────────────────────────

    private string? _importError;
    public string? ImportError
    {
        get => _importError;
        private set { _importError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasImportError)); }
    }

    public bool HasImportError => !string.IsNullOrEmpty(_importError);

    private string? _importResult;
    public string? ImportResult
    {
        get => _importResult;
        private set { _importResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasImportResult)); }
    }

    public bool HasImportResult => !string.IsNullOrEmpty(_importResult);

    // Merges a JSON export into tasks.json with the same record-level LWW as sync (local wins
    // ties). Reloads MainViewModel so the list — and, via ReloadAsync, due-date reminders —
    // reflect the merge, and pushes when signed in so the merged state propagates.
    public async Task ImportAsync(string path)
    {
        ImportError = null;
        ImportResult = null;
        try
        {
            var imported = TaskImport.Parse(await File.ReadAllTextAsync(path));
            if (imported == null)
            {
                ImportError = Strings.Settings_Import_BadFile;
                return;
            }

            var (merged, applied) = TaskImport.Merge(await _taskStorage.LoadAsync(), imported);
            await _taskStorage.SaveAsync(merged);

            if (App.MainWindowInstance?.ViewModel is { } vm)
                await vm.ReloadAsync();

            if (_syncAccountService.IsSignedIn)
                _syncService.SchedulePush(merged);

            ImportResult = Strings.Settings_Import_Count(applied);
        }
        catch (Exception ex)
        {
            ImportError = ex.Message;
        }
    }

    public ICommand OpenGitHubIssuesCommand { get; } =
        new RelayCommand(_ => Process.Start(new ProcessStartInfo
            { FileName = "https://github.com/fbtwitter/hatch/issues", UseShellExecute = true }));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
