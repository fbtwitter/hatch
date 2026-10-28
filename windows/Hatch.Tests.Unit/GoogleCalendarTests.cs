using System.Net;
using System.Text.Json;
using Hatch.Helpers;
using Hatch.Models;
using Hatch.Services;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class GoogleCalendarTests
{
    private static void Apply(Dictionary<string, CalendarEvent> events, string json)
    {
        using var document = JsonDocument.Parse(json);
        GoogleCalendarEventParser.Apply(events, document.RootElement, "google-account", "primary", "Personal", "UTC");
    }

    private static string Timed(string id = "occurrence", string start = "2026-10-26T09:00:00+07:00", string extra = "") => $$"""
        {"id":"{{id}}","summary":"Google meeting","start":{"dateTime":"{{start}}"},
         "end":{"dateTime":"2026-10-26T10:00:00+07:00"}{{extra}}}
        """;

    [TestMethod]
    public void TimedEventsPreserveTheirInstantAndCalendarIdentity()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, Timed());
        var item = events["occurrence"];
        Assert.AreEqual(new DateTimeOffset(2026, 10, 26, 2, 0, 0, TimeSpan.Zero), item.Start);
        Assert.AreEqual("google-account", item.AccountId);
        Assert.IsFalse(item.IsAllDay);
    }

    [TestMethod]
    public void CancelledOccurrenceIsRemovedWithoutNeedingStartAndEnd()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, Timed());
        Apply(events, """{"id":"occurrence","status":"cancelled"}""");
        Assert.IsEmpty(events);
    }

    [TestMethod]
    public void OnlyTheCurrentUsersDeclineRemovesTheMeeting()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, Timed(extra: ",\"attendees\":[{\"responseStatus\":\"declined\"}]"));
        Assert.HasCount(1, events);
        Apply(events, """{"id":"occurrence","attendees":[{"self":true,"responseStatus":"declined"}]}""");
        Assert.IsEmpty(events);
    }

    [TestMethod]
    public void AllDayDatesRemainLiteralAndEndIsExclusive()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, """{"id":"birthday","start":{"date":"2026-10-26"},"end":{"date":"2026-10-27"}}""");
        Assert.IsTrue(events["birthday"].IsAllDay);
        Assert.AreEqual(new DateOnly(2026, 10, 26), events["birthday"].AllDayStart);
        Assert.AreEqual(new DateOnly(2026, 10, 27), events["birthday"].AllDayEnd);
        Assert.IsEmpty(CalendarReminderPlanner.CreateCandidates(events.Values, new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero)));
    }

    [TestMethod]
    public void ConferenceVideoLinkWinsAndTelephoneEntryIsIgnored()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, Timed(extra: """
            ,"htmlLink":"https://calendar.google.com/event","hangoutLink":"https://meet.google.com/fallback",
             "conferenceData":{"entryPoints":[{"entryPointType":"phone","uri":"tel:123"},
             {"entryPointType":"video","uri":"https://meet.google.com/abc-defg-hij"}]}
            """));
        Assert.AreEqual("https://meet.google.com/abc-defg-hij", events["occurrence"].MeetingUrl);
        Assert.AreEqual("https://calendar.google.com/event", events["occurrence"].EventUrl);
    }

    [TestMethod]
    public void RescheduledRecurringOccurrenceReplacesTheSameIdentity()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, Timed());
        Apply(events, Timed(start: "2026-10-26T09:30:00+07:00"));
        Apply(events, Timed(id: "other-occurrence"));
        Assert.HasCount(2, events);
        Assert.AreEqual(30, events["occurrence"].Start.Minute);
    }

    [TestMethod]
    public void SharedManagerRoleDoesNotCountAsDataOwnership()
    {
        Assert.IsTrue(GoogleCalendarEventParser.HasMatchingDataOwner(true, null, "me@example.com"));
        Assert.IsTrue(GoogleCalendarEventParser.HasMatchingDataOwner(false, "ME@example.com", "me@example.com"));
        Assert.IsFalse(GoogleCalendarEventParser.HasMatchingDataOwner(false, "other@example.com", "me@example.com"));
        Assert.IsFalse(GoogleCalendarEventParser.HasMatchingDataOwner(false, null, "me@example.com"));
    }

    [TestMethod]
    public void ExistingMicrosoftCacheDefaultsToMicrosoftWhenProviderIsMissing()
    {
        var accounts = JsonSerializer.Deserialize("""[{"Id":"existing","Email":"me@example.com"}]""", CalendarJsonContext.Default.ListCalendarAccount)!;
        Assert.AreEqual(CalendarProvider.Microsoft, accounts[0].Provider);
        var mixed = new List<CalendarAccount> { accounts[0], new() { Id = "google", Email = "me@example.com", Provider = CalendarProvider.Google } };
        var roundTrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(mixed, CalendarJsonContext.Default.ListCalendarAccount),
            CalendarJsonContext.Default.ListCalendarAccount)!;
        Assert.HasCount(2, roundTrip);
        Assert.AreEqual(CalendarProvider.Google, roundTrip[1].Provider);
    }

    [TestMethod]
    public void OAuthCallbackRejectsMissingWrongAndDuplicateState()
    {
        Assert.IsFalse(CalendarOAuthCallback.HasMatchingState(new Uri("http://127.0.0.1/?code=c"), "expected"));
        Assert.IsFalse(CalendarOAuthCallback.HasMatchingState(new Uri("http://127.0.0.1/?state=wrong"), "expected"));
        Assert.IsFalse(CalendarOAuthCallback.HasMatchingState(new Uri("http://127.0.0.1/?state=expected&state=expected"), "expected"));
        Assert.IsTrue(CalendarOAuthCallback.HasMatchingState(new Uri("http://127.0.0.1/?state=expected"), "expected"));
        Assert.AreEqual("a+b", CalendarOAuthCallback.Parameter(new Uri("http://127.0.0.1/?code=a%2Bb"), "code"));
    }

    [TestMethod]
    public async Task LoopbackReceiverRejectsWrongStateAndHostBeforeAcceptingTheValidCode()
    {
        using var receiver = new CalendarLoopbackReceiver();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        var callback = receiver.WaitAsync("expected", "Return to Hatch", timeout.Token);
        using var wrongState = await http.GetAsync(new Uri(receiver.RedirectUri, "?state=wrong&code=private-code"), timeout.Token);
        Assert.AreEqual(HttpStatusCode.BadRequest, wrongState.StatusCode);
        using var badHost = new HttpRequestMessage(HttpMethod.Get, new Uri(receiver.RedirectUri, "?state=expected&code=private-code"));
        badHost.Headers.Host = "untrusted.example";
        using var rejected = await http.SendAsync(badHost, timeout.Token);
        Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var valid = await http.GetAsync(new Uri(receiver.RedirectUri, "?state=expected&code=private-code"), timeout.Token);
        Assert.AreEqual(HttpStatusCode.OK, valid.StatusCode);
        var html = await valid.Content.ReadAsStringAsync(timeout.Token);
        Assert.IsFalse(html.Contains("private-code", StringComparison.Ordinal));
        Assert.AreEqual("private-code", CalendarOAuthCallback.Parameter(await callback, "code"));
    }

    [TestMethod]
    public async Task LoopbackWaitingCanBeCancelled()
    {
        using var receiver = new CalendarLoopbackReceiver();
        using var cancellation = new CancellationTokenSource();
        var callback = receiver.WaitAsync("expected", "Return", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await callback);
    }

    [TestMethod]
    public async Task AuthorizationCodeExchangeSendsDesktopPkceFieldsAndReturnsRefreshToken()
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.AreEqual("https://oauth2.googleapis.com/token", request.RequestUri!.AbsoluteUri);
            var body = await request.Content!.ReadAsStringAsync();
            StringAssert.Contains(body, "client_id=desktop-client");
            StringAssert.Contains(body, "grant_type=authorization_code");
            StringAssert.Contains(body, "code_verifier=verifier");
            StringAssert.Contains(body, "redirect_uri=http%3A%2F%2F127.0.0.1%3A1234%2F");
            return Json("""{"access_token":"access","refresh_token":"refresh","expires_in":3600,"scope":"scope.one scope.two"}""");
        }));
        var client = new GoogleCalendarClient(http, "desktop-client", null, "scope.one scope.two");

        var token = await client.RequestTokenAsync(new()
        {
            ["grant_type"] = "authorization_code", ["code"] = "code", ["code_verifier"] = "verifier",
            ["redirect_uri"] = "http://127.0.0.1:1234/"
        }, CancellationToken.None);

        Assert.AreEqual("access", token.Access);
        Assert.AreEqual("refresh", token.Refresh);
        Assert.IsTrue(token.Expires > DateTimeOffset.UtcNow.AddMinutes(59));
    }

    [TestMethod]
    public async Task InvalidRefreshGrantRequestsReconnectInsteadOfBlamingTheSignInCode()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        { Content = new StringContent("""{"error":"invalid_grant"}""") })));
        var client = new GoogleCalendarClient(http, "desktop-client", null, "scope.one");

        var error = await Assert.ThrowsAsync<GoogleCalendarException>(() =>
            client.RequestTokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = "expired" }, CancellationToken.None));

        Assert.AreEqual("Calendar_ErrorReconnect", error.Key);
        Assert.IsFalse(error.IsAuthorizationCodeExchange);
        Assert.AreEqual("invalid_grant", error.OAuthErrorCode);
    }

    [TestMethod]
    public void ReauthorizationKeepsStoredRefreshTokenWhenGoogleOmitsAReplacement()
    {
        Assert.AreEqual("stored", GoogleCalendarClient.ResolveRefreshToken(null, "stored"));
        Assert.AreEqual("stored", GoogleCalendarClient.ResolveRefreshToken(string.Empty, "stored"));
        Assert.AreEqual("returned", GoogleCalendarClient.ResolveRefreshToken("returned", "stored"));
        Assert.IsNull(GoogleCalendarClient.ResolveRefreshToken(null, null));
    }

    [TestMethod]
    public async Task CalendarListPaginatesAndFiltersNonOwnerEntries()
    {
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler(request =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri);
            return Task.FromResult(Json(Query(uri, "pageToken") == "next"
                ? """{"items":[{"id":"owned@example.com","summary":"Work","accessRole":"owner","dataOwner":"me@example.com"}]}"""
                : """{"nextPageToken":"next","items":[{"id":"me@example.com","summary":"Personal","timeZone":"Asia/Jakarta","primary":true,"accessRole":"owner"},{"id":"shared@example.com","summary":"Shared","accessRole":"reader"}]}"""));
        }));
        var client = new GoogleCalendarClient(http, "desktop-client", null, "scope.one");

        var calendars = await client.ReadCalendarsAsync("access", CancellationToken.None);

        Assert.HasCount(2, calendars);
        Assert.AreEqual("Personal", calendars[0].Name);
        Assert.AreEqual("Asia/Jakarta", calendars[0].TimeZone);
        Assert.AreEqual("owned@example.com", calendars[1].Id);
        Assert.HasCount(2, requests);
        StringAssert.Contains(requests[0].Query, "minAccessRole=owner");
        Assert.AreEqual("next", Query(requests[1], "pageToken"));
    }

    [TestMethod]
    public async Task EventPagesPreserveBoundaryEventsAndOnlyReadCalendarsOwnedByTheAccount()
    {
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler(request =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri);
            if (uri.AbsolutePath.Contains("owned", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json("""{"items":[{"id":"owned-event","summary":"Work event","start":{"dateTime":"2026-10-01T03:00:00Z"},"end":{"dateTime":"2026-10-01T04:00:00Z"}}]}"""));

            return Task.FromResult(Json(Query(uri, "pageToken") == "events-next"
                ? """{"items":[{"id":"inside","summary":"Inside","start":{"dateTime":"2026-10-01T01:00:00Z"},"end":{"dateTime":"2026-10-01T02:00:00Z"}},{"id":"outside","summary":"Outside","start":{"dateTime":"2026-10-11T00:00:00Z"},"end":{"dateTime":"2026-10-11T01:00:00Z"}}]}"""
                : """{"nextPageToken":"events-next","items":[{"id":"boundary","summary":"Boundary","start":{"dateTime":"2026-09-30T23:00:00Z"},"end":{"dateTime":"2026-10-01T00:00:00Z"}}]}"""));
        }));
        var client = new GoogleCalendarClient(http, "desktop-client", null, "scope.one");
        var calendars = new List<GoogleCalendar>
        {
            new("me@example.com", "Personal", "UTC", true, null),
            new("owned@example.com", "Work", "UTC", false, "me@example.com"),
            new("shared@example.com", "Shared", "UTC", false, "someone-else@example.com")
        };
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(10);

        var snapshot = await client.ReadEventsAsync("account", "me@example.com", calendars, start, end,
            (url, ct) => client.ReadAsync(url, "access", ct), CancellationToken.None);

        Assert.HasCount(3, snapshot.Events);
        CollectionAssert.AreEquivalent(new[] { "boundary", "inside", "owned-event" }, snapshot.Events.Select(e => e.Id).ToArray());
        Assert.HasCount(2, snapshot.Sources);
        Assert.HasCount(3, requests);
        Assert.AreEqual(start.AddSeconds(-1).ToString("O"), Query(requests[0], "timeMin"));
        Assert.AreEqual("events-next", Query(requests[1], "pageToken"));
        Assert.IsFalse(requests.Any(uri => uri.AbsolutePath.Contains("shared", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CalendarApiUnauthorizedResponseIsReportedAsReconnect()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent("{}") })));
        var client = new GoogleCalendarClient(http, "desktop-client", null, "scope.one");

        var error = await Assert.ThrowsAsync<GoogleCalendarException>(() =>
            client.ReadAsync("https://www.googleapis.com/calendar/v3/users/me/calendarList", "access", CancellationToken.None));

        Assert.AreEqual("Calendar_ErrorReconnect", error.Key);
        Assert.AreEqual(HttpStatusCode.Unauthorized, error.Status);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static string? Query(Uri uri, string name) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .Where(pair => Uri.UnescapeDataString(pair[0]) == name)
        .Select(pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty).FirstOrDefault();

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
