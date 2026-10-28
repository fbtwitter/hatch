using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hatch.Models;

namespace Hatch.Helpers;

// Bounded RFC5545 snapshot reader. Unsupported recurrence/timezone forms fail the entire import.
internal static partial class IcsCalendarImport
{
    internal const int MaxFileBytes = 4 * 1024 * 1024;
    private const int MaxOccurrences = 10000;

    internal static CalendarAccount Parse(string text, string fileName, string accountId, DateTimeOffset now)
    {
        try { return Read(text, fileName, accountId, now); }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new FormatException("Calendar_ImportInvalid", ex); }
    }

    private static CalendarAccount Read(string text, string fileName, string accountId, DateTimeOffset now)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes) throw new FormatException("Calendar_ImportTooLarge");
        var lines = new List<StringBuilder>();
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        var physicalLines = 0;
        while (reader.ReadLine() is { } line)
        {
            if (++physicalLines > 50000) throw new FormatException("Calendar_ImportTooLarge");
            if (line.StartsWith(' ') || line.StartsWith('\t'))
            {
                if (lines.Count == 0) throw Invalid();
                lines[^1].Append(line.AsSpan(1));
            }
            else if (line.Length > 0) lines.Add(new(line));
        }
        var stack = new Stack<string>();
        var events = new List<EventData>();
        List<Property>? properties = null;
        string? calendarName = null, version = null, method = null;
        var calendarCount = 0;
        foreach (var line in lines)
        {
            var property = Property.Read(line.ToString());
            if (property.Name == "BEGIN")
            {
                var component = property.Value.ToUpperInvariant();
                if (stack.Count == 0)
                {
                    if (component != "VCALENDAR" || ++calendarCount != 1) throw Invalid();
                }
                if (component == "VEVENT")
                {
                    if (stack.Count != 1 || stack.Peek() != "VCALENDAR") throw Invalid();
                    properties = [];
                }
                if (stack.Count > 16) throw Invalid();
                stack.Push(component);
            }
            else if (property.Name == "END")
            {
                if (stack.Count == 0 || stack.Pop() != property.Value.ToUpperInvariant()) throw Invalid();
                if (property.Value.Equals("VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    if (events.Count >= 5000 || properties == null) throw Invalid();
                    events.Add(EventData.Read(properties, events.Count));
                    properties = null;
                }
            }
            else if (stack.Count == 1 && stack.Peek() == "VCALENDAR")
            {
                if (property.Name == "VERSION") version = property.Value;
                if (property.Name == "X-WR-CALNAME") calendarName = Unescape(property.Value);
                if (property.Name == "METHOD") method = property.Value;
            }
            else if (stack.Count == 2 && stack.Peek() == "VEVENT") properties!.Add(property);
            else if (stack.Count == 0) throw Invalid();
        }
        if (stack.Count != 0 || calendarCount != 1 || version != "2.0") throw Invalid();

        var start = new DateTimeOffset(now.LocalDateTime.Date.AddDays(-7)).ToUniversalTime();
        var end = new DateTimeOffset(now.LocalDateTime.Date.AddDays(31)).ToUniversalTime();
        var name = string.IsNullOrWhiteSpace(calendarName) ? Path.GetFileNameWithoutExtension(fileName) : calendarName;
        var result = new Dictionary<string, CalendarEvent>(StringComparer.Ordinal);
        var budget = new Budget();
        if (!string.Equals(method, "CANCEL", StringComparison.OrdinalIgnoreCase))
        foreach (var series in events.GroupBy(e => e.Uid, StringComparer.Ordinal))
        {
            var latest = series.GroupBy(e => e.RecurrenceId?.Key ?? string.Empty)
                .Select(g => g.OrderBy(e => e.Sequence).ThenBy(e => e.Stamp, StringComparer.Ordinal).ThenBy(e => e.Index).Last()).ToList();
            var master = latest.FirstOrDefault(e => e.RecurrenceId == null);
            if (master?.Cancelled == true) continue;
            var overrides = latest.Where(e => e.RecurrenceId != null).ToDictionary(e => e.RecurrenceId!.Key, StringComparer.Ordinal);
            if (master != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var date in Dates(master, end, budget))
                {
                    if (!seen.Add(date.Key) || master.Exclusions.Contains(date.Key) || overrides.ContainsKey(date.Key)) continue;
                    Add(master, date, master.Rule != null || master.Dates.Count > 0 ? date.Key : null, null);
                }
            }
            foreach (var replacement in overrides.Values)
                if (!replacement.Cancelled) Add(replacement, replacement.Start!, replacement.RecurrenceId!.Key, master);
        }
        return new CalendarAccount
        {
            Id = accountId, Provider = CalendarProvider.Ics, Email = fileName, DefaultCalendarId = "import",
            WindowStart = start, WindowEnd = end, LastSyncedAt = now,
            Calendars = [new CalendarSource { Id = "import", Name = name }], Events = result.Values.OrderBy(e => e.Start).ToList()
        };

        void Add(EventData source, DateValue date, string? occurrenceKey, EventData? master)
        {
            var begins = date.Instant;
            var ends = source.EndAt(date, master);
            if (ends < begins) throw Invalid();
            DateOnly? first = date.AllDay ? DateOnly.FromDateTime(date.Value) : null;
            DateOnly? last = date.AllDay ? DateOnly.FromDateTime(ends.UtcDateTime) : null;
            if (date.AllDay ? last <= DateOnly.FromDateTime(start.LocalDateTime) || first >= DateOnly.FromDateTime(end.LocalDateTime)
                : ends < start || begins >= end) return;
            var identity = source.Uid + (occurrenceKey == null ? string.Empty : "|" + occurrenceKey);
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            result[id] = new CalendarEvent
            {
                Id = id, AccountId = accountId, CalendarId = "import", CalendarName = name,
                Subject = source.Summary ?? master?.Summary ?? string.Empty, Start = begins, End = ends,
                IsAllDay = date.AllDay, AllDayStart = first, AllDayEnd = last,
                EventUrl = CalendarEventParser.HttpsUrl(source.Url ?? master?.Url),
                MeetingUrl = MeetingUrl(source) ?? (master == null ? null : MeetingUrl(master))
            };
            if (result.Count > MaxOccurrences) throw new FormatException("Calendar_ImportTooMany");
        }
    }

    private static IEnumerable<DateValue> Dates(EventData item, DateTimeOffset end, Budget budget)
    {
        var origin = item.Start!;
        yield return origin;
        if (item.Rule is { } rule)
        {
            var matched = 1;
            for (var day = origin.Value.Date.AddDays(1); day <= end.UtcDateTime.Date.AddDays(2); day = day.AddDays(1))
            {
                budget.Spend();
                if (rule.Count is { } count && matched >= count) break;
                var date = origin with { Value = day + origin.Value.TimeOfDay };
                if (!date.AllDay && date.Zone.IsInvalidTime(date.Value)) continue;
                if (rule.Until is { } until && (until.AllDay ? date.Value.Date > until.Value.Date : date.Instant > until.Instant)) break;
                if (!rule.Matches(day, origin.Value)) continue;
                matched++;
                yield return date;
            }
        }
        foreach (var date in item.Dates) yield return date;
    }

    private sealed class Budget
    {
        private int _count;
        public void Spend() { if (++_count > 500000) throw new FormatException("Calendar_ImportTooMany"); }
    }

    private sealed record DateValue(DateTime Value, TimeZoneInfo Zone, bool AllDay)
    {
        public DateTimeOffset Instant
        {
            get
            {
                if (AllDay) return new DateTimeOffset(DateTime.SpecifyKind(Value, DateTimeKind.Utc));
                if (Zone.IsInvalidTime(Value)) throw Invalid();
                // RFC5545 selects the first occurrence of an ambiguous local time.
                var offset = Zone.IsAmbiguousTime(Value) ? Zone.GetAmbiguousTimeOffsets(Value).Max() : Zone.GetUtcOffset(Value);
                return new DateTimeOffset(Value, offset).ToUniversalTime();
            }
        }
        public string Key => AllDay ? Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : Instant.ToString("O", CultureInfo.InvariantCulture);
        public static DateValue Read(Property property, string? value = null, TimeZoneInfo? fallback = null)
        {
            value ??= property.Value;
            var allDay = value.Length == 8 && !value.Contains('T');
            var utc = value.EndsWith('Z');
            var format = allDay ? "yyyyMMdd" : utc ? "yyyyMMdd'T'HHmmss'Z'" : "yyyyMMdd'T'HHmmss";
            if (!DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw Invalid();
            if (property.Parameters.TryGetValue("VALUE", out var kind) &&
                !kind.Equals(allDay ? "DATE" : "DATE-TIME", StringComparison.OrdinalIgnoreCase)) throw Invalid();
            var zone = allDay || utc ? TimeZoneInfo.Utc : property.Parameters.TryGetValue("TZID", out var id)
                ? TimeZoneInfo.FindSystemTimeZoneById(id) : fallback ?? TimeZoneInfo.Local;
            return new(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), zone, allDay);
        }
    }

    private sealed record Property(string Name, Dictionary<string, string> Parameters, string Value)
    {
        public static Property Read(string line)
        {
            var parts = new List<string>();
            var quoted = false;
            var from = 0;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') quoted = !quoted;
                if (quoted || (line[i] != ';' && line[i] != ':')) continue;
                parts.Add(line[from..i]);
                from = i + 1;
                if (line[i] != ':') continue;
                var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var part in parts.Skip(1))
                {
                    var pair = part.Split('=', 2);
                    if (pair.Length != 2 || !parameters.TryAdd(pair[0], pair[1].Trim('"'))) throw Invalid();
                }
                return new(parts[0].ToUpperInvariant(), parameters, line[from..]);
            }
            throw Invalid();
        }
    }

    private sealed class EventData
    {
        public required string Uid { get; init; }
        public DateValue? Start { get; init; }
        public DateValue? End { get; init; }
        public DateValue? RecurrenceId { get; init; }
        public int Sequence { get; init; }
        public int Index { get; init; }
        public string Stamp { get; init; } = string.Empty;
        public bool Cancelled { get; init; }
        public string? Summary { get; init; }
        public string? Url { get; init; }
        public string? Description { get; init; }
        public string? Location { get; init; }
        public string? Conference { get; init; }
        public (int Days, TimeSpan Time)? Duration { get; init; }
        public Rule? Rule { get; init; }
        public List<DateValue> Dates { get; } = [];
        public HashSet<string> Exclusions { get; } = new(StringComparer.Ordinal);

        public static EventData Read(List<Property> properties, int index)
        {
            if (properties.Any(p => p.Name == "EXRULE")) throw Invalid();
            Property? Field(string name)
            {
                var matches = properties.Where(p => p.Name == name).ToArray();
                if (matches.Length > 1) throw Invalid();
                return matches.SingleOrDefault();
            }
            string? Text(string name)
            {
                var field = Field(name);
                if (field?.Parameters.ContainsKey("ENCODING") == true) throw Invalid();
                return field == null ? null : Unescape(field.Value);
            }
            var uid = Text("UID");
            if (string.IsNullOrWhiteSpace(uid)) throw Invalid();
            var cancelled = Text("STATUS")?.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) == true;
            var recurrence = Field("RECURRENCE-ID");
            if (recurrence?.Parameters.TryGetValue("RANGE", out var range) == true && range != "THISINSTANCE") throw Invalid();
            var rid = recurrence == null ? null : DateValue.Read(recurrence);
            var start = Field("DTSTART") is { } startField ? DateValue.Read(startField) : cancelled ? rid : null;
            if (!cancelled && start == null) throw Invalid();
            var end = Field("DTEND") is { } endField ? DateValue.Read(endField) : null;
            (int Days, TimeSpan Time)? duration = null;
            if (Text("DURATION") is { } durationText)
            {
                var match = DurationPattern().Match(durationText);
                if (!match.Success || durationText is "P" or "PT") throw Invalid();
                int Number(string key) => match.Groups[key].Success ? int.Parse(match.Groups[key].Value, CultureInfo.InvariantCulture) : 0;
                duration = (checked(Number("w") * 7 + Number("d")),
                    TimeSpan.FromHours(Number("h")) + TimeSpan.FromMinutes(Number("m")) + TimeSpan.FromSeconds(Number("s")));
            }
            if (start != null && (end != null && (end.AllDay != start.AllDay || end.Instant < start.Instant ||
                start.AllDay && end.Value <= start.Value) || end != null && duration != null ||
                start.AllDay && duration is { } days && (days.Time != TimeSpan.Zero || days.Days < 1))) throw Invalid();
            var sequence = Text("SEQUENCE");
            var item = new EventData
            {
                Uid = uid, Start = start, End = end, RecurrenceId = rid, Cancelled = cancelled,
                Sequence = sequence == null ? 0 : int.Parse(sequence, CultureInfo.InvariantCulture),
                Stamp = Text("DTSTAMP") ?? string.Empty, Index = index, Summary = Text("SUMMARY"), Url = Text("URL"),
                Description = Text("DESCRIPTION"), Location = Text("LOCATION"),
                Conference = Text("X-MICROSOFT-SKYPETEAMSMEETINGURL") ?? Text("X-GOOGLE-CONFERENCE"),
                Duration = duration, Rule = Field("RRULE") is { } rule && start != null ? Rule.Read(rule.Value, start) : null
            };
            foreach (var field in properties.Where(p => p.Name is "RDATE" or "EXDATE"))
                foreach (var value in field.Value.Split(','))
                {
                    var date = DateValue.Read(field, value);
                    if (start != null && date.AllDay != start.AllDay) throw Invalid();
                    if (field.Name == "RDATE") item.Dates.Add(date); else item.Exclusions.Add(date.Key);
                }
            return item;
        }

        public DateTimeOffset EndAt(DateValue date, EventData? master)
        {
            if (End != null && Start != null)
                return date.AllDay ? date.Instant.AddDays((End.Value.Date - Start.Value.Date).Days) : date.Instant + (End.Instant - Start.Instant);
            if (Duration is { } duration)
                return (date with { Value = date.Value.AddDays(duration.Days) }).Instant + duration.Time;
            return master != null ? master.EndAt(date, null) : date.AllDay ? date.Instant.AddDays(1) : date.Instant;
        }
    }

    private sealed class Rule
    {
        public required string Frequency { get; init; }
        public int Interval { get; init; } = 1;
        public int? Count { get; init; }
        public DateValue? Until { get; init; }
        public DayOfWeek WeekStart { get; init; } = DayOfWeek.Monday;
        public int[] Months { get; init; } = [];
        public int[] MonthDays { get; init; } = [];
        public (int Ordinal, DayOfWeek Day)[] Days { get; init; } = [];
        public int[] Positions { get; init; } = [];
        private readonly Dictionary<(int, int), HashSet<int>> _positions = [];

        public static Rule Read(string text, DateValue origin)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in text.ToUpperInvariant().Split(';'))
            {
                var pair = part.Split('=', 2);
                if (pair.Length != 2 || !fields.TryAdd(pair[0], pair[1]) ||
                    pair[0] is not ("FREQ" or "INTERVAL" or "COUNT" or "UNTIL" or "WKST" or "BYMONTH" or "BYMONTHDAY" or "BYDAY" or "BYSETPOS")) throw Invalid();
            }
            if (!fields.TryGetValue("FREQ", out var freq) || freq is not ("DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY") ||
                fields.ContainsKey("COUNT") && fields.ContainsKey("UNTIL")) throw Invalid();
            int Positive(string key, int fallback) => fields.TryGetValue(key, out var value) &&
                int.TryParse(value, out var number) && number > 0 ? number : fields.ContainsKey(key) ? throw Invalid() : fallback;
            int[] Numbers(string key, int limit, bool negative) => fields.TryGetValue(key, out var value)
                ? value.Split(',').Select(v => int.TryParse(v, out var n) && n != 0 && n <= limit && n >= (negative ? -limit : 1)
                    ? n : throw Invalid()).Distinct().ToArray() : [];
            var rule = new Rule
            {
                Frequency = freq, Interval = Positive("INTERVAL", 1),
                Count = fields.ContainsKey("COUNT") ? Positive("COUNT", 0) : null,
                Until = fields.TryGetValue("UNTIL", out var until) ? DateValue.Read(new("UNTIL", [], until), fallback: origin.Zone) : null,
                Months = Numbers("BYMONTH", 12, false), MonthDays = Numbers("BYMONTHDAY", 31, true),
                Positions = Numbers("BYSETPOS", 366, true),
                WeekStart = fields.TryGetValue("WKST", out var week) ? Weekday(week) : DayOfWeek.Monday,
                Days = fields.TryGetValue("BYDAY", out var days) ? days.Split(',').Select(v =>
                {
                    var match = WeekdayPattern().Match(v);
                    if (!match.Success) throw Invalid();
                    return (match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0, Weekday(match.Groups[2].Value));
                }).ToArray() : []
            };
            if (rule.Positions.Length > 0 && (freq != "MONTHLY" && !(freq == "YEARLY" && rule.Months.Length == 1) ||
                rule.Days.Length == 0 && rule.MonthDays.Length == 0) ||
                rule.Days.Any(d => d.Ordinal != 0) && (freq is "DAILY" or "WEEKLY" || freq == "YEARLY" && rule.Months.Length == 0) ||
                freq == "WEEKLY" && rule.MonthDays.Length > 0) throw Invalid();
            return rule;
        }

        public bool Matches(DateTime day, DateTime origin)
        {
            if (!Basic(day, origin)) return false;
            if (Positions.Length == 0) return true;
            var key = (day.Year, day.Month);
            if (!_positions.TryGetValue(key, out var selected))
            {
                var choices = Enumerable.Range(1, DateTime.DaysInMonth(day.Year, day.Month))
                    .Select(n => new DateTime(day.Year, day.Month, n)).Where(d => Basic(d, origin)).Select(d => d.Day).ToArray();
                selected = Positions.Select(p => p > 0 ? p - 1 : choices.Length + p)
                    .Where(i => i >= 0 && i < choices.Length).Select(i => choices[i]).ToHashSet();
                _positions[key] = selected;
            }
            return selected.Contains(day.Day);
        }

        private bool Basic(DateTime day, DateTime origin)
        {
            var months = (day.Year - origin.Year) * 12 + day.Month - origin.Month;
            if (Frequency == "DAILY" && (day - origin.Date).Days % Interval != 0 ||
                Frequency == "MONTHLY" && months % Interval != 0 ||
                Frequency == "YEARLY" && (day.Year - origin.Year) % Interval != 0) return false;
            if (Frequency == "WEEKLY")
            {
                DateTime Week(DateTime d) => d.Date.AddDays(-((7 + (int)d.DayOfWeek - (int)WeekStart) % 7));
                if ((Week(day) - Week(origin)).Days / 7 % Interval != 0 ||
                    Days.Length == 0 && day.DayOfWeek != origin.DayOfWeek) return false;
            }
            if (Months.Length > 0 && !Months.Contains(day.Month) ||
                Frequency == "YEARLY" && Months.Length == 0 && MonthDays.Length == 0 && Days.Length == 0 && day.Month != origin.Month) return false;
            var length = DateTime.DaysInMonth(day.Year, day.Month);
            if (MonthDays.Length > 0 && !MonthDays.Any(d => day.Day == (d > 0 ? d : length + d + 1))) return false;
            if (Days.Length > 0 && !Days.Any(d => day.DayOfWeek == d.Day &&
                (d.Ordinal == 0 || d.Ordinal > 0 && (day.Day - 1) / 7 + 1 == d.Ordinal ||
                 d.Ordinal < 0 && -((length - day.Day) / 7 + 1) == d.Ordinal))) return false;
            return Frequency is not ("MONTHLY" or "YEARLY") || MonthDays.Length > 0 || Days.Length > 0 || day.Day == origin.Day;
        }
    }

    private static DayOfWeek Weekday(string value) => value switch
    {
        "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday, "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday,
        _ => throw Invalid()
    };
    private static FormatException Invalid() => new("Calendar_ImportInvalid");

    private static string Unescape(string value) => TextEscapes().Replace(value, m => m.Groups[1].Value is "n" or "N" ? "\n" : m.Groups[1].Value);

    private static string? MeetingUrl(EventData item)
    {
        foreach (var value in new[] { item.Conference, item.Url, item.Location, item.Description }.Where(v => v != null))
            foreach (Match match in HttpsLinks().Matches(WebUtility.HtmlDecode(value!)))
            {
                var link = CalendarEventParser.HttpsUrl(match.Value.TrimEnd('.', ',', ')', ';'));
                if (link == null) continue;
                var host = new Uri(link).Host.ToLowerInvariant();
                if (host is "teams.microsoft.com" or "teams.live.com" or "teams.cloud.microsoft" or "meet.google.com" ||
                    host == "zoom.us" || host.EndsWith(".zoom.us", StringComparison.Ordinal) ||
                    host == "webex.com" || host.EndsWith(".webex.com", StringComparison.Ordinal)) return link;
            }
        return null;
    }

    [GeneratedRegex(@"\\([nN,;\\])", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TextEscapes();
    [GeneratedRegex(@"^([+-]?[1-5])?(MO|TU|WE|TH|FR|SA|SU)$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex WeekdayPattern();
    [GeneratedRegex(@"^P(?:(?<w>\d+)W|(?:(?<d>\d+)D)?(?:T(?:(?<h>\d+)H)?(?:(?<m>\d+)M)?(?:(?<s>\d+)S)?)?)$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex DurationPattern();
    [GeneratedRegex("https://[^\\s<>\"\\\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex HttpsLinks();
}
