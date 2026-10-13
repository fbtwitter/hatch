using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.Services;

// Owns the optional Sync account session and its authentication state.
public sealed class SyncAccountService
{
    private static readonly string SupabaseUrl = SyncDecisions.NormalizeSupabaseUrl(Secrets.SupabaseUrl);
    private const string SupabaseKey = Secrets.SupabaseKey;

    private readonly SettingsService _settings;
    private SupabaseSyncClient? _client;
    private string? _pkceVerifier;

    public SyncAccountService(SettingsService settings) => _settings = settings;

    // SyncService uses the same authenticated client for the user_data row.
    internal SupabaseSyncClient? Client => _client;

    public string? LastAuthError { get; private set; }
    public bool IsSignedIn => _client?.Session != null;
    public string? UserEmail => _client?.Session?.User?.Email;
    public bool HasPassphrase => SyncPassphraseStore.Load().Passphrase != null;
    public bool IsMfaChallengePending { get; private set; }

    public event Action? StateChanged;
    // SyncService must stop its timer even when the remote sign-out request fails.
    public event Action? SigningOut;

    public void SetPassphrase(string passphrase)
    {
        SyncPassphraseStore.Save(passphrase);
        StateChanged?.Invoke();
    }

    public void ClearPassphrase()
    {
        SyncPassphraseStore.Clear();
        StateChanged?.Invoke();
    }

    public string? PassphraseForRecoveryKit => SyncPassphraseStore.Load().Passphrase;

    public async Task InitializeAsync()
    {
        _client = new SupabaseSyncClient(SupabaseUrl, SupabaseKey);
        _client.SessionRefreshed = async session =>
        {
            await PersistSessionAsync(session);
            await RefreshMfaChallengeStateAsync();
            StateChanged?.Invoke();
        };
        _client.SessionRejected = () =>
        {
            SigningOut?.Invoke();
            IsMfaChallengePending = false;
            ClearTokens();
            _settings.SaveDebounced();
            StateChanged?.Invoke();
        };
        await RestoreSessionAsync();
    }

    private async Task RestoreSessionAsync()
    {
        var (access, refresh) = SyncTokenStore.Load(_settings);
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) return;
        try
        {
            await _client!.RestoreSessionAsync(access, refresh);
            await RefreshMfaChallengeStateAsync();
            StateChanged?.Invoke();
        }
        catch (FormatException)
        {
            ClearTokens();
            _settings.SaveDebounced();
        }
        catch
        {
        }
    }

    public async Task<string?> SignInAsync(string email, string password)
    {
        if (_client == null) return Strings.Sync_Error_NotReady;
        try
        {
            var session = await _client.SignInAsync(email, password);
            if (session?.AccessToken == null) return Strings.Sync_Error_SignInFailed;
            await PersistSessionAsync(session);
            await RefreshMfaChallengeStateAsync();
            StateChanged?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task<string?> SignUpAsync(string email, string password)
    {
        if (_client == null) return Strings.Sync_Error_NotReady;
        try
        {
            var session = await _client.SignUpAsync(email, password);
            if (session?.AccessToken != null)
            {
                await PersistSessionAsync(session);
                StateChanged?.Invoke();
                return null;
            }
            // No session means email confirmation is still required.
            return Strings.Sync_Info_ConfirmEmail;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task SignOutAsync()
    {
        SigningOut?.Invoke();
        try { await (_client?.SignOutAsync() ?? Task.CompletedTask); } catch { }
        _pkceVerifier = null;
        IsMfaChallengePending = false;
        SyncPassphraseStore.Clear();
        ClearTokens();
        await _settings.SaveAsync();
        StateChanged?.Invoke();
    }

    public Task<(string? Url, string? Error)> GetGitHubSignInUrlAsync()
    {
        if (_client == null) return Task.FromResult<(string?, string?)>((null, Strings.Sync_Error_NotReady));
        var (url, verifier) = SupabaseSyncClient.GitHubSignInUrl(SupabaseUrl);
        _pkceVerifier = verifier;
        return Task.FromResult<(string?, string?)>((url, null));
    }
    public async Task<(MfaFactorInfo? Factor, string? Error)> EnrollMfaAsync()
    {
        if (_client == null) return (null, Strings.Sync_Error_NotReady);
        if (!IsSignedIn) return (null, Strings.Sync_Error_NotSignedIn);
        try
        {
            var result = await _client.EnrollAsync($"Hatch {DateTime.Now:yyyy-MM-dd HH:mm}");

            if (result?.Id == null || result.Totp == null)
                return (null, "Could not start authenticator setup.");

            return (new MfaFactorInfo(
                result.Id, result.Totp.Secret, result.Totp.QrCode, result.Totp.Uri), null);
        }
        catch (Exception ex) { return (null, ex.Message); }
    }

    public async Task<string?> VerifyMfaAsync(string factorId, string code)
    {
        if (_client == null) return Strings.Sync_Error_NotReady;
        try
        {
            var session = await _client.VerifyMfaAsync(factorId, code.Trim());
            if (session == null) return "That code was not accepted.";

            await PersistSessionAsync(session);
            StateChanged?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task<string?> UnenrollMfaAsync(string factorId)
    {
        if (_client == null) return Strings.Sync_Error_NotReady;
        try
        {
            await _client.UnenrollAsync(factorId);
            await RefreshMfaChallengeStateAsync();
            StateChanged?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public Task<MfaFactorInfo?> GetVerifiedMfaFactorAsync()
    {
        var verified = _client?.VerifiedTotp;
        return Task.FromResult(verified == null ? null : new MfaFactorInfo(verified.Id, null, null, null));
    }

    public Task RefreshMfaChallengeStateAsync()
    {
        bool pending = false;
        if (_client != null && IsSignedIn)
        {
            try
            {
                // NextLevel reaches aal2 when a verified factor exists, without another network request.
                var aal = _client.AssuranceLevel();
                pending = SyncDecisions.IsMfaChallengePending(
                    aal.Current, aal.Next);
            }
            catch
            {
                // A failed local check must not lock users out; the server still enforces aal2.
            }
        }

        if (pending == IsMfaChallengePending) return Task.CompletedTask;
        IsMfaChallengePending = pending;
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public async Task<(string[]? Codes, string? Error)> GenerateRecoveryCodesAsync()
    {
        if (_client == null) return (null, Strings.Sync_Error_NotReady);
        if (!IsSignedIn) return (null, Strings.Sync_Error_NotSignedIn);
        try
        {
            var codes = await _client.GenerateRecoveryCodesAsync();
            return codes is { Length: > 0 } ? (codes, null) : (null, "Could not create recovery codes.");
        }
        catch (Exception ex) { return (null, ex.Message); }
    }

    public async Task<string?> RedeemRecoveryCodeAsync(string code)
    {
        if (_client == null) return Strings.Sync_Error_NotReady;
        if (!IsSignedIn) return Strings.Sync_Error_NotSignedIn;
        try
        {
            var accepted = await _client.RedeemRecoveryCodeAsync(code.Trim());
            if (!accepted) return Strings.Sync_Error_BadRecoveryCode;

            // GoTrue cannot mint an aal2 token outside its challenge flow, so recovery removes the factor.
            await RefreshMfaChallengeStateAsync();
            StateChanged?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task<string?> SubmitMfaChallengeAsync(string code)
    {
        var factor = await GetVerifiedMfaFactorAsync();
        if (factor == null) return Strings.Sync_Error_NoMfaFactor;

        var error = await VerifyMfaAsync(factor.Id, code);
        if (error != null) return error;

        await RefreshMfaChallengeStateAsync();
        return null;
    }

    public async Task HandleOAuthCallbackAsync(Uri callbackUri)
    {
        if (_client == null) return;
        LastAuthError = null;
        try
        {
            // PKCE returns a single-use code in the query; its verifier stays in memory through the browser round trip.
            var p = SyncDecisions.ParseQueryString(callbackUri.Query.TrimStart('?'));
            var providerError = p.GetValueOrDefault("error_description")
                             ?? p.GetValueOrDefault("error");
            if (!string.IsNullOrEmpty(providerError))
            {
                LastAuthError = providerError;
                StateChanged?.Invoke();
                return;
            }

            var code = p.GetValueOrDefault("code");
            if (string.IsNullOrEmpty(code))
            {
                LastAuthError = "Sign-in callback carried no authorization code.";
                StateChanged?.Invoke();
                return;
            }

            if (string.IsNullOrEmpty(_pkceVerifier))
            {
                LastAuthError = "Sign-in expired. Start the GitHub sign-in again.";
                StateChanged?.Invoke();
                return;
            }

            var session = await _client.ExchangeCodeAsync(_pkceVerifier, code);
            _pkceVerifier = null;

            if (session != null)
            {
                await PersistSessionAsync(session);
                await RefreshMfaChallengeStateAsync();
            }
            else LastAuthError = "Could not exchange the sign-in code for a session.";

            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            LastAuthError = ex.Message;
            StateChanged?.Invoke();
        }
    }

    private async Task PersistSessionAsync(SyncSession session)
    {
        SyncTokenStore.Save(session.AccessToken, session.RefreshToken);
        _settings.Current.SyncUserEmail = _client?.Session?.User?.Email;
        await _settings.SaveAsync();
    }

    private void ClearTokens()
    {
        SyncTokenStore.Clear();
        _settings.Current.SyncUserEmail = null;
        _settings.Current.LastSyncedAt = null;
    }
}
