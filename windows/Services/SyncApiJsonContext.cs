using System.Text.Json.Serialization;

namespace Hatch.Services;

[JsonSerializable(typeof(SyncSession))]
[JsonSerializable(typeof(SyncUser))]
[JsonSerializable(typeof(SyncFactor))]
[JsonSerializable(typeof(UserDataRow[]))]
[JsonSerializable(typeof(UserDataRow))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(bool))]
internal partial class SyncApiJsonContext : JsonSerializerContext;

internal sealed class SyncSession
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
    [JsonPropertyName("user")]
    public SyncUser? User { get; set; }
}

internal sealed class SyncUser
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonPropertyName("email")]
    public string? Email { get; set; }
    [JsonPropertyName("factors")]
    public List<SyncFactor>? Factors { get; set; } = [];
}

internal sealed class SyncFactor
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    [JsonPropertyName("factor_type")]
    public string? FactorType { get; set; }
    [JsonPropertyName("totp")]
    public SyncTotp? Totp { get; set; }
}

internal sealed class SyncTotp
{
    [JsonPropertyName("secret")]
    public string? Secret { get; set; }
    [JsonPropertyName("qr_code")]
    public string? QrCode { get; set; }
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}
