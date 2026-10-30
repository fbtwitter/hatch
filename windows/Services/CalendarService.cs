using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.Services;

public sealed partial class CalendarService
{
    private const string GraphRoot = "https://graph.microsoft.com/v1.0";
    private const string Authority = "https://login.microsoftonline.com/common/oauth2/v2.0";
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(30) };
    private readonly GoogleCalendarClient _googleClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.Combine(AppDataPath.Folder, "calendar.json");
    private readonly Dictionary<string, TokenResult> _sessions = [];
    private readonly Dictionary<string, DateTimeOffset> _nextRefresh = [];
    private readonly Dictionary<string, DateTimeOffset> _throttledUntil = [];
    private readonly Dictionary<string, string> _errors = [];
    private readonly CalendarNotificationSchedulerService _notifications = new();
    private List<CalendarAccount> _accounts = [];
    private Task? _initialization;
    private CancellationTokenSource? _pollCancellation;
    private CancellationTokenSource? _microsoftAuthorization;
    private CancellationTokenSource? _googleAuthorization;

    public CalendarService() => _googleClient = new(_http, CalendarConfiguration.GoogleClientId,
        CalendarConfiguration.GoogleClientSecret, CalendarConfiguration.GoogleScope);

    public IReadOnlyList<CalendarAccount> Accounts => _accounts;
    public bool IsMicrosoftConfigured => Guid.TryParse(CalendarConfiguration.ClientId, out _);
    public bool IsGoogleConfigured => CalendarConfiguration.GoogleClientId.Length > 30 &&
        CalendarConfiguration.GoogleClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal);
    public bool IsConfigured => IsMicrosoftConfigured || IsGoogleConfigured;
    public bool IsRefreshing { get; private set; }
    public bool IsAuthorizing => _microsoftAuthorization != null || _googleAuthorization != null;
    public bool IsGoogleCallbackReceived { get; private set; }
    public string? Error { get; private set; }
    public string? ReminderError { get; private set; }
    public event Action? StateChanged;

    public string? GetAccountError(string id) => _errors.GetValueOrDefault(id);
    public Task InitializeAsync() => _initialization ??= LoadAsync();

    private async Task LoadAsync()
    {
        Exception? lastError = null;
        foreach (var candidate in CalendarCacheFile.ReadCandidates(_path))
        {
            try
            {
                await using var file = File.OpenRead(candidate);
                var accounts = await JsonSerializer.DeserializeAsync(file,
                    CalendarJsonContext.Default.ListCalendarAccount) ?? [];
                ValidateAccounts(accounts);
                _accounts = accounts;
                if (lastError != null)
                    CrashLog.Write("Calendar cache recovery", "A newer snapshot could not be read; loaded the previous valid snapshot.");
                lastError = null;
                break;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                lastError = ex;
                CrashLog.Write("Calendar cache read", $"{Path.GetFileName(candidate)}: {ex.GetType().Name} (HRESULT 0x{ex.HResult:X8}).");
            }
        }
        if (lastError != null)
        {
            _accounts = [];
            Error = Strings.Get("Calendar_ErrorCache");
        }
        ReconcileReminders();
        StateChanged?.Invoke();
        EnsurePolling();
    }

    private static void ValidateAccounts(List<CalendarAccount> accounts)
    {
        if (accounts.Any(a => a == null || string.IsNullOrEmpty(a.Id) || string.IsNullOrEmpty(a.Email) ||
            a.Provider is not (CalendarProvider.Microsoft or CalendarProvider.Google or CalendarProvider.Ics) ||
            a.Calendars == null || a.Events == null ||
            a.Events.Any(e => e == null || e.AccountId != a.Id || string.IsNullOrEmpty(e.Id) ||
                string.IsNullOrEmpty(e.CalendarId) || e.End < e.Start)))
            throw new JsonException("Invalid calendar account cache.");
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync();
        if (!IsMicrosoftConfigured || AppDataPath.IsUiTest)
            throw new ProviderException("Calendar_NotConfigured");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var receiver = new CalendarLoopbackReceiver(useLocalhost: true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var authorization = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            _microsoftAuthorization = authorization;
            StateChanged?.Invoke();
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));
            var url = Authority + "/authorize?" + Query(new()
            {
                ["client_id"] = CalendarConfiguration.ClientId,
                ["redirect_uri"] = receiver.RedirectUri.AbsoluteUri,
                ["response_type"] = "code", ["response_mode"] = "query",
                ["scope"] = CalendarConfiguration.Scope, ["state"] = state,
                ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
                ["code_challenge_method"] = "S256", ["prompt"] = "select_account"
            });
            if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(url)))
                throw new ProviderException("Calendar_ErrorBrowser");
            Uri callback;
            try
            {
                callback = await receiver.WaitAsync(state,
                    succeeded => CalendarOAuthCompletionPage.Render(Strings.Get("Calendar_OAuthMicrosoft"), succeeded),
                    authorization.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            { throw new TimeoutException(); }
            receiver.Dispose();
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() => App.MainWindowInstance?.ActivateAfterExternalAuthorization());
            if (Parameter(callback, "error") != null)
                throw new ProviderException("Calendar_ErrorConsent");
            var code = Parameter(callback, "code");
            if (string.IsNullOrEmpty(code)) throw new ProviderException("Calendar_ErrorConsent");
            var token = await RequestTokenAsync(new()
            {
                ["grant_type"] = "authorization_code", ["code"] = code,
                ["code_verifier"] = verifier, ["redirect_uri"] = receiver.RedirectUri.AbsoluteUri
            }, cancellationToken);
            if (string.IsNullOrEmpty(token.Refresh))
                throw new ProviderException("Calendar_ErrorReconnect");

            using var primary = await ReadGraphAsync(GraphRoot + "/me/calendar?$select=id,name,owner", token.Access, cancellationToken);
            var root = primary.RootElement;
            var email = CalendarEventParser.Text(root.GetProperty("owner"), "address");
            var defaultId = CalendarEventParser.Text(root, "id");
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(defaultId)) throw new JsonException();
            var previous = _accounts.FirstOrDefault(a => a.Provider == CalendarProvider.Microsoft &&
                (a.DefaultCalendarId == defaultId || string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase)));
            var id = previous?.Id ?? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.ToLowerInvariant())));
            var account = previous == null ? new CalendarAccount { Id = id, Email = email, DefaultCalendarId = defaultId }
                : previous with { Email = email, DefaultCalendarId = defaultId };
            await StoreConnectionAsync(account, token, cancellationToken);
        }
        finally { _microsoftAuthorization = null; _gate.Release(); StateChanged?.Invoke(); }
        EnsurePolling();
        await RefreshAsync(force: true, cancellationToken);
    }

    public void CancelConnect()
    {
        _microsoftAuthorization?.Cancel();
        _googleAuthorization?.Cancel();
    }

    public async Task DisconnectAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account == null) return;
            var refresh = CalendarTokenStore.Load(account);
            CalendarTokenStore.Clear(account);
            var accounts = _accounts.Where(a => a.Id != accountId).ToList();
            try { await SaveAsync(accounts, cancellationToken); }
            catch
            {
                if (refresh != null) CalendarTokenStore.Save(account, refresh);
                throw;
            }
            _accounts = accounts;
            _sessions.Remove(accountId);
            _nextRefresh.Remove(accountId);
            _throttledUntil.Remove(accountId);
            _errors.Remove(accountId);
            ReconcileReminders();
            if (!_accounts.Any(a => a.Provider != CalendarProvider.Ics))
            {
                _pollCancellation?.Cancel();
                _pollCancellation = null;
            }
        }
        finally { _gate.Release(); StateChanged?.Invoke(); }
    }

    public async Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        await InitializeAsync();
        if (!IsConfigured || AppDataPath.IsUiTest || !_accounts.Any(a => a.Provider != CalendarProvider.Ics) ||
            !await _gate.WaitAsync(0, cancellationToken)) return;
        IsRefreshing = true;
        StateChanged?.Invoke();
        try
        {
            foreach (var account in _accounts.ToArray())
            {
                if (account.Provider == CalendarProvider.Ics) continue;
                if ((_throttledUntil.TryGetValue(account.Id, out var retryAt) && retryAt > DateTimeOffset.UtcNow) ||
                    (!force && _nextRefresh.TryGetValue(account.Id, out var next) && next > DateTimeOffset.UtcNow)) continue;
                _nextRefresh[account.Id] = DateTimeOffset.UtcNow.AddMinutes(5);
                try
                {
                    if (account.Provider == CalendarProvider.Google ? !IsGoogleConfigured : !IsMicrosoftConfigured)
                        throw new ProviderException("Calendar_NotConfigured");
                    var updated = account.Provider == CalendarProvider.Google
                        ? await RefreshGoogleAccountAsync(account, cancellationToken)
                        : await RefreshAccountAsync(account, cancellationToken);
                    var accounts = _accounts.Select(a => a.Id == account.Id ? updated : a).ToList();
                    await SaveAsync(accounts, cancellationToken);
                    _accounts = accounts;
                    ReconcileReminders();
                    _errors.Remove(account.Id);
                    _throttledUntil.Remove(account.Id);
                    Error = null;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (ProviderException ex)
                {
                    _errors[account.Id] = ex.Message;
                    if (ex.RetryAt is { } retry) _throttledUntil[account.Id] = retry;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
                { _errors[account.Id] = Strings.Get("Calendar_ErrorOffline"); }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
                {
                    if (account.Provider == CalendarProvider.Microsoft)
                        CrashLog.Write("Microsoft calendar refresh", $"Response parsing failed ({ex.GetType().Name}).");
                    _errors[account.Id] = Strings.Get("Calendar_ErrorProvider");
                }
                catch (Exception ex) { _errors[account.Id] = FriendlyError(ex); }
            }
        }
        finally { IsRefreshing = false; _gate.Release(); StateChanged?.Invoke(); }
    }

    private async Task<CalendarAccount> RefreshAccountAsync(CalendarAccount account, CancellationToken ct)
    {
        using var primary = await ReadForAccountAsync(account.Id, GraphRoot + "/me/calendar?$select=id,name,owner", ct);
        var owner = CalendarEventParser.Text(primary.RootElement.GetProperty("owner"), "address");
        var defaultId = CalendarEventParser.Text(primary.RootElement, "id") ?? throw new JsonException();
        var today = DateTime.Today;
        var start = new DateTimeOffset(today.AddDays(-7)).ToUniversalTime();
        var end = new DateTimeOffset(today.AddDays(31)).ToUniversalTime();
        var sameWindow = account.WindowStart == start && account.WindowEnd == end;
        var sources = new List<CalendarSource>();
        string? link = GraphRoot + "/me/calendars?$select=id,name,owner&$top=100";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (link != null)
        {
            if (!seen.Add(link) || seen.Count > 1000) throw new JsonException();
            using var page = await ReadForAccountAsync(account.Id, link, ct);
            foreach (var item in page.RootElement.GetProperty("value").EnumerateArray())
            {
                if (!item.TryGetProperty("owner", out var calendarOwner) ||
                    !string.Equals(CalendarEventParser.Text(calendarOwner, "address"), owner, StringComparison.OrdinalIgnoreCase)) continue;
                var id = CalendarEventParser.Text(item, "id") ?? throw new JsonException();
                sources.Add(new CalendarSource
                {
                    Id = id, Name = CalendarEventParser.Text(item, "name") ?? string.Empty,
                    DeltaLink = sameWindow ? account.Calendars.FirstOrDefault(c => c.Id == id)?.DeltaLink : null
                });
            }
            link = CalendarEventParser.Text(page.RootElement, "@odata.nextLink");
        }
        if (string.IsNullOrEmpty(owner)) throw new JsonException();
        if (sources.All(s => s.Id != defaultId))
            sources.Add(new CalendarSource { Id = defaultId, Name = CalendarEventParser.Text(primary.RootElement, "name") ?? string.Empty });

        var events = new List<CalendarEvent>();
        var syncedSources = new List<CalendarSource>();
        var range = "startDateTime=" + Uri.EscapeDataString(start.ToString("O", CultureInfo.InvariantCulture)) +
                    "&endDateTime=" + Uri.EscapeDataString(end.ToString("O", CultureInfo.InvariantCulture));
        foreach (var source in sources.DistinctBy(s => s.Id))
        {
            var isPrimary = source.Id == defaultId;
            var initialUrl = isPrimary ? GraphRoot + "/me/calendarView/delta?" + range
                : GraphRoot + "/me/calendars/" + Uri.EscapeDataString(source.Id) + "/calendarView?" + range +
                  "&$select=id,subject,start,end,isAllDay,originalStartTimeZone,isCancelled,responseStatus,webLink,onlineMeeting&$top=250";
            var values = isPrimary && source.DeltaLink != null
                ? account.Events.Where(e => e.CalendarId == source.Id).ToDictionary(e => e.Id, StringComparer.Ordinal)
                : new Dictionary<string, CalendarEvent>(StringComparer.Ordinal);
            var continuation = isPrimary ? source.DeltaLink : null;
            string? delta;
            try
            {
                delta = await ReadEventsAsync(account.Id, source, continuation ?? initialUrl, values, isPrimary, ct);
            }
            catch (ProviderException ex) when (ex.Status == HttpStatusCode.Gone && continuation != null)
            {
                values.Clear();
                delta = await ReadEventsAsync(account.Id, source, initialUrl, values, isPrimary, ct);
            }
            events.AddRange(values.Values.Where(e => e.End >= start && e.Start < end)
                .Select(e => e with { CalendarName = source.Name }));
            syncedSources.Add(source with { DeltaLink = isPrimary ? delta : null });
        }
        return account with
        {
            Email = owner, DefaultCalendarId = defaultId, WindowStart = start, WindowEnd = end,
            Calendars = syncedSources, Events = events, LastSyncedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<string?> ReadEventsAsync(string accountId, CalendarSource source, string initialUrl,
        Dictionary<string, CalendarEvent> events, bool deltaRequired, CancellationToken ct)
    {
        string? link = initialUrl, delta = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (link != null)
        {
            if (!seen.Add(link) || seen.Count > 1000) throw new JsonException();
            using var page = await ReadForAccountAsync(accountId, link, ct);
            foreach (var item in page.RootElement.GetProperty("value").EnumerateArray())
                CalendarEventParser.Apply(events, item, accountId, source.Id, source.Name);
            link = CalendarEventParser.Text(page.RootElement, "@odata.nextLink");
            delta = CalendarEventParser.Text(page.RootElement, "@odata.deltaLink") ?? delta;
        }
        if (deltaRequired && delta == null)
            throw new JsonException("Calendar delta round has no completion token.");
        return delta;
    }

    private async Task<JsonDocument> ReadForAccountAsync(string accountId, string url, CancellationToken ct)
    {
        var access = await AccessTokenAsync(accountId, false, ct);
        try { return await ReadGraphAsync(url, access, ct); }
        catch (ProviderException ex) when (ex.Status == HttpStatusCode.Unauthorized)
        { return await ReadGraphAsync(url, await AccessTokenAsync(accountId, true, ct), ct); }
    }

    private async Task<string> AccessTokenAsync(string accountId, bool renew, CancellationToken ct)
    {
        if (!renew && _sessions.TryGetValue(accountId, out var session) && session.Expires > DateTimeOffset.UtcNow.AddMinutes(1))
            return session.Access;
        var account = _accounts.First(a => a.Id == accountId);
        var refresh = CalendarTokenStore.Load(account);
        if (string.IsNullOrEmpty(refresh)) throw new ProviderException("Calendar_ErrorReconnect");
        var token = account.Provider == CalendarProvider.Google
            ? await RequestGoogleTokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh }, ct)
            : await RequestTokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh }, ct);
        if (token.Refresh is { Length: > 0 } rotated && rotated != refresh) CalendarTokenStore.Save(account, rotated);
        _sessions[accountId] = token;
        return token.Access;
    }

    private async Task<TokenResult> RequestTokenAsync(Dictionary<string, string> parameters, CancellationToken ct)
    {
        parameters["client_id"] = CalendarConfiguration.ClientId;
        parameters["scope"] = CalendarConfiguration.Scope;
        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _http.PostAsync(Authority + "/token", content, ct);
        if (!response.IsSuccessStatusCode) throw new ProviderException("Calendar_ErrorReconnect", response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var access = CalendarEventParser.Text(json.RootElement, "access_token");
        if (string.IsNullOrEmpty(access)) throw new JsonException();
        var seconds = json.RootElement.TryGetProperty("expires_in", out var expiry) && expiry.TryGetInt32(out var value) ? value : 3600;
        return new(access, CalendarEventParser.Text(json.RootElement, "refresh_token"), DateTimeOffset.UtcNow.AddSeconds(seconds));
    }

    private async Task<JsonDocument> ReadGraphAsync(string url, string accessToken, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host != "graph.microsoft.com" || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.AbsolutePath.StartsWith("/v1.0/", StringComparison.Ordinal)) throw new JsonException("Invalid Graph continuation URL.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\", IdType=\"ImmutableId\", odata.maxpagesize=250");
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var graphErrorCode = await ReadGraphErrorCodeAsync(response.Content, ct);
            var requestId = response.Headers.TryGetValues("request-id", out var requestIds)
                ? requestIds.FirstOrDefault() : null;
            CrashLog.Write("Microsoft calendar Graph request",
                $"{GraphOperation(uri)} returned HTTP {(int)response.StatusCode}; Graph error {graphErrorCode ?? "unknown"}; request ID {requestId ?? "unavailable"}.");
            var key = response.StatusCode switch
            {
                HttpStatusCode.Forbidden => "Calendar_ErrorPermission",
                HttpStatusCode.Unauthorized => "Calendar_ErrorReconnect",
                HttpStatusCode.TooManyRequests => "Calendar_ErrorThrottled",
                _ => "Calendar_ErrorProvider"
            };
            var retry = response.Headers.RetryAfter;
            throw new ProviderException(key, response.StatusCode,
                response.StatusCode == HttpStatusCode.TooManyRequests
                    ? retry?.Date ?? DateTimeOffset.UtcNow.Add(retry?.Delta ?? TimeSpan.FromMinutes(5)) : null);
        }
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private static async Task<string?> ReadGraphErrorCodeAsync(HttpContent content, CancellationToken ct)
    {
        try
        {
            using var document = JsonDocument.Parse(await content.ReadAsStringAsync(ct));
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                    ? CalendarEventParser.Text(error, "code") : null;
        }
        catch (JsonException) { return null; }
    }

    private static string GraphOperation(Uri uri) => uri.AbsolutePath switch
    {
        "/v1.0/me/calendar" => "primary calendar",
        "/v1.0/me/calendars" => "calendar list",
        "/v1.0/me/calendarView/delta" => "primary event delta",
        _ when uri.AbsolutePath.Contains("/calendarView", StringComparison.Ordinal) => "other calendar events",
        _ => "calendar request"
    };

    private async Task SaveAsync(List<CalendarAccount> accounts, CancellationToken ct)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".new";
        try
        {
            Directory.CreateDirectory(AppDataPath.Folder);
            await using (var file = File.Create(temporary))
                await JsonSerializer.SerializeAsync(file, accounts, CalendarJsonContext.Default.ListCalendarAccount, ct);

            await CalendarCacheFile.WriteSnapshotAsync(temporary, _path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("Calendar cache save", $"{ex.GetType().Name} (HRESULT 0x{ex.HResult:X8}).");
            throw new ProviderException("Calendar_ErrorCache");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private void ReconcileReminders() => ReminderError = _notifications.Reconcile(_accounts.SelectMany(a => a.Events));

    private void EnsurePolling()
    {
        if (AppDataPath.IsUiTest || !IsConfigured || !_accounts.Any(a => a.Provider != CalendarProvider.Ics) || _pollCancellation != null) return;
        _pollCancellation = new();
        _ = PollAsync(_pollCancellation.Token);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        try
        {
            while (await timer.WaitForNextTickAsync(ct)) await RefreshAsync(cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { Error = Strings.Get("Calendar_ErrorProvider"); StateChanged?.Invoke(); }
    }

    private static string? Parameter(Uri uri, string name) => CalendarOAuthCallback.Parameter(uri, name);

    private async Task StoreConnectionAsync(CalendarAccount account, TokenResult token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token.Refresh)) throw new ProviderException("Calendar_ErrorReconnect");
        var oldRefresh = CalendarTokenStore.Load(account);
        CalendarTokenStore.Save(account, token.Refresh);
        try
        {
            var accounts = _accounts.Where(a => a.Id != account.Id).Append(account).ToList();
            await SaveAsync(accounts, ct);
            _accounts = accounts;
        }
        catch
        {
            if (oldRefresh == null) CalendarTokenStore.Clear(account);
            else CalendarTokenStore.Save(account, oldRefresh);
            throw;
        }
        _sessions[account.Id] = token;
        _nextRefresh.Remove(account.Id);
        _throttledUntil.Remove(account.Id);
        Error = null;
        ReconcileReminders();
    }

    private static string Query(Dictionary<string, string> values) => string.Join("&",
        values.Select(v => Uri.EscapeDataString(v.Key) + "=" + Uri.EscapeDataString(v.Value)));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static string FriendlyError(Exception error) => error switch
    {
        ProviderException { IsGoogleTokenExchange: true, Status: { } status, GoogleOAuthErrorCode: "invalid_grant" } =>
            string.Format(Strings.Get("Calendar_ErrorGoogleGrant"), (int)status),
        ProviderException { IsGoogleTokenExchange: true, Status: { } status, GoogleOAuthErrorCode: "redirect_uri_mismatch" } =>
            string.Format(Strings.Get("Calendar_ErrorGoogleRedirect"), (int)status),
        ProviderException { IsGoogleTokenExchange: true, Status: { } status, GoogleOAuthErrorCode: "invalid_client" or "unauthorized_client" } =>
            string.Format(Strings.Get("Calendar_ErrorGoogleClient"), (int)status),
        ProviderException { IsGoogleTokenExchange: true, Status: { } status, GoogleOAuthErrorCode: { } code } =>
            string.Format(Strings.Get("Calendar_ErrorGoogleTokenCode"), (int)status, code),
        ProviderException { IsGoogleTokenExchange: true, Status: { } status } =>
            string.Format(Strings.Get("Calendar_ErrorGoogleToken"), (int)status),
        ProviderException { Key: "Calendar_ErrorGoogleApiToken", Status: { } status } =>
            string.Format(Strings.Get("Calendar_ErrorGoogleApiToken"), (int)status),
        ProviderException => error.Message,
        CalendarCredentialStoreException => Strings.Get("Calendar_ErrorCredentials"),
        TimeoutException => Strings.Get("Calendar_ErrorTimeout"),
        HttpRequestException or TaskCanceledException => Strings.Get("Calendar_ErrorOffline"),
        IOException or UnauthorizedAccessException => Strings.Get("Calendar_ErrorCache"),
        System.Runtime.InteropServices.COMException => Strings.Get("Calendar_ErrorCredentials"),
        System.Net.Sockets.SocketException => Strings.Get("Calendar_ErrorLoopback"),
        _ => Strings.Get("Calendar_ErrorProvider")
    };
    private sealed record TokenResult(string Access, string? Refresh, DateTimeOffset Expires);
    private sealed class ProviderException(string key, HttpStatusCode? status = null, DateTimeOffset? retryAt = null,
        bool googleTokenExchange = false, string? googleOAuthErrorCode = null)
        : Exception(Strings.Get(key))
    {
        internal string Key => key;
        internal HttpStatusCode? Status => status;
        internal DateTimeOffset? RetryAt => retryAt;
        internal bool IsGoogleTokenExchange => googleTokenExchange;
        internal string? GoogleOAuthErrorCode => googleOAuthErrorCode;
    }
}
