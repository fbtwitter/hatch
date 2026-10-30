using System.Net;
using System.Net.Sockets;
using System.Text;
using Hatch.Helpers;

namespace Hatch.Services;

internal sealed partial class CalendarLoopbackReceiver : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    internal Uri RedirectUri { get; }

    internal CalendarLoopbackReceiver(bool useLocalhost = false)
    {
        _listener.ExclusiveAddressUse = true;
        _listener.Start();
        var host = useLocalhost ? "localhost" : "127.0.0.1";
        RedirectUri = new Uri($"http://{host}:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
    }

    internal async Task<Uri> WaitAsync(string state, Func<bool, (string Html, string Nonce)> completionPage, CancellationToken ct)
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
                var status = callback == null ? "400 Bad Request" : "200 OK";
                var document = callback == null ? default : completionPage(CalendarOAuthCallback.Parameter(callback, "code") != null);
                var body = document.Html == null ? [] : Encoding.UTF8.GetBytes(document.Html);
                var nonce = document.Nonce ?? string.Empty;
                var headers = Encoding.ASCII.GetBytes("HTTP/1.1 " + status +
                    "\r\nCache-Control: no-store\r\nPragma: no-cache" +
                    "\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff" +
                    "\r\nContent-Security-Policy: default-src 'none'; img-src data:; style-src 'nonce-" + nonce + "'; script-src 'nonce-" + nonce + "'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'" +
                    "\r\nContent-Type: text/html; charset=utf-8" +
                    "\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n");
                await stream.WriteAsync(headers, requestTimeout.Token);
                if (body.Length > 0) await stream.WriteAsync(body, requestTimeout.Token);
                if (callback != null) return callback;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (IOException) when (!ct.IsCancellationRequested) { }
        }
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
