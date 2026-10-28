using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Hatch.Models;

namespace Hatch.Helpers;

internal sealed class GoogleCalendarClient(HttpClient http, string clientId, string? clientSecret, string scope)
{
    private const string CalendarRoot = "https://www.googleapis.com/calendar/v3";

    internal static string? ResolveRefreshToken(string? returned, string? stored) =>
        string.IsNullOrEmpty(returned) ? stored : returned;

    internal async Task<GoogleToken> RequestTokenAsync(Dictionary<string, string> parameters, CancellationToken ct)
    {
        parameters["client_id"] = clientId;
        if (!string.IsNullOrEmpty(clientSecret)) parameters["client_secret"] = clientSecret;
        using var content = new FormUrlEncodedContent(parameters);
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var code = await OAuthErrorCodeAsync(response, ct);
            var authorizationCode = parameters.GetValueOrDefault("grant_type") == "authorization_code";
            throw new GoogleCalendarException("Calendar_ErrorReconnect", response.StatusCode,
                isAuthorizationCodeExchange: authorizationCode, oauthErrorCode: code);
        }
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var access = CalendarEventParser.Text(json.RootElement, "access_token");
        if (string.IsNullOrEmpty(access)) throw new JsonException();
        var granted = CalendarEventParser.Text(json.RootElement, "scope");
        if (granted != null && scope.Split(' ').Except(granted.Split(' ')).Any())
            throw new GoogleCalendarException("Calendar_ErrorConsent");
        var seconds = json.RootElement.TryGetProperty("expires_in", out var expiry) && expiry.TryGetInt32(out var value) ? value : 3600;
        return new(access, CalendarEventParser.Text(json.RootElement, "refresh_token"), DateTimeOffset.UtcNow.AddSeconds(seconds));
    }

    internal async Task<List<GoogleCalendar>> ReadCalendarsAsync(string access, CancellationToken ct)
    {
        var parameters = new Dictionary<string, string>
        {
            ["minAccessRole"] = "owner", ["showHidden"] = "true", ["maxResults"] = "250",
            ["fields"] = "nextPageToken,items(id,summary,summaryOverride,timeZone,primary,accessRole,dataOwner)"
        };
        var calendars = new List<GoogleCalendar>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? next;
        do
        {
            using var page = await ReadAsync(CalendarRoot + "/users/me/calendarList?" + Query(parameters), access, ct);
            if (page.RootElement.TryGetProperty("items", out var items))
                foreach (var item in items.EnumerateArray())
                {
                    if (CalendarEventParser.Text(item, "accessRole") != "owner") continue;
                    calendars.Add(new(
                        CalendarEventParser.Text(item, "id") ?? throw new JsonException(),
                        CalendarEventParser.Text(item, "summaryOverride") ?? CalendarEventParser.Text(item, "summary") ?? string.Empty,
                        CalendarEventParser.Text(item, "timeZone") ?? "UTC",
                        item.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True,
                        CalendarEventParser.Text(item, "dataOwner")));
                }
            next = CalendarEventParser.Text(page.RootElement, "nextPageToken");
            if (next != null)
            {
                if (!seen.Add(next) || seen.Count > 1000) throw new JsonException();
                parameters["pageToken"] = next;
            }
        } while (next != null);
        return calendars;
    }

    internal async Task<GoogleCalendarSnapshot> ReadEventsAsync(string accountId, string primaryEmail,
        IReadOnlyList<GoogleCalendar> calendars, DateTimeOffset start, DateTimeOffset end,
        Func<string, CancellationToken, Task<JsonDocument>> readPage, CancellationToken ct)
    {
        var events = new List<CalendarEvent>();
        var sources = new List<CalendarSource>();
        foreach (var calendar in calendars.Where(c => GoogleCalendarEventParser.HasMatchingDataOwner(
                     c.IsPrimary, c.DataOwner, primaryEmail)).DistinctBy(c => c.Id))
        {
            var parameters = new Dictionary<string, string>
            {
                ["timeMin"] = start.AddSeconds(-1).ToString("O", CultureInfo.InvariantCulture),
                ["timeMax"] = end.ToString("O", CultureInfo.InvariantCulture),
                ["singleEvents"] = "true", ["showDeleted"] = "false", ["maxResults"] = "250", ["maxAttendees"] = "1",
                ["timeZone"] = "UTC",
                ["fields"] = "nextPageToken,items(id,status,summary,start,end,htmlLink,hangoutLink,attendees(self,responseStatus),conferenceData(entryPoints(entryPointType,uri)))"
            };
            var values = new Dictionary<string, CalendarEvent>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? next;
            do
            {
                using var page = await readPage(CalendarRoot + "/calendars/" + Uri.EscapeDataString(calendar.Id) + "/events?" + Query(parameters), ct);
                if (page.RootElement.TryGetProperty("items", out var items))
                    foreach (var item in items.EnumerateArray())
                        GoogleCalendarEventParser.Apply(values, item, accountId, calendar.Id, calendar.Name, calendar.TimeZone);
                next = CalendarEventParser.Text(page.RootElement, "nextPageToken");
                if (next != null)
                {
                    if (!seen.Add(next) || seen.Count > 1000) throw new JsonException();
                    parameters["pageToken"] = next;
                }
            } while (next != null);
            events.AddRange(values.Values.Where(e => e.End >= start && e.Start < end));
            sources.Add(new CalendarSource { Id = calendar.Id, Name = calendar.Name });
        }
        return new(events, sources);
    }

    internal async Task<JsonDocument> ReadAsync(string url, string access, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "www.googleapis.com" ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || !uri.AbsolutePath.StartsWith("/calendar/v3/", StringComparison.Ordinal))
            throw new JsonException("Invalid Google Calendar URL.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var limited = response.StatusCode == HttpStatusCode.TooManyRequests;
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                try
                {
                    using var error = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                    limited = error.RootElement.TryGetProperty("error", out var details) && details.TryGetProperty("errors", out var errors) &&
                        errors.EnumerateArray().Any(e => CalendarEventParser.Text(e, "reason") is "rateLimitExceeded" or "userRateLimitExceeded" or "dailyLimitExceeded" or "quotaExceeded");
                }
                catch (JsonException) { }
            }
            var key = limited ? "Calendar_ErrorThrottled" : response.StatusCode switch
            {
                HttpStatusCode.Forbidden => "Calendar_ErrorPermission", HttpStatusCode.Unauthorized => "Calendar_ErrorReconnect",
                _ => "Calendar_ErrorProvider"
            };
            throw new GoogleCalendarException(key, response.StatusCode, limited
                ? response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5)) : null);
        }
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    private static async Task<string?> OAuthErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var error = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var code = CalendarEventParser.Text(error.RootElement, "error");
            return code is "invalid_grant" or "invalid_client" or "unauthorized_client" or
                "redirect_uri_mismatch" or "invalid_request" or "invalid_scope" or "unsupported_grant_type" ? code : null;
        }
        catch (JsonException) { return null; }
    }

    private static string Query(Dictionary<string, string> values) => string.Join("&",
        values.Select(v => Uri.EscapeDataString(v.Key) + "=" + Uri.EscapeDataString(v.Value)));
}
