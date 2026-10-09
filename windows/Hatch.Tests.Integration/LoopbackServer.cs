using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Hatch.Tests.Integration;

// Minimal HTTP fixture, bound to loopback only. Real Supabase SDK requests reach it.
internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    internal ConcurrentQueue<string> Requests { get; } = new();
    internal Func<string, (int Status, string Body)> Respond { get; set; } = _ => (200, "[]");
    internal string Url { get; }

    internal LoopbackServer()
    {
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _loop = ServeAsync();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var request = await reader.ReadLineAsync(_stop.Token) ?? "";
                int length = 0;
                string? header;
                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(_stop.Token)))
                {
                    if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        length = int.Parse(header[15..].Trim());
                }
                // Tests send ASCII encrypted envelopes, so chars equal bytes here.
                if (length > 0)
                {
                    var body = new char[length];
                    await reader.ReadBlockAsync(body.AsMemory(), _stop.Token);
                }
                Requests.Enqueue(request);
                var response = Respond(request);
                var bytes = Encoding.UTF8.GetBytes(response.Body);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {response.Status} Test\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, _stop.Token);
                await stream.WriteAsync(bytes, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _loop;
        _stop.Dispose();
    }
}
