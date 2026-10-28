using System.Text.Json.Serialization;

namespace Hatch.Models;

[JsonSerializable(typeof(List<CalendarAccount>))]
internal partial class CalendarJsonContext : JsonSerializerContext;
