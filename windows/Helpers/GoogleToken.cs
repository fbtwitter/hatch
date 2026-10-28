namespace Hatch.Helpers;

internal sealed record GoogleToken(string Access, string? Refresh, DateTimeOffset Expires);
