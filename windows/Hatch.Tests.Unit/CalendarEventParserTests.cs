using System.Text.Json;
using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.Tests.Unit;

[TestClass]
public sealed class CalendarEventParserTests
{
    private static void Apply(Dictionary<string, CalendarEvent> events, string json)
    {
        using var document = JsonDocument.Parse(json);
        CalendarEventParser.Apply(events, document.RootElement, "account", "calendar", "Work");
    }

    private static string Event(string id = "occurrence-1", string start = "2026-10-26T09:00:00", string extra = "") => $$"""
        {"id":"{{id}}","subject":"Planning","start":{"dateTime":"{{start}}","timeZone":"UTC"},
         "end":{"dateTime":"2026-10-26T10:00:00","timeZone":"UTC"}{{extra}}}
        """;

    [TestMethod]
    public void RepeatedDeltaReplacesTheSameOccurrenceWithoutDuplicatingIt()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, Event());
        Apply(events, Event(start: "2026-10-26T09:30:00"));
        Assert.HasCount(1, events);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 26, 9, 30, 0, TimeSpan.Zero), events["occurrence-1"].Start);
    }

    [TestMethod]
    public void DeletedEventNeedsOnlyItsIdentityToRemoveTheCachedOccurrence()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, Event());
        Apply(events, """{"id":"occurrence-1","@removed":{"reason":"deleted"}}""");
        Assert.IsEmpty(events);
    }

    [TestMethod]
    public void CancelledAndDeclinedEventsRemoveAnExistingOccurrence()
    {
        foreach (var status in new[] { "\"isCancelled\":true", "\"responseStatus\":{\"response\":\"declined\"}" })
        {
            Dictionary<string, CalendarEvent> events = [];
            Apply(events, Event());
            Apply(events, "{\"id\":\"occurrence-1\"," + status + "}");
            Assert.IsEmpty(events);
        }
    }

    [TestMethod]
    public void NullResponseStatusDoesNotBreakEventParsing()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, Event(extra: ",\"responseStatus\":null"));
        Assert.HasCount(1, events);
    }

    [TestMethod]
    public void RecurringOccurrencesRemainDistinct()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, Event(id: "series-occurrence-1"));
        Apply(events, Event(id: "series-occurrence-2"));
        Assert.HasCount(2, events);
    }

    [TestMethod]
    public void AllDayDateUsesTheOriginalZoneRatherThanTheUtcDate()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, """
            {"id":"all-day","isAllDay":true,"originalStartTimeZone":"SE Asia Standard Time",
             "start":{"dateTime":"2026-10-25T17:00:00","timeZone":"UTC"},
             "end":{"dateTime":"2026-10-26T17:00:00","timeZone":"UTC"}}
            """);
        Assert.AreEqual(new DateOnly(2026, 10, 26), events["all-day"].AllDayStart);
        Assert.AreEqual(new DateOnly(2026, 10, 27), events["all-day"].AllDayEnd);
    }

    [TestMethod]
    public void MalformedUpdateDoesNotReplaceThePreviousGoodEvent()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, Event());
        var before = events["occurrence-1"];
        Assert.Throws<JsonException>(() => Apply(events, Event(start: "invalid")));
        Assert.AreSame(before, events["occurrence-1"]);
    }

    [TestMethod]
    public void MeetingLinkUsesOnlineMeetingJoinUrlAndDiscardsTheBody()
    {
        Dictionary<string, CalendarEvent> events = [];
        Apply(events, Event(extra: ",\"webLink\":\"https://outlook.office.com/event\",\"onlineMeeting\":{\"joinUrl\":\"https://teams.microsoft.com/join\"},\"body\":{\"content\":\"CONFIDENTIAL\"}"));
        var result = events["occurrence-1"];
        Assert.AreEqual("https://teams.microsoft.com/join", result.MeetingUrl);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("CONFIDENTIAL", StringComparison.Ordinal));
    }

    [TestMethod]
    public void EventLinksCannotLaunchLocalFilesOrOtherProtocols()
    {
        foreach (var value in new[] { "file:///C:/private.txt", "hatch://complete?id=anything", "javascript:alert(1)", "https://name:password@example.com/" })
            Assert.IsNull(CalendarEventParser.HttpsUrl(value));
        Assert.AreEqual("https://example.com/event", CalendarEventParser.HttpsUrl("https://example.com/event"));
    }
}
