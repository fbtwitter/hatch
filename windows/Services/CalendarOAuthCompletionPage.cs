using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Reflection;
using Hatch.Helpers;

namespace Hatch.Services;

internal static class CalendarOAuthCompletionPage
{
    private static readonly string Logo = LoadLogo();

    internal static (string Html, string Nonce) Render(string provider, bool succeeded)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var status = succeeded
            ? $"<h1>{HtmlEncoder.Default.Encode(Strings.Get("Calendar_OAuthSignedIn"))}</h1>" +
              $"<p>{string.Format(Strings.Get("Calendar_OAuthGranted"), HtmlEncoder.Default.Encode(provider))}</p>" +
              $"<p>{HtmlEncoder.Default.Encode(Strings.Get("Calendar_OAuthClose"))}</p>"
            : $"<h1>{HtmlEncoder.Default.Encode(Strings.Get("Calendar_OAuthFailedTitle"))}</h1>" +
              $"<p>{HtmlEncoder.Default.Encode(Strings.Get("Calendar_OAuthFailed"))}</p>";

        var html = $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{{HtmlEncoder.Default.Encode(Strings.Get(succeeded ? "Calendar_OAuthAuthorizationComplete" : "Calendar_OAuthFailedTitle"))}}</title>
    <style nonce="{{nonce}}">
    :root { color-scheme: light dark; font-family: "Segoe UI Variable", "Segoe UI", sans-serif; }
    body { min-height: 100vh; margin: 0; display: grid; place-items: center; background: Canvas; color: CanvasText; }
    main { box-sizing: border-box; width: min(440px, calc(100% - 48px)); padding: 32px; border: 1px solid color-mix(in srgb, CanvasText 14%, transparent); border-radius: 16px; }
    .brand { display: flex; align-items: center; gap: 12px; margin-bottom: 32px; font-size: 18px; font-weight: 600; }
    .logo { width: 48px; height: 48px; }
    .eyebrow { color: color-mix(in srgb, CanvasText 65%, transparent); font-size: 13px; }
    h1 { margin: 8px 0 16px; font-size: 26px; font-weight: 600; }
    p { line-height: 1.55; }
  </style>
</head>
<body>
  <main>
    <div class="brand"><img class="logo" alt="Hatch" src="{{Logo}}">Hatch</div>
    <p class="eyebrow">{{HtmlEncoder.Default.Encode(Strings.Get(succeeded ? "Calendar_OAuthAuthorizationComplete" : "Calendar_OAuthSignIn"))}}</p>
    {{status}}
  </main>
  <script nonce="{{nonce}}">history.replaceState(null, "", "/");</script>
</body>
</html>
""";
        return (html, nonce);
    }

    private static string LoadLogo()
    {
        using var stream = typeof(CalendarOAuthCompletionPage).Assembly.GetManifestResourceStream("HatchLogo.svg")
            ?? throw new InvalidOperationException("Hatch logo resource is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(buffer.ToArray());
    }
}
