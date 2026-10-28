using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Hatch.Helpers;

namespace Hatch.Services;

internal sealed partial class CalendarLoopbackReceiver : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    internal Uri RedirectUri { get; }

    internal CalendarLoopbackReceiver()
    {
        _listener.ExclusiveAddressUse = true;
        _listener.Start();
        RedirectUri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
    }

    internal async Task<Uri> WaitAsync(string state, string completionMessage, CancellationToken ct)
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(ct);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using var stream = client.GetStream();
            try
            {
                var callback = await ReadCallbackAsync(stream, state, requestTimeout.Token);
                var denied = callback != null && CalendarOAuthCallback.Parameter(callback, "error") != null;
                var title = callback == null ? Strings.Get("Calendar_Callback_InvalidHeading") :
                    denied ? Strings.Get("Calendar_Callback_DeniedHeading") : Strings.Get("Calendar_Callback_ReceivedHeading");
                var message = callback == null ? Strings.Get("Calendar_Callback_InvalidBody") :
                    denied ? Strings.Get("Calendar_ErrorConsent") : completionMessage;
                var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                var body = CreateCallbackPage(title, message, callback != null && !denied, nonce);
                var headers = Encoding.ASCII.GetBytes("HTTP/1.1 " + (callback == null ? "400 Bad Request" : "200 OK") +
                    "\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nPragma: no-cache" +
                    "\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff" +
                    "\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-" + nonce +
                    "'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'" +
                    "\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n");
                await stream.WriteAsync(headers, requestTimeout.Token);
                await stream.WriteAsync(body, requestTimeout.Token);
                if (callback != null) return callback;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (IOException) when (!ct.IsCancellationRequested) { }
        }
    }

    private static byte[] CreateCallbackPage(string heading, string message, bool success, string nonce)
    {
        var statusClass = success ? "success" : "error";
        var symbol = success ? "✓" : "!";
        var role = success ? "status" : "alert";
        var page = $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <meta name="color-scheme" content="light dark">
                <title>Hatch Calendar</title>
                <script nonce="{{nonce}}">history.replaceState(null, document.title, location.pathname);</script>
                <style>
                    :root { color-scheme: light dark; font: 16px/1.55 "Segoe UI", system-ui, sans-serif; }
                    * { box-sizing: border-box; }
                    body { min-height: 100vh; margin: 0; padding: 24px; display: grid; place-items: center; background: Canvas; color: CanvasText; }
                    main { width: min(100%, 440px); padding: 32px; border: 1px solid GrayText; border-radius: 24px; background: Canvas; box-shadow: 0 18px 48px rgba(0, 0, 0, .12); }
                    .brand { display: flex; align-items: center; gap: 10px; font-size: 15px; font-weight: 650; letter-spacing: .01em; }
                    .brand-mark { display: grid; width: 32px; height: 32px; place-items: center; border-radius: 10px; background: Highlight; color: HighlightText; font-size: 17px; font-weight: 700; }
                    .brand-detail { color: GrayText; font-weight: 450; }
                    .status-mark { display: grid; width: 52px; height: 52px; margin: 32px 0 16px; place-items: center; border: 1px solid Highlight; border-radius: 50%; color: Highlight; font-size: 25px; font-weight: 600; }
                    .status-mark.error { border-color: GrayText; color: GrayText; }
                    h1 { margin: 0; font-size: 26px; line-height: 1.25; letter-spacing: -.02em; }
                    p { max-width: 38ch; margin: 12px 0 0; }
                    .hint { margin-top: 28px; padding-top: 18px; border-top: 1px solid GrayText; color: GrayText; font-size: 14px; }
                    @media (max-width: 480px) { main { padding: 28px 24px; } }
                </style>
            </head>
            <body>
                <main role="{{role}}" aria-labelledby="status-heading">
                    <div class="brand"><span class="brand-mark" aria-hidden="true">h</span><span>Hatch</span><span class="brand-detail">Calendar connection</span></div>
                    <div class="status-mark {{statusClass}}" aria-hidden="true">{{symbol}}</div>
                    <h1 id="status-heading">{{WebUtility.HtmlEncode(heading)}}</h1>
                    <p>{{WebUtility.HtmlEncode(message)}}</p>
                    <div class="hint">You can close this tab when you’re ready to return to Hatch.</div>
                </main>
            </body>
            </html>
            """;
        return Encoding.UTF8.GetBytes(page);
    }

    private async Task<Uri?> ReadCallbackAsync(NetworkStream stream, string state, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), ct);
            if (read == 0) return null;
            length += read;
            var header = Encoding.ASCII.GetString(buffer, 0, length);
            if (!header.Contains("\r\n\r\n", StringComparison.Ordinal)) continue;
            var lines = header.Split("\r\n");
            var request = lines[0].Split(' ');
            if (request.Length != 3 || request[0] != "GET" || !request[1].StartsWith('/') ||
                request[1].StartsWith("//", StringComparison.Ordinal)) return null;
            var host = lines.Skip(1).FirstOrDefault(l => l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))?[5..].Trim();
            if (!string.Equals(host, RedirectUri.Authority, StringComparison.OrdinalIgnoreCase)) return null;
            if (!Uri.TryCreate(RedirectUri, request[1], out var callback) || callback.AbsolutePath != RedirectUri.AbsolutePath ||
                callback.Host != RedirectUri.Host || callback.Port != RedirectUri.Port ||
                !CalendarOAuthCallback.HasMatchingState(callback, state)) return null;
            if (CalendarOAuthCallback.Parameter(callback, "code") == null && CalendarOAuthCallback.Parameter(callback, "error") == null) return null;
            return callback;
        }
        return null;
    }

    public void Dispose() => _listener.Stop();
}
