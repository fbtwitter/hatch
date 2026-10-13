using System.Text.Json.Serialization;

namespace Hatch.Models;

[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;
