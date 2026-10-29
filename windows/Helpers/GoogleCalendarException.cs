using System.Net;

namespace Hatch.Helpers;

internal sealed class GoogleCalendarException : Exception
{
    internal GoogleCalendarException(string key, HttpStatusCode? status = null, DateTimeOffset? retryAt = null,
        bool isAuthorizationCodeExchange = false, string? oauthErrorCode = null) : base(key)
    {
        Key = key;
        Status = status;
        RetryAt = retryAt;
        IsAuthorizationCodeExchange = isAuthorizationCodeExchange;
        OAuthErrorCode = oauthErrorCode;
    }

    internal string Key { get; }
    internal HttpStatusCode? Status { get; }
    internal DateTimeOffset? RetryAt { get; }
    internal bool IsAuthorizationCodeExchange { get; }
    internal string? OAuthErrorCode { get; }
}
