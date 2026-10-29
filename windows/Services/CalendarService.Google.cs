using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.Services;

public sealed partial class CalendarService
{
    public async Task ConnectGoogleAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync();
        if (!IsGoogleConfigured || AppDataPath.IsUiTest) throw new ProviderException("Calendar_NotConfigured");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var receiver = new CalendarLoopbackReceiver();
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var authorization = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            _googleAuthorization = authorization;
            StateChanged?.Invoke();
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var url = "https://accounts.google.com/o/oauth2/v2/auth?" + Query(new()
            {
                ["client_id"] = CalendarConfiguration.GoogleClientId,
                ["redirect_uri"] = receiver.RedirectUri.AbsoluteUri, ["response_type"] = "code",
                ["scope"] = CalendarConfiguration.GoogleScope, ["access_type"] = "offline",
                ["prompt"] = "consent select_account", ["state"] = state,
                ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
                ["code_challenge_method"] = "S256"
            });
            if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(url))) throw new ProviderException("Calendar_ErrorBrowser");
            Uri callback;
            try { callback = await receiver.WaitAsync(state, Strings.Get("Calendar_BrowserReturn"), authorization.Token); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            { throw new TimeoutException(); }
            receiver.Dispose();
            _googleAuthorization = null;
            StateChanged?.Invoke();
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() => App.MainWindowInstance?.Activate());
            if (Parameter(callback, "error") != null) throw new ProviderException("Calendar_ErrorConsent");
            var code = Parameter(callback, "code");
            if (string.IsNullOrEmpty(code)) throw new ProviderException("Calendar_ErrorConsent");
            var token = await RequestGoogleTokenAsync(new()
            {
                ["grant_type"] = "authorization_code", ["code"] = code,
                ["code_verifier"] = verifier, ["redirect_uri"] = receiver.RedirectUri.AbsoluteUri
            }, cancellationToken);
            List<GoogleCalendar> calendars;
            try { calendars = await ReadGoogleCalendarsAsync(token.Access, cancellationToken); }
            catch (ProviderException ex) when (ex.Status == HttpStatusCode.Unauthorized)
            { throw new ProviderException("Calendar_ErrorGoogleApiToken", ex.Status); }
            var primary = calendars.FirstOrDefault(c => c.IsPrimary) ?? throw new JsonException("No primary Google calendar.");
            var previous = _accounts.FirstOrDefault(a => a.Provider == CalendarProvider.Google &&
                string.Equals(a.Email, primary.Id, StringComparison.OrdinalIgnoreCase));
            token = token with
            {
                Refresh = GoogleCalendarClient.ResolveRefreshToken(token.Refresh,
                    previous == null ? null : CalendarTokenStore.Load(previous))
            };
            if (string.IsNullOrEmpty(token.Refresh)) throw new ProviderException("Calendar_ErrorGoogleRefreshToken");
            var id = previous?.Id ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Google:" + primary.Id.ToLowerInvariant())));
            var account = previous ?? new CalendarAccount
                { Id = id, Provider = CalendarProvider.Google, Email = primary.Id, DefaultCalendarId = primary.Id };
            await StoreConnectionAsync(account, token, cancellationToken);
        }
        finally
        {
            _googleAuthorization = null;
            _gate.Release();
            StateChanged?.Invoke();
        }
        EnsurePolling();
        await RefreshAsync(force: true, cancellationToken);
    }

    private async Task<CalendarAccount> RefreshGoogleAccountAsync(CalendarAccount account, CancellationToken ct)
    {
        var access = await AccessTokenAsync(account.Id, false, ct);
        List<GoogleCalendar> calendars;
        try { calendars = await ReadGoogleCalendarsAsync(access, ct); }
        catch (ProviderException ex) when (ex.Status == HttpStatusCode.Unauthorized)
        {
            access = await AccessTokenAsync(account.Id, true, ct);
            calendars = await ReadGoogleCalendarsAsync(access, ct);
        }
        var primary = calendars.FirstOrDefault(c => c.IsPrimary) ?? throw new JsonException();
        var today = DateTime.Today;
        var start = new DateTimeOffset(today.AddDays(-7)).ToUniversalTime();
        var end = new DateTimeOffset(today.AddDays(31)).ToUniversalTime();
        var snapshot = await _googleClient.ReadEventsAsync(account.Id, primary.Id, calendars, start, end,
            (url, token) => ReadGoogleForAccountAsync(account.Id, url, token), ct);
        return account with
        {
            Email = primary.Id, DefaultCalendarId = primary.Id, WindowStart = start, WindowEnd = end,
            Calendars = snapshot.Sources, Events = snapshot.Events, LastSyncedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<List<GoogleCalendar>> ReadGoogleCalendarsAsync(string access, CancellationToken ct)
    {
        try { return await _googleClient.ReadCalendarsAsync(access, ct); }
        catch (GoogleCalendarException ex) { throw ToProviderException(ex); }
    }

    private async Task<TokenResult> RequestGoogleTokenAsync(Dictionary<string, string> parameters, CancellationToken ct)
    {
        try
        {
            var token = await _googleClient.RequestTokenAsync(parameters, ct);
            return new(token.Access, token.Refresh, token.Expires);
        }
        catch (GoogleCalendarException ex) { throw ToProviderException(ex); }
    }

    private async Task<JsonDocument> ReadGoogleForAccountAsync(string accountId, string url, CancellationToken ct)
    {
        try { return await ReadGoogleAsync(url, await AccessTokenAsync(accountId, false, ct), ct); }
        catch (ProviderException ex) when (ex.Status == HttpStatusCode.Unauthorized)
        { return await ReadGoogleAsync(url, await AccessTokenAsync(accountId, true, ct), ct); }
    }

    private async Task<JsonDocument> ReadGoogleAsync(string url, string access, CancellationToken ct)
    {
        try { return await _googleClient.ReadAsync(url, access, ct); }
        catch (GoogleCalendarException ex) { throw ToProviderException(ex); }
    }

    private static ProviderException ToProviderException(GoogleCalendarException ex) =>
        new(ex.Key, ex.Status, ex.RetryAt, ex.IsAuthorizationCodeExchange, ex.OAuthErrorCode);
}
