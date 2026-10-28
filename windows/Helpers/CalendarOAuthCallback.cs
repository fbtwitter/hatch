using System.Security.Cryptography;
using System.Text;

namespace Hatch.Helpers;

internal static class CalendarOAuthCallback
{
    internal static bool IsExpectedRedirect(Uri uri, string expected)
    {
        var redirect = new Uri(expected);
        return uri.IsAbsoluteUri && uri.Scheme == redirect.Scheme && uri.Host == redirect.Host &&
            uri.Port == redirect.Port && uri.AbsolutePath == redirect.AbsolutePath &&
            uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
    }

    internal static bool HasMatchingState(Uri uri, string expected)
    {
        var actual = Parameter(uri, "state");
        return actual != null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
    }

    internal static string? Parameter(Uri uri, string name)
    {
        string? result = null;
        foreach (var part in uri.Query.TrimStart('?').Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || Uri.UnescapeDataString(pair[0]) != name) continue;
            if (result != null) return null;
            result = Uri.UnescapeDataString(pair[1].Replace('+', ' '));
        }
        return result;
    }
}
