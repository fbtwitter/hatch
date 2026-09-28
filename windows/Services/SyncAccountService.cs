using Hatch.Helpers;
using Hatch.Models;
using SupabaseClient = Supabase.Client;
using GotrueConstants = Supabase.Gotrue.Constants;
using GotrueSignInOptions = Supabase.Gotrue.SignInOptions;
using Supabase.Gotrue.Mfa;

namespace Hatch.Services;

// Owns the optional Sync account session and its authentication state.
public sealed class SyncAccountService
{
    private static readonly string SupabaseUrl = SyncDecisions.NormalizeSupabaseUrl(Secrets.SupabaseUrl);
    private const string SupabaseKey = Secrets.SupabaseKey;

    private readonly SettingsService _settings;
    private SupabaseClient? _client;
    private string? _pkceVerifier;

    public SyncAccountService(SettingsService settings) => _settings = settings;

    // SyncService uses the same authenticated client for the user_data row.
    internal SupabaseClient? Client => _client;

    public string? LastAuthError { get; private set; }
    public bool IsSignedIn => _client?.Auth?.CurrentSession != null;
    public string? UserEmail => _client?.Auth?.CurrentUser?.Email;
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
        var options = new Supabase.SupabaseOptions { AutoRefreshToken = true };
        _client = new SupabaseClient(SupabaseUrl, SupabaseKey, options);
        _client.Auth.AddStateChangedListener(OnAuthStateChanged);
        await _client.InitializeAsync();
        await RestoreSessionAsync();
    }

    private async void OnAuthStateChanged(object? sender, GotrueConstants.AuthState state)
    {
        if (state != GotrueConstants.AuthState.TokenRefreshed) return;
        var session = _client?.Auth.CurrentSession;
        if (session?.AccessToken == null || session.RefreshToken == null) return;
        await PersistSessionAsync(session);
    }

    private async Task RestoreSessionAsync()
    {
        var (access, refresh) = SyncTokenStore.Load(_settings);
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) return;
        try
        {
            await _client!.Auth.SetSession(access, refresh);
            await RefreshMfaChallengeStateAsync();
            StateChanged?.Invoke();
        }
        catch (Supabase.Gotrue.Exceptions.GotrueException ex)
            when (ex.Reason != Supabase.Gotrue.Exceptions.FailureHint.Reason.Offline)
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
            var session = await _client.Auth.SignIn(email, password);
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
            var session = await _client.Auth.SignUp(email, password);
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
        try { await (_client?.Auth?.SignOut() ?? Task.CompletedTask); } catch { }
        SigningOut?.Invoke();
        IsMfaChallengePending = false;
        SyncPassphraseStore.Clear();
        ClearTokens();
        await _settings.SaveAsync();
        StateChanged?.Invoke();
    }

    public async Task<(string? Url, string? Error)> GetGitHubSignInUrlAsync()
    {
        if (_client == null) return (null, Strings.Sync_Error_NotReady);
        try
        {
            var state = await _client.Auth.SignIn(
                GotrueConstants.Provider.Github,
                new GotrueSignInOptions
                {
                    RedirectTo = "hatch://auth-callback",
                    // PKCE keeps a hijacked hatch:// redirect from exposing usable session tokens.
                    FlowType = GotrueConstants.OAuthFlowType.PKCE
                });

            var url = state?.Uri?.ToString();
            if (string.IsNullOrEmpty(url)) return (null, "GitHub sign-in returned no URL.");

            // GoTrue's client-side state breaks its server-tracked OAuth state when forwarded to GitHub.
            url = RemoveQueryParam(url, "state");
            _pkceVerifier = state!.PKCEVerifier;
            return (url, null);
        }
        catch (Exception ex) { return (null, ex.Message); }
    }

    private static string RemoveQueryParam(string url, string paramName)
    {
        var uri = new Uri(url);
        var query = SyncDecisions.ParseQueryString(uri.Query.TrimStart('?'))
            .Where(kv => kv.Key != paramName)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");
        return new UriBuilder(uri) { Query = string.Join("&", query) }.Uri.ToString();
    }

    public async Task<(MfaFactorInfo? Factor, string? Error)> EnrollMfaAsync()
    {
        if (_client == null) return (null, Strings.Sync_Error_NotReady);
        if (!IsSignedIn) return (null, Strings.Sync_Error_NotSignedIn);
        try
        {
            var result = await _client.Auth.Enroll(new MfaEnrollParams
            {
                FactorType = "totp",
                Issuer = "Hatch",
                FriendlyName = $"Hatch {DateTime.Now:yyyy-MM-dd HH:mm}"
            });

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
            var session = await _client.Auth.ChallengeAndVerify(new MfaChallengeAndVerifyParams
            {
                FactorId = factorId,
                Code = code.Trim()
            });
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
            await _client.Auth.Unenroll(new MfaUnenrollParams { FactorId = factorId });
            await RefreshMfaChallengeStateAsync();
            StateChanged?.Invoke();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public async Task<MfaFactorInfo?> GetVerifiedMfaFactorAsync()
    {
        if (_client == null || !IsSignedIn) return null;
        try
        {
            var factors = await _client.Auth.ListFactors();
            var verified = factors?.Totp?.FirstOrDefault(f => f.Status == "verified");
            return verified == null ? null : new MfaFactorInfo(verified.Id, null, null, null);
        }
        catch { return null; }
    }

    public async Task RefreshMfaChallengeStateAsync()
    {
        bool pending = false;
        if (_client != null && IsSignedIn)
        {
            try
            {
                // NextLevel reaches aal2 when a verified factor exists, without another network request.
                var aal = await _client.Auth.GetAuthenticatorAssuranceLevel();
                pending = SyncDecisions.IsMfaChallengePending(
                    aal?.CurrentLevel?.ToString(), aal?.NextLevel?.ToString());
            }
            catch
            {
                // A failed local check must not lock users out; the server still enforces aal2.
            }
        }

        if (pending == IsMfaChallengePending) return;
        IsMfaChallengePending = pending;
        StateChanged?.Invoke();
    }

    public async Task<(string[]? Codes, string? Error)> GenerateRecoveryCodesAsync()
    {
        if (_client == null) return (null, Strings.Sync_Error_NotReady);
        if (!IsSignedIn) return (null, Strings.Sync_Error_NotSignedIn);
        try
        {
            var codes = await _client.Rpc<string[]>("generate_mfa_recovery_codes", null);
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
            var accepted = await _client.Rpc<bool>(
                "redeem_mfa_recovery_code", new Dictionary<string, object> { ["code"] = code.Trim() });
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

            var session = await _client.Auth.ExchangeCodeForSession(_pkceVerifier, code);
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

    private async Task PersistSessionAsync(Supabase.Gotrue.Session session)
    {
        SyncTokenStore.Save(session.AccessToken, session.RefreshToken);
        _settings.Current.SyncUserEmail = _client?.Auth.CurrentUser?.Email;
        await _settings.SaveAsync();
    }

    private void ClearTokens()
    {
        SyncTokenStore.Clear();
        _settings.Current.SyncUserEmail = null;
        _settings.Current.LastSyncedAt = null;
    }
}
