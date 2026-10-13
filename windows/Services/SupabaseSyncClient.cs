using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Hatch.Services;

// Only the Auth and user_data endpoints Hatch uses. JSON contracts are generated,
// and refreshes are serialized because refresh tokens rotate after every use.
internal sealed class SupabaseSyncClient
{
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler { AllowAutoRedirect = false });
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string _key;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private CancellationTokenSource? _refreshCts;

    internal SupabaseSyncClient(string url, string key, HttpClient? http = null)
    {
        _baseUri = new Uri(url.TrimEnd('/') + "/");
        _key = key;
        _http = http ?? SharedHttp;
    }

    internal SyncSession? Session { get; private set; }
    internal Func<SyncSession, Task>? SessionRefreshed { get; set; }
    internal Action? SessionRejected { get; set; }

    internal async Task RestoreSessionAsync(string access, string refresh)
    {
        await _sessionGate.WaitAsync();
        try
        {
            SyncSession restored;
            try
            {
                using var claims = ReadClaims(access);
                _ = ExpiresAt(access);
                var userId = claims.RootElement.GetProperty("sub").GetString();
                if (string.IsNullOrEmpty(userId)) throw new FormatException("Invalid saved session.");
                restored = new SyncSession
                {
                    AccessToken = access, RefreshToken = refresh,
                    User = new SyncUser
                    {
                        Id = userId,
                        Email = claims.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null
                    }
                };
            }
            catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException)
            {
                throw new FormatException("Invalid saved session.", ex);
            }
            SetSession(restored);
            await RefreshIfNeededAsync();
            await UpdateCurrentUserAsync();
        }
        catch
        {
            // Startup needs a validated user/factor snapshot. Keep saved credentials
            // through a transient failure, but do not expose an incomplete live account.
            SetSession(null);
            throw;
        }
        finally { _sessionGate.Release(); }
    }

    internal Task<SyncSession?> SignInAsync(string email, string password) =>
        AuthenticateAsync("password", new JsonObject { ["email"] = email, ["password"] = password });

    internal Task<SyncSession?> SignUpAsync(string email, string password) =>
        AuthenticateAsync(null, new JsonObject { ["email"] = email, ["password"] = password });

    internal Task<SyncSession?> ExchangeCodeAsync(string verifier, string code) =>
        AuthenticateAsync("pkce", new JsonObject { ["auth_code"] = code, ["code_verifier"] = verifier });

    private async Task<SyncSession?> AuthenticateAsync(string? grant, JsonObject body)
    {
        await _sessionGate.WaitAsync();
        try
        {
            var path = grant == null ? "auth/v1/signup" : "auth/v1/token?grant_type=" + grant;
            var session = await SendAsync(HttpMethod.Post, path, SyncApiJsonContext.Default.SyncSession, body.ToJsonString());
            if (string.IsNullOrEmpty(session?.AccessToken))
            {
                if (grant == null) return null; // Sign-up may require email confirmation.
                throw new JsonException("The server returned an incomplete session.");
            }
            SetSession(session);
            return session;
        }
        finally { _sessionGate.Release(); }
    }

    internal async Task SignOutAsync()
    {
        await _sessionGate.WaitAsync();
        string? token;
        try
        {
            token = Session?.AccessToken;
            SetSession(null); // Clear locally even if the remote logout fails.
        }
        finally { _sessionGate.Release(); }
        if (token != null)
            await SendAsync(HttpMethod.Post, "auth/v1/logout?scope=global", "{}", token);
    }

    internal static (string Url, string Verifier) GitHubSignInUrl(string url)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return ($"{url.TrimEnd('/')}/auth/v1/authorize?provider=github&redirect_to=" +
                Uri.EscapeDataString("hatch://auth-callback") +
                $"&code_challenge={challenge}&code_challenge_method=s256", verifier);
    }

    internal async Task<SyncFactor?> EnrollAsync(string friendlyName) =>
        await SendAsync(HttpMethod.Post, "auth/v1/factors", SyncApiJsonContext.Default.SyncFactor,
            new JsonObject { ["factor_type"] = "totp", ["issuer"] = "Hatch", ["friendly_name"] = friendlyName }.ToJsonString(),
            await AccessTokenAsync());

    internal async Task<SyncSession?> VerifyMfaAsync(string factorId, string code)
    {
        await _sessionGate.WaitAsync();
        try
        {
            await RefreshIfNeededAsync();
            var token = Session?.AccessToken ?? throw new InvalidOperationException("Not signed in.");
            var path = "auth/v1/factors/" + Uri.EscapeDataString(factorId);
            var challengeJson = await SendAsync(HttpMethod.Post, path + "/challenge", "{}", token);
            using var challenge = JsonDocument.Parse(challengeJson);
            var body = new JsonObject { ["code"] = code, ["challenge_id"] = challenge.RootElement.GetProperty("id").GetString() };
            var session = await SendAsync(HttpMethod.Post, path + "/verify", SyncApiJsonContext.Default.SyncSession,
                body.ToJsonString(), token);
            if (string.IsNullOrEmpty(session?.AccessToken)) throw new JsonException("The server returned an incomplete session.");
            SetSession(session);
            return session;
        }
        finally { _sessionGate.Release(); }
    }

    internal async Task UnenrollAsync(string factorId)
    {
        await SendAsync(HttpMethod.Delete, "auth/v1/factors/" + Uri.EscapeDataString(factorId), token: await AccessTokenAsync());
        await UpdateUserAsync();
    }

    internal SyncFactor? VerifiedTotp => Session?.User?.Factors?.FirstOrDefault(f => f.FactorType == "totp" && f.Status == "verified");

    // Claims are used for UI/refresh scheduling only. Supabase validates every request,
    // including the user's identity, signature and aal2 requirement through RLS.
    internal (string? Current, string? Next) AssuranceLevel()
    {
        using var claims = ReadClaims(Session?.AccessToken ?? throw new InvalidOperationException("Not signed in."));
        var current = claims.RootElement.TryGetProperty("aal", out var aal) ? aal.GetString() : null;
        return (current, Session?.User?.Factors?.Any(f => f.Status == "verified") == true ? "aal2" : current);
    }

    private async Task UpdateUserAsync()
    {
        await _sessionGate.WaitAsync();
        try
        {
            await RefreshIfNeededAsync();
            await UpdateCurrentUserAsync();
        }
        finally { _sessionGate.Release(); }
    }

    // Called under the session gate, preserving the previous user if a response is malformed.
    private async Task UpdateCurrentUserAsync()
    {
        var session = Session ?? throw new InvalidOperationException("Not signed in.");
        var user = await SendAsync(HttpMethod.Get, "auth/v1/user", SyncApiJsonContext.Default.SyncUser, token: session.AccessToken);
        if (string.IsNullOrEmpty(user?.Id) || user.Id != session.User?.Id)
            throw new JsonException("The server returned an incomplete user.");
        session.User = user;
    }

    internal async Task<UserDataRow?> GetRowAsync(bool metadataOnly = false)
    {
        var session = await AccessSessionAsync();
        var userId = session.User?.Id ?? throw new InvalidOperationException("No user ID.");
        var path = "rest/v1/user_data?select=" + (metadataOnly ? "updated_at" : "*") +
                   "&user_id=eq." + Uri.EscapeDataString(userId);
        var rows = await SendAsync(HttpMethod.Get, path, SyncApiJsonContext.Default.UserDataRowArray, token: session.AccessToken);
        return rows?.FirstOrDefault();
    }

    internal async Task UpsertAsync(UserDataRow row)
    {
        var session = await AccessSessionAsync();
        if (row.UserId != session.User?.Id) throw new InvalidOperationException("The account changed. Try syncing again.");
        await SendAsync(HttpMethod.Post, "rest/v1/user_data", JsonSerializer.Serialize(row, SyncApiJsonContext.Default.UserDataRow),
            session.AccessToken, "resolution=merge-duplicates,return=minimal");
    }

    internal Task<string[]?> GenerateRecoveryCodesAsync() =>
        RpcAsync("generate_mfa_recovery_codes", "{}", SyncApiJsonContext.Default.StringArray);

    internal async Task<bool> RedeemRecoveryCodeAsync(string code)
    {
        await _sessionGate.WaitAsync();
        try
        {
            await RefreshIfNeededAsync();
            var session = Session ?? throw new InvalidOperationException("Not signed in.");
            var accepted = await SendAsync(HttpMethod.Post, "rest/v1/rpc/redeem_mfa_recovery_code",
                SyncApiJsonContext.Default.Boolean, new JsonObject { ["code"] = code }.ToJsonString(), session.AccessToken);
            // The existing RPC removes all factors; update the same account immediately.
            if (accepted && session.User != null) session.User.Factors = [];
            return accepted;
        }
        finally { _sessionGate.Release(); }
    }

    private async Task<T?> RpcAsync<T>(string name, string body, JsonTypeInfo<T> typeInfo) =>
        await SendAsync(HttpMethod.Post, "rest/v1/rpc/" + name, typeInfo, body, await AccessTokenAsync());

    private async Task<string> AccessTokenAsync() => (await AccessSessionAsync()).AccessToken!;

    private async Task<SyncSession> AccessSessionAsync()
    {
        await _sessionGate.WaitAsync();
        try
        {
            await RefreshIfNeededAsync();
            return Session ?? throw new InvalidOperationException("Not signed in.");
        }
        finally { _sessionGate.Release(); }
    }

    // Called only while holding _sessionGate: no concurrent refresh, MFA or sign-out
    // can reuse an old refresh token or restore the account after local sign-out.
    private async Task RefreshIfNeededAsync()
    {
        if (Session?.AccessToken == null || ExpiresAt(Session.AccessToken) > DateTimeOffset.UtcNow.AddSeconds(60)) return;
        try
        {
            var body = new JsonObject { ["refresh_token"] = Session.RefreshToken };
            var session = await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token",
                SyncApiJsonContext.Default.SyncSession, body.ToJsonString());
            if (string.IsNullOrEmpty(session?.AccessToken) || string.IsNullOrEmpty(session.RefreshToken))
                throw new InvalidOperationException("The server returned an incomplete session.");
            // Preserve factor state if the response omits user details.
            session.User ??= Session.User;
            ValidateSession(session);
            Session = session;
            if (SessionRefreshed != null) await SessionRefreshed(session);
        }
        catch (SyncApiException ex) when (ex.IsInvalidRefreshToken)
        {
            SetSession(null);
            SessionRejected?.Invoke();
            throw;
        }
    }

    private void SetSession(SyncSession? session)
    {
        if (session != null) ValidateSession(session);
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;
        Session = session;
        if (session?.AccessToken == null) return;
        _refreshCts = new CancellationTokenSource();
        _ = RefreshLoopAsync(_refreshCts.Token);
    }

    private static void ValidateSession(SyncSession session)
    {
        if (string.IsNullOrEmpty(session.AccessToken) || string.IsNullOrEmpty(session.RefreshToken) || string.IsNullOrEmpty(session.User?.Id))
            throw new JsonException("The server returned an incomplete session.");
        try { _ = ExpiresAt(session.AccessToken); }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            throw new JsonException("The server returned an invalid session.", ex);
        }
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && Session?.AccessToken != null)
            {
                var delay = ExpiresAt(Session.AccessToken) - DateTimeOffset.UtcNow - TimeSpan.FromSeconds(60);
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(30), ct);
                await _sessionGate.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    await RefreshIfNeededAsync();
                }
                catch (HttpRequestException) { /* Offline/5xx keeps the saved session for retry. */ }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested) { /* HTTP timeout; retry. */ }
                catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or IOException)
                {
                    // A malformed response or local persistence error must not stop later refresh attempts.
                }
                finally { _sessionGate.Release(); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, JsonTypeInfo<T> typeInfo, string? body = null, string? token = null) =>
        JsonSerializer.Deserialize(await SendAsync(method, path, body, token), typeInfo);

    private async Task<string> SendAsync(HttpMethod method, string path, string? body = null, string? token = null, string? prefer = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        request.Headers.Add("apikey", _key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? _key);
        if (prefer != null) request.Headers.Add("Prefer", prefer);
        if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw SyncApiException.FromResponse(response.StatusCode, json);
        return json;
    }

    private static DateTimeOffset ExpiresAt(string token)
    {
        using var claims = ReadClaims(token);
        return DateTimeOffset.FromUnixTimeSeconds(claims.RootElement.GetProperty("exp").GetInt64());
    }

    private static JsonDocument ReadClaims(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) throw new FormatException("Invalid saved session.");
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class SyncApiException(string message, HttpStatusCode status, string? code) : HttpRequestException(message, null, status)
{
    internal bool IsInvalidRefreshToken => StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized &&
        code is "refresh_token_not_found" or "refresh_token_already_used" or "session_not_found" or "session_expired";

    internal static SyncApiException FromResponse(HttpStatusCode status, string json)
    {
        string? message = null, code = null;
        try
        {
            using var error = JsonDocument.Parse(json);
            if (error.RootElement.ValueKind != JsonValueKind.Object)
                return new SyncApiException($"Sync request failed ({(int)status}).", status, null);
            foreach (var key in new[] { "msg", "message", "error_description", "error" })
                if (error.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                { message = value.GetString(); break; }
            if (error.RootElement.TryGetProperty("error_code", out var errorCode) && errorCode.ValueKind == JsonValueKind.String)
                code = errorCode.GetString();
        }
        catch (JsonException) { }
        return new SyncApiException(message ?? $"Sync request failed ({(int)status}).", status, code);
    }
}
