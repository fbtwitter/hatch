using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Hatch.Helpers;
using Hatch.Models;
using Hatch.Services;
using Hatch.Views;

namespace Hatch.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private static readonly Windows.Globalization.DateTimeFormatting.DateTimeFormatter _lastSyncedFormatter =
        new("month.abbreviated day hour minute");

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

    public bool IsPassphraseSet => App.SyncService.HasPassphrase;

    // Drives the passphrase card + info bar: sync is paused in this state.
    // The two-factor challenge takes precedence — it is about the session, and until it is
    // satisfied nothing can reach the server for a passphrase to be checked against.
    public bool IsSignedInWithoutPassphrase =>
        IsSyncSignedIn && !IsPassphraseSet && !IsMfaChallengePending;

    public async Task SetSyncPassphraseAsync(string passphrase)
    {
        if (passphrase.Trim().Length < 8)
        {
            SyncError = Strings.Sync_Error_PassphraseTooShort;
            return;
        }

        SyncError = null;

        // Verify against the existing row before storing. Storing first hides the entry
        // card (IsSignedInWithoutPassphrase goes false) and strands the user with a
        // passphrase that cannot decrypt anything and no way to correct it.
        if (!await App.SyncService.CanDecryptServerRowAsync(passphrase))
        {
            SyncError = Strings.Sync_Error_WrongPassphrase;
            return;
        }

        App.SyncService.SetPassphrase(passphrase);
        OnPropertyChanged(nameof(IsPassphraseSet));
        OnPropertyChanged(nameof(IsSignedInWithoutPassphrase));
        OnPropertyChanged(nameof(CanShowRecoveryKit));

        // Offered at the one moment the user is thinking about this secret. A warning in a
        // box has not been enough: the passphrase cannot be reset, recovered or reissued by
        // anyone, so what they need is an artefact to keep, not more prose.
        ShowRecoveryKit();

        // The conflict check deferred at sign-in runs now that server data is readable.
        await CheckAndHandleConflictAsync();
    }

    // --- Sync recovery kit --------------------------------------------------------------
    // Recovery codes restore account access; nothing restores the passphrase, because
    // anything that could would mean the server can decrypt. See docs/mfa-spec.md §6.

    private string? _recoveryKitText;
    public string? RecoveryKitText
    {
        get => _recoveryKitText;
        private set
        {
            _recoveryKitText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRecoveryKit));
        }
    }

    public bool HasRecoveryKit => !string.IsNullOrEmpty(_recoveryKitText);

    // Available whenever a passphrase is set, not only just after setting one: the kit is
    // useless to someone who has already lost the passphrase, so it has to stay reachable
    // while they still have it.
    public bool CanShowRecoveryKit => IsSyncSignedIn && IsPassphraseSet;

    public string RecoveryKitFileName => RecoveryKit.FileName(DateTime.Now);

    public void ShowRecoveryKit()
    {
        var passphrase = App.SyncService.PassphraseForRecoveryKit;
        if (passphrase == null) return;
        RecoveryKitText = RecoveryKit.Build(passphrase, SyncUserEmail, DateTime.Now);
    }

    public void DismissRecoveryKit() => RecoveryKitText = null;

    public string SyncLastSyncedText
    {
        get
        {
            var t = _settings.Current.LastSyncedAt;
            if (t == null) return Strings.Sync_NeverSynced;
            var diff = DateTime.UtcNow - t.Value;
            if (diff.TotalMinutes < 1)  return Strings.Sync_JustNow;
            if (diff.TotalMinutes < 60) return Strings.Sync_MinAgo((int)diff.TotalMinutes);
            if (diff.TotalHours   < 24) return Strings.Sync_HrAgo((int)diff.TotalHours);
            return _lastSyncedFormatter.Format(t.Value.ToLocalTime());
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
        internal set { _syncError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSyncError)); }
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
            OnPropertyChanged(nameof(IsPassphraseSet));
            OnPropertyChanged(nameof(IsSignedInWithoutPassphrase));
            OnPropertyChanged(nameof(CanShowRecoveryKit));

            // Surface OAuth callback failures; without this the browser closes and the app
            // shows nothing at all.
            if (App.SyncService.LastAuthError is { } authError)
                SyncError = authError;

            OnPropertyChanged(nameof(IsMfaChallengePending));
            OnPropertyChanged(nameof(IsMfaSettingsVisible));
            OnPropertyChanged(nameof(ShowMfaOnInfo));
            OnPropertyChanged(nameof(CanEnrollMfa));
            if (isNowSignedIn) await RefreshMfaStateAsync();

            // Without a passphrase the server payload is unreadable — the conflict check
            // is deferred until SetSyncPassphraseAsync provides one. An outstanding
            // two-factor challenge defers it the same way (SubmitMfaChallengeAsync).
            if (justSignedIn && IsPassphraseSet && !IsMfaChallengePending)
                await CheckAndHandleConflictAsync();
        });
    }

    // --- Multi-factor authentication ---------------------------------------------------
    // Protects sign-in only; the passphrase still protects the data. See docs/mfa-spec.md.

    private MfaFactorInfo? _pendingFactor;
    private bool _isMfaEnrolled;

    public bool IsMfaEnrolled
    {
        get => _isMfaEnrolled;
        private set
        {
            _isMfaEnrolled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEnrollMfa));
            OnPropertyChanged(nameof(ShowMfaOnInfo));
        }
    }

    public bool IsMfaEnrolling => _pendingFactor != null;
    public bool CanEnrollMfa => IsMfaSettingsVisible && !_isMfaEnrolled && _pendingFactor == null;

    // A pending challenge hides the whole enrol/disable card: offering "Turn off" to a
    // session that has not proved the second factor would make it trivially bypassable.
    public bool IsMfaSettingsVisible => IsSyncSignedIn && !IsMfaChallengePending;
    public bool ShowMfaOnInfo        => IsMfaEnrolled && !IsMfaChallengePending;

    public bool IsMfaChallengePending => App.SyncService.IsMfaChallengePending;

    public async Task SubmitMfaChallengeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        IsSyncing = true;
        SyncError = null;
        var error = await App.SyncService.SubmitMfaChallengeAsync(code.Trim());
        IsSyncing = false;
        if (error != null) { SyncError = error; return; }

        // Sync was held closed while the challenge stood; this is the deferred resume.
        if (IsPassphraseSet) await CheckAndHandleConflictAsync();
    }

    // Shown as selectable text alongside the QR: enrolling on the same device you would
    // scan from is common, so manual entry has to be possible (docs/mfa-spec.md §4).
    public string? MfaSecret => _pendingFactor?.Secret;

    // Raw SVG markup from the enrolment response. The View turns it into an image; the
    // ViewModel deliberately does not touch imaging types.
    public string? MfaQrSvg => _pendingFactor?.QrSvg;

    public async Task RefreshMfaStateAsync()
    {
        IsMfaEnrolled = await App.SyncService.GetVerifiedMfaFactorAsync() != null;
    }

    public async Task StartMfaEnrollmentAsync()
    {
        SyncError = null;
        var (factor, error) = await App.SyncService.EnrollMfaAsync();
        if (error != null) { SyncError = error; return; }

        _pendingFactor = factor;
        OnPropertyChanged(nameof(IsMfaEnrolling));
        OnPropertyChanged(nameof(MfaSecret));
        OnPropertyChanged(nameof(MfaQrSvg));
        OnPropertyChanged(nameof(CanEnrollMfa));
    }

    public async Task ConfirmMfaEnrollmentAsync(string code)
    {
        if (_pendingFactor == null) return;
        SyncError = null;

        var error = await App.SyncService.VerifyMfaAsync(_pendingFactor.Id, code);
        if (error != null) { SyncError = error; return; }

        _pendingFactor = null;
        OnPropertyChanged(nameof(IsMfaEnrolling));
        OnPropertyChanged(nameof(MfaSecret));
        OnPropertyChanged(nameof(MfaQrSvg));
        await RefreshMfaStateAsync();

        // Generated immediately after verifying, never later: this is the one moment the
        // session is known to be aal2 and the user is already thinking about lockout.
        var (codes, codesError) = await App.SyncService.GenerateRecoveryCodesAsync();
        if (codesError != null) { SyncError = codesError; return; }
        RecoveryCodes = codes;
    }

    // --- Recovery codes ----------------------------------------------------------------

    private string[]? _recoveryCodes;

    // Held only until the user dismisses the panel. The server stores hashes, so once this
    // is cleared the plaintext is gone for good — which is the point of showing it loudly.
    public string[]? RecoveryCodes
    {
        get => _recoveryCodes;
        private set
        {
            _recoveryCodes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRecoveryCodes));
            OnPropertyChanged(nameof(RecoveryCodesText));
        }
    }

    public bool HasRecoveryCodes => _recoveryCodes is { Length: > 0 };

    public string RecoveryCodesText => _recoveryCodes == null ? "" : string.Join("\n", _recoveryCodes);

    public void DismissRecoveryCodes() => RecoveryCodes = null;

    // Shown on the challenge card so a lost authenticator has a way out that does not
    // involve an admin deleting rows.
    private bool _isRedeemingRecovery;
    public bool IsRedeemingRecovery
    {
        get => _isRedeemingRecovery;
        private set { _isRedeemingRecovery = value; OnPropertyChanged(); }
    }

    public void StartRecoveryCodeEntry() => IsRedeemingRecovery = true;
    public void CancelRecoveryCodeEntry() => IsRedeemingRecovery = false;

    public async Task RedeemRecoveryCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        IsSyncing = true;
        SyncError = null;
        var error = await App.SyncService.RedeemRecoveryCodeAsync(code);
        IsSyncing = false;
        if (error != null) { SyncError = error; return; }

        IsRedeemingRecovery = false;
        await RefreshMfaStateAsync();
        if (IsPassphraseSet) await CheckAndHandleConflictAsync();

        // Set last and on its own channel: CheckAndHandleConflictAsync clears SyncError on
        // entry and may set a real one. Two-factor is OFF now, not merely satisfied, and
        // that must not be swallowed by whatever the resumed sync had to say.
        SyncNotice = Strings.Sync_Info_RecoveryUsed;
    }

    private string? _syncNotice;
    public string? SyncNotice
    {
        get => _syncNotice;
        private set { _syncNotice = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSyncNotice)); }
    }

    public bool HasSyncNotice => !string.IsNullOrEmpty(_syncNotice);

    public void DismissSyncNotice() => SyncNotice = null;

    // Abandoning enrolment must remove the unverified factor, or it lingers server-side
    // and blocks a clean retry.
    public async Task CancelMfaEnrollmentAsync()
    {
        if (_pendingFactor == null) return;
        await App.SyncService.UnenrollMfaAsync(_pendingFactor.Id);
        _pendingFactor = null;
        OnPropertyChanged(nameof(IsMfaEnrolling));
        OnPropertyChanged(nameof(MfaSecret));
        OnPropertyChanged(nameof(MfaQrSvg));
        OnPropertyChanged(nameof(CanEnrollMfa));
    }

    public async Task DisableMfaAsync()
    {
        var factor = await App.SyncService.GetVerifiedMfaFactorAsync();
        if (factor == null) return;

        var error = await App.SyncService.UnenrollMfaAsync(factor.Id);
        if (error != null) { SyncError = error; return; }
        await RefreshMfaStateAsync();
    }

    private void ForgetPassphrase()
    {
        App.SyncService.ClearPassphrase();
        OnPropertyChanged(nameof(IsPassphraseSet));
        OnPropertyChanged(nameof(IsSignedInWithoutPassphrase));
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
                SyncError = await App.SyncService.PullIfNewerAsync();
                // A stored passphrase that cannot decrypt the row is worse than none: it
                // hides the entry card forever. Discard it so the user can try again.
                if (SyncError == Strings.Sync_Error_WrongPassphrase) ForgetPassphrase();
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
                // No UI subscriber (e.g. OAuth callback with Settings closed): merge is the
                // safe fallback — unlike "use server", it can't silently discard local data.
                await App.SyncService.ResolveConflictMergeAsync();
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

    public async Task ResolveConflictAsync(SyncConflictResolution resolution)
    {
        IsSyncing = true;
        SyncError = null;
        try
        {
            var error = resolution switch
            {
                SyncConflictResolution.UseLocal  => await App.SyncService.ResolveConflictUseLocalAsync(),
                SyncConflictResolution.UseServer => await App.SyncService.ResolveConflictUseServerAsync(),
                _                                 => await App.SyncService.ResolveConflictMergeAsync()
            };
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
            App.MainWindowInstance?.ApplyTheme(_settings.Current.Theme);
            App.MainWindowInstance?.ViewModel.NotifyThemeChanged();
            App.BubbleWindowInstance?.ApplyCurrentTheme();
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

    public uint HotkeyModifiers
    {
        get => _settings.Current.HotkeyModifiers;
        set
        {
            // Zero modifiers would register the bare key system-wide. The checkboxes already
            // prevent it; this is the backstop for any other caller.
            if (value == 0) return;
            if (_settings.Current.HotkeyModifiers == value) return;
            _settings.Current.HotkeyModifiers = value;
            ReRegisterHotKey();
            _settings.SaveDebounced();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyDescription));
            OnPropertyChanged(nameof(HotkeyCtrl));
            OnPropertyChanged(nameof(HotkeyShift));
            OnPropertyChanged(nameof(HotkeyAlt));
            OnPropertyChanged(nameof(IsHotkeyCtrlEnabled));
            OnPropertyChanged(nameof(IsHotkeyShiftEnabled));
            OnPropertyChanged(nameof(IsHotkeyAltEnabled));
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
            _settings.SaveDebounced();
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

    private void ReRegisterHotKey()
    {
        var mascot = App.MascotWindowInstance;
        if (mascot is null) return;
        mascot.UnregisterHotKey();
        IsHotkeyRegistered = mascot.RegisterHotKey();
    }

    // Seeded from the mascot window rather than assumed true: the hotkey is registered at
    // startup, so a conflict already exists by the time Settings is first opened.
    private bool _isHotkeyRegistered = App.MascotWindowInstance?.IsHotkeyRegistered ?? true;

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

    private int HotkeyModifierCount =>
        (HotkeyCtrl ? 1 : 0) + (HotkeyShift ? 1 : 0) + (HotkeyAlt ? 1 : 0);

    // With no modifier, RegisterHotKey claims the bare key globally — press Space in any
    // application and Hatch would swallow it. The last remaining modifier is locked rather
    // than silently refused, so the constraint is visible instead of feeling broken.
    public bool IsHotkeyCtrlEnabled  => !(HotkeyCtrl  && HotkeyModifierCount == 1);
    public bool IsHotkeyShiftEnabled => !(HotkeyShift && HotkeyModifierCount == 1);
    public bool IsHotkeyAltEnabled   => !(HotkeyAlt   && HotkeyModifierCount == 1);

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
    public async Task ExportAsync(string format, string path)
    {
        ExportError = null;
        try
        {
            var data = await new TaskStorageService().LoadAsync();
            var content = format switch
            {
                "csv"      => TaskExportFormatter.ToCsv(data),
                "markdown" => TaskExportFormatter.ToMarkdown(data),
                _          => TaskExportFormatter.ToJson(data)
            };
            await File.WriteAllTextAsync(path, content);
        }
        catch (Exception ex) { ExportError = ex.Message; }
    }

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
