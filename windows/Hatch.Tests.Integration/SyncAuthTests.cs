using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hatch.Tests.Integration;

[TestClass]
public sealed class SyncAuthTests
{
    private readonly ConcurrentQueue<Request> _requests = new();
    private Func<Request, (HttpStatusCode Status, string Body)> _respond = _ => (HttpStatusCode.OK, "{}");
    private HttpClient _http = null!;
    private SupabaseSyncClient _client = null!;

    [TestInitialize]
    public void SetUp()
    {
        _http = new HttpClient(new Handler(async request =>
        {
            await Task.Yield();
            var captured = new Request(request.Method.Method, request.RequestUri!.PathAndQuery,
                request.Content == null ? "" : await request.Content.ReadAsStringAsync(),
                request.Headers.Authorization?.ToString(), request.Headers.GetValues("apikey").Single());
            _requests.Enqueue(captured);
            var response = _respond(captured);
            return new HttpResponseMessage(response.Status) { Content = new StringContent(response.Body) };
        }));
        _client = new SupabaseSyncClient("https://fixture.invalid", "synthetic-key", _http);
    }

    [TestCleanup]
    public async Task TearDown()
    {
        _respond = _ => (HttpStatusCode.OK, "{}");
        await _client.SignOutAsync();
        _http.Dispose();
    }

    [TestMethod]
    public async Task PasswordSignIn_UsesGeneratedSessionAndExpectedHeaders()
    {
        _respond = _ => (HttpStatusCode.OK, Session());
        var session = await _client.SignInAsync("person@example.test", "unicode-é-password");
        Assert.AreEqual("user-1", session!.User!.Id);
        var request = _requests.Single();
        Assert.AreEqual("POST", request.Method);
        Assert.AreEqual("/auth/v1/token?grant_type=password", request.Path);
        Assert.AreEqual("Bearer synthetic-key", request.Authorization);
        Assert.AreEqual("synthetic-key", request.ApiKey);
        using var body = JsonDocument.Parse(request.Body);
        Assert.AreEqual("person@example.test", body.RootElement.GetProperty("email").GetString());
        Assert.AreEqual("unicode-é-password", body.RootElement.GetProperty("password").GetString());
    }

    [TestMethod]
    public async Task SignUp_ConfirmationDoesNotCreateAnIncompleteSession()
    {
        _respond = _ => (HttpStatusCode.OK, "{\"id\":\"user-1\",\"email\":\"person@example.test\"}");
        Assert.IsNull(await _client.SignUpAsync("person@example.test", "password"));
        Assert.IsNull(_client.Session);
        Assert.AreEqual("/auth/v1/signup", _requests.Single().Path);
    }

    [TestMethod]
    public async Task Pkce_UsesMatchingS256ChallengeAndExchangesCode()
    {
        var (url, verifier) = SupabaseSyncClient.GitHubSignInUrl("https://fixture.invalid/");
        var query = new Uri(url).Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
        Assert.AreEqual("github", query["provider"]);
        Assert.AreEqual("hatch://auth-callback", query["redirect_to"]);
        Assert.AreEqual("s256", query["code_challenge_method"]);
        Assert.AreEqual(43, verifier.Length);
        Assert.AreEqual(Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), query["code_challenge"]);
        _respond = _ => (HttpStatusCode.OK, Session());
        await _client.ExchangeCodeAsync(verifier, "synthetic-code");
        using var body = JsonDocument.Parse(_requests.Single().Body);
        Assert.AreEqual(verifier, body.RootElement.GetProperty("code_verifier").GetString());
        Assert.AreEqual("synthetic-code", body.RootElement.GetProperty("auth_code").GetString());
        Assert.AreEqual("/auth/v1/token?grant_type=pkce", _requests.Single().Path);
    }

    [TestMethod]
    public async Task Restore_RefreshesExpiredSessionAndValidatesUserWithNewToken()
    {
        int rotations = 0;
        _client.SessionRefreshed = session => { rotations++; Assert.AreEqual("new-refresh", session.RefreshToken); return Task.CompletedTask; };
        _respond = request => (HttpStatusCode.OK, request.Path.StartsWith("/auth/v1/token")
            ? Session(refresh: "new-refresh") : "{\"id\":\"user-1\",\"email\":\"person@example.test\"}");
        await _client.RestoreSessionAsync(Token(expired: true), "old-refresh");
        Assert.AreEqual(1, rotations);
        Assert.AreEqual(2, _requests.Count);
        using var body = JsonDocument.Parse(_requests.ElementAt(0).Body);
        Assert.AreEqual("old-refresh", body.RootElement.GetProperty("refresh_token").GetString());
        Assert.AreEqual("/auth/v1/user", _requests.ElementAt(1).Path);
        Assert.AreEqual("Bearer " + _client.Session!.AccessToken, _requests.ElementAt(1).Authorization);
    }

    [TestMethod]
    public async Task Restore_MalformedTokenNeverReachesNetwork()
    {
        await Assert.ThrowsAsync<FormatException>(() => _client.RestoreSessionAsync("not-a-jwt", "refresh"));
        Assert.IsNull(_client.Session);
        Assert.AreEqual(0, _requests.Count);
    }

    [TestMethod]
    public async Task Restore_MismatchedUserDoesNotExposeSession()
    {
        _respond = _ => (HttpStatusCode.OK, "{\"id\":\"another-user\"}");
        await Assert.ThrowsAsync<JsonException>(() => _client.RestoreSessionAsync(Token(), "refresh"));
        Assert.IsNull(_client.Session);
    }

    [TestMethod]
    public async Task Mfa_EnrollChallengeAndVerifyPreserveContracts()
    {
        await SignIn(factors: true);
        Assert.AreEqual(("aal1", "aal2"), _client.AssuranceLevel());
        _respond = request => (HttpStatusCode.OK, request.Path switch
        {
            "/auth/v1/factors" => "{\"id\":\"factor-1\",\"factor_type\":\"totp\",\"totp\":{\"secret\":\"fixture-secret\",\"qr_code\":\"fixture-svg\",\"uri\":\"otpauth://totp/Hatch\"}}",
            "/auth/v1/factors/factor-1/challenge" => "{\"id\":\"challenge-1\"}",
            _ => Session(aal: "aal2", factors: true)
        });
        var factor = await _client.EnrollAsync("My PC");
        Assert.AreEqual("fixture-secret", factor!.Totp!.Secret);
        using (var enrollment = JsonDocument.Parse(_requests.ElementAt(0).Body))
        {
            Assert.AreEqual("totp", enrollment.RootElement.GetProperty("factor_type").GetString());
            Assert.AreEqual("Hatch", enrollment.RootElement.GetProperty("issuer").GetString());
            Assert.AreEqual("My PC", enrollment.RootElement.GetProperty("friendly_name").GetString());
        }
        await _client.VerifyMfaAsync("factor-1", "123456");
        using var verification = JsonDocument.Parse(_requests.ElementAt(2).Body);
        Assert.AreEqual("challenge-1", verification.RootElement.GetProperty("challenge_id").GetString());
        Assert.AreEqual("123456", verification.RootElement.GetProperty("code").GetString());
        Assert.AreEqual(("aal2", "aal2"), _client.AssuranceLevel());
        Assert.IsNotNull(_client.VerifiedTotp);
    }

    [TestMethod]
    public async Task Mfa_UnenrollReloadsUserFactorState()
    {
        await SignIn(factors: true);
        _respond = _ => (HttpStatusCode.OK, "{\"id\":\"user-1\",\"factors\":[]}");
        await _client.UnenrollAsync("factor-1");
        Assert.AreEqual("DELETE", _requests.ElementAt(0).Method);
        Assert.AreEqual("/auth/v1/factors/factor-1", _requests.ElementAt(0).Path);
        Assert.AreEqual("/auth/v1/user", _requests.ElementAt(1).Path);
        Assert.IsNull(_client.VerifiedTotp);
    }

    [TestMethod]
    public async Task Recovery_GeneratesCodesUsingExistingRpc()
    {
        await SignIn();
        _respond = _ => (HttpStatusCode.OK, "[\"code-1\",\"code-2\"]");
        CollectionAssert.AreEqual(new[] { "code-1", "code-2" }, await _client.GenerateRecoveryCodesAsync());
        Assert.AreEqual("/rest/v1/rpc/generate_mfa_recovery_codes", _requests.Single().Path);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Recovery_RemovesFactorsOnlyAfterAcceptedCode(bool accepted)
    {
        await SignIn(factors: true);
        _respond = _ => (HttpStatusCode.OK, accepted ? "true" : "false");
        Assert.AreEqual(accepted, await _client.RedeemRecoveryCodeAsync("recovery-code"));
        Assert.AreEqual(accepted, _client.VerifiedTotp == null);
        using var body = JsonDocument.Parse(_requests.Single().Body);
        Assert.AreEqual("recovery-code", body.RootElement.GetProperty("code").GetString());
        Assert.AreEqual("/rest/v1/rpc/redeem_mfa_recovery_code", _requests.Single().Path);
    }

    [TestMethod]
    public async Task SignOut_ClearsLocalSessionEvenIfRemoteLogoutFails()
    {
        await SignIn();
        _respond = _ => (HttpStatusCode.ServiceUnavailable, "{\"message\":\"offline\"}");
        await Assert.ThrowsAsync<SyncApiException>(() => _client.SignOutAsync());
        Assert.IsNull(_client.Session);
        Assert.AreEqual("/auth/v1/logout?scope=global", _requests.Single().Path);
    }

    [TestMethod]
    public async Task Refresh_DefinitiveRejectionClearsSessionAndNotifies()
    {
        await SignIn(expired: true);
        bool rejected = false;
        _client.SessionRejected = () => rejected = true;
        _respond = _ => (HttpStatusCode.BadRequest, "{\"error_code\":\"refresh_token_not_found\",\"msg\":\"expired\"}");
        await Assert.ThrowsAsync<SyncApiException>(() => _client.GetRowAsync());
        Assert.IsTrue(rejected);
        Assert.IsNull(_client.Session);
    }

    [TestMethod]
    public async Task Refresh_TransientFailurePreservesSessionForRetry()
    {
        await SignIn(expired: true);
        var previous = _client.Session;
        _respond = _ => (HttpStatusCode.ServiceUnavailable, "{\"message\":\"unavailable\"}");
        await Assert.ThrowsAsync<SyncApiException>(() => _client.GetRowAsync());
        Assert.AreSame(previous, _client.Session);
        _respond = request => (HttpStatusCode.OK, request.Path.StartsWith("/auth/") ? Session() : "[]");
        Assert.IsNull(await _client.GetRowAsync());
    }

    [TestMethod]
    public async Task Refresh_ConcurrentReadsRotateTokenOnlyOnce()
    {
        await SignIn(expired: true);
        _respond = request => (HttpStatusCode.OK, request.Path.StartsWith("/auth/") ? Session() : "[]");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _client.GetRowAsync()));
        Assert.AreEqual(1, _requests.Count(request => request.Path.Contains("grant_type=refresh_token")));
        Assert.AreEqual(8, _requests.Count(request => request.Path.StartsWith("/rest/")));
        Assert.IsTrue(_requests.Where(request => request.Path.StartsWith("/rest/"))
            .All(request => request.Authorization == "Bearer " + _client.Session!.AccessToken));
    }

    [TestMethod]
    public async Task Upsert_AnotherAccountIsRejectedBeforeNetworkWrite()
    {
        await SignIn();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.UpsertAsync(new UserDataRow { UserId = "another-user" }));
        Assert.AreEqual(0, _requests.Count);
    }

    private async Task SignIn(bool factors = false, bool expired = false)
    {
        _respond = _ => (HttpStatusCode.OK, Session(expired: expired, factors: factors));
        await _client.SignInAsync("person@example.test", "password");
        _requests.Clear();
    }

    private static string Session(bool expired = false, string aal = "aal1", bool factors = false, string refresh = "refresh") =>
        JsonSerializer.Serialize(new
        {
            access_token = Token(expired, aal), refresh_token = refresh,
            user = new { id = "user-1", factors = factors ? new[] { new { id = "factor-1", factor_type = "totp", status = "verified" } } : [] }
        });

    private static string Token(bool expired = false, string aal = "aal1") =>
        "e30." + Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            sub = "user-1", exp = DateTimeOffset.UtcNow.AddHours(expired ? -1 : 1).ToUnixTimeSeconds(), aal
        }))) + ".synthetic";

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private sealed record Request(string Method, string Path, string Body, string? Authorization, string ApiKey);
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
