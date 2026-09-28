using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Hatch.Helpers;
using Hatch.Models;
using Hatch.Services;
using Microsoft.UI.Dispatching;

namespace Hatch.ViewModels;

public sealed class SyncAccountViewModel : INotifyPropertyChanged
{
    private static readonly Windows.Globalization.DateTimeFormatting.DateTimeFormatter _lastSyncedFormatter =
        new("month.abbreviated day hour minute");

    private readonly SyncService _syncService;
    private readonly SettingsService _settings;
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _wasSignedIn;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<SyncConflict>? ConflictDetected;

    public SyncAccountViewModel(SyncService syncService, SettingsService settings, DispatcherQueue dispatcherQueue)
    {
        _syncService = syncService;
        _settings = settings;
        _dispatcherQueue = dispatcherQueue;
        _wasSignedIn = syncService.IsSignedIn;
        _syncService.StateChanged += OnSyncStateChanged;
    }

    public bool IsSyncSignedIn => _syncService.IsSignedIn;

    public string SyncUserEmail => _syncService.UserEmail ?? "";

    public bool IsPassphraseSet => _syncService.HasPassphrase;

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
        if (!await _syncService.CanDecryptServerRowAsync(passphrase))
        {
            SyncError = Strings.Sync_Error_WrongPassphrase;
            return;
        }

        _syncService.SetPassphrase(passphrase);
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
        var passphrase = _syncService.PassphraseForRecoveryKit;
        if (passphrase == null) return;
        RecoveryKitText = RecoveryKit.Build(passphrase, SyncUserEmail, DateTime.Now);
    }

    public void DismissRecoveryKit() => RecoveryKitText = null;

    private bool _isChangingPassphrase;
    public bool IsChangingPassphrase
    {
        get => _isChangingPassphrase;
        private set { _isChangingPassphrase = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartChangePassphrase)); }
    }

    public bool CanStartChangePassphrase => IsSyncSignedIn && IsPassphraseSet && !IsChangingPassphrase;

    public void StartChangePassphrase() => IsChangingPassphrase = true;
    public void CancelChangePassphrase() => IsChangingPassphrase = false;

    public async Task ChangePassphraseAsync(string oldPassphrase, string newPassphrase, string confirmPassphrase)
    {
        if (newPassphrase.Trim().Length < 8)
        {
            SyncError = Strings.Sync_Error_PassphraseTooShort;
            return;
        }
        if (newPassphrase != confirmPassphrase)
        {
            SyncError = Strings.Sync_Error_PassphraseMismatch;
            return;
        }

        SyncError = null;
        IsSyncing = true;
        var error = await _syncService.ChangePassphraseAsync(oldPassphrase, newPassphrase);
        IsSyncing = false;
        if (error != null) { SyncError = error; return; }

        IsChangingPassphrase = false;

        ShowRecoveryKit();
    }

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
        var error = await _syncService.SignInAsync(SyncEmail.Trim(), password);
        IsSyncing = false;
        if (error != null) { SyncError = error; return; }
    }

    public async Task SignUpAsync(string password)
    {
        if (string.IsNullOrWhiteSpace(SyncEmail) || string.IsNullOrWhiteSpace(password)) return;
        IsSyncing = true;
        SyncError = null;
        var msg = await _syncService.SignUpAsync(SyncEmail.Trim(), password);
        IsSyncing = false;
        if (msg != null) SyncError = msg;
    }

    public async Task SignOutAsync()
    {
        await _syncService.SignOutAsync();
        SyncError = null;
    }

    public ICommand SignOutCommand => new RelayCommand(async _ => await SignOutAsync());

    public async Task SignInWithGitHubAsync()
    {
        IsSyncing = true;
        SyncError = null;
        var (url, error) = await _syncService.GetGitHubSignInUrlAsync();
        IsSyncing = false;
        if (error != null) { SyncError = error; return; }
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url!));
    }

    private void OnSyncStateChanged()
    {
        _dispatcherQueue.TryEnqueue(async () =>
        {
            bool isNowSignedIn = _syncService.IsSignedIn;
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
            if (_syncService.LastAuthError is { } authError)
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

    public bool IsMfaChallengePending => _syncService.IsMfaChallengePending;

    public async Task SubmitMfaChallengeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        IsSyncing = true;
        SyncError = null;
        var error = await _syncService.SubmitMfaChallengeAsync(code.Trim());
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
        IsMfaEnrolled = await _syncService.GetVerifiedMfaFactorAsync() != null;
    }

    public async Task StartMfaEnrollmentAsync()
    {
        SyncError = null;
        var (factor, error) = await _syncService.EnrollMfaAsync();
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

        var error = await _syncService.VerifyMfaAsync(_pendingFactor.Id, code);
        if (error != null) { SyncError = error; return; }

        _pendingFactor = null;
        OnPropertyChanged(nameof(IsMfaEnrolling));
        OnPropertyChanged(nameof(MfaSecret));
        OnPropertyChanged(nameof(MfaQrSvg));
        await RefreshMfaStateAsync();

        // Generated immediately after verifying, never later: this is the one moment the
        // session is known to be aal2 and the user is already thinking about lockout.
        var (codes, codesError) = await _syncService.GenerateRecoveryCodesAsync();
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
        var error = await _syncService.RedeemRecoveryCodeAsync(code);
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
        await _syncService.UnenrollMfaAsync(_pendingFactor.Id);
        _pendingFactor = null;
        OnPropertyChanged(nameof(IsMfaEnrolling));
        OnPropertyChanged(nameof(MfaSecret));
        OnPropertyChanged(nameof(MfaQrSvg));
        OnPropertyChanged(nameof(CanEnrollMfa));
    }

    public async Task DisableMfaAsync()
    {
        var factor = await _syncService.GetVerifiedMfaFactorAsync();
        if (factor == null) return;

        var error = await _syncService.UnenrollMfaAsync(factor.Id);
        if (error != null) { SyncError = error; return; }
        await RefreshMfaStateAsync();
    }

    private void ForgetPassphrase()
    {
        _syncService.ClearPassphrase();
        OnPropertyChanged(nameof(IsPassphraseSet));
        OnPropertyChanged(nameof(IsSignedInWithoutPassphrase));
    }

    private async Task CheckAndHandleConflictAsync()
    {
        IsSyncing = true;
        SyncError = null;
        try
        {
            var conflict = await _syncService.CheckConflictAsync();
            if (conflict == null)
            {
                // No conflict: pull if there's newer data on the server, then start the timer.
                SyncError = await _syncService.PullIfNewerAsync();
                // A stored passphrase that cannot decrypt the row is worse than none: it
                // hides the entry card forever. Discard it so the user can try again.
                if (SyncError == Strings.Sync_Error_WrongPassphrase) ForgetPassphrase();
                _syncService.StartAutoSync();
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
                await _syncService.ResolveConflictAsync(SyncConflictResolution.Merge);
                _syncService.StartAutoSync();
                OnPropertyChanged(nameof(SyncLastSyncedText));
            }
        }
        catch { _syncService.StartAutoSync(); }
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
            SyncError = await _syncService.ResolveConflictAsync(resolution);
        }
        finally
        {
            IsSyncing = false;
            OnPropertyChanged(nameof(SyncLastSyncedText));
            _syncService.StartAutoSync();
        }
    }


    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
