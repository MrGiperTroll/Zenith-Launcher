using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

/// <summary>
/// Minimal loopback HTTP listener built on TcpListener - unlike HttpListener this
/// needs no URL ACL / admin rights to bind 127.0.0.1. Shared by the Microsoft
/// and Ely.by OAuth flows to catch the browser redirect containing the code.
/// </summary>
public static class LoopbackCodeListener
{
    public static async Task<Dictionary<string, string>> WaitForCallbackAsync(
        int port, string expectedState, string successHtml, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"Could not listen on localhost:{port}. Is another instance of Zenith running?", ex);
        }

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                using TcpClient client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                var query = await ReadCallbackQueryAsync(client, ct).ConfigureAwait(false);
                if (query == null)
                    continue; // favicon.ico and other noise

                if (query.TryGetValue("error", out var error))
                {
                    var desc = query.TryGetValue("error_description", out var d) ? d : "";
                    throw new InvalidOperationException($"OAuth error: {error} {desc}".Trim());
                }

                if (!query.TryGetValue("state", out var state) ||
                    !string.Equals(state, expectedState, StringComparison.Ordinal))
                    throw new InvalidOperationException("OAuth: state mismatch.");

                if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                    continue;

                WriteHtmlResponse(client, successHtml);

                return query;
            }
        }
        finally
        {
            try { listener.Stop(); } catch { }
        }
    }

    /// <summary>Reads one HTTP request from the loopback connection and returns its query parameters.</summary>
    private static async Task<Dictionary<string, string>?> ReadCallbackQueryAsync(TcpClient client, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        var stream = client.GetStream();
        var buffer = new byte[8192];
        var sb = new StringBuilder();

        while (!sb.ToString().Contains("\r\n\r\n"))
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token).ConfigureAwait(false);
            if (read == 0) return null;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (sb.Length > 65536) return null; // absurd request - give up
        }

        var request = sb.ToString();
        var lineEnd = request.IndexOf("\r\n", StringComparison.Ordinal);
        if (lineEnd < 0) return null;
        var requestLine = request[..lineEnd]; // GET /auth/callback?code=x&state=y HTTP/1.1

        var parts = requestLine.Split(' ');
        if (parts.Length < 2) return null;
        var rawUrl = parts[1];

        var qIndex = rawUrl.IndexOf('?');
        if (qIndex < 0)
        {
            WriteHtmlResponse(client, "<html><body></body></html>");
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in rawUrl[(qIndex + 1)..].Split('&'))
        {
            if (pair.Length == 0) continue;
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair[..eq];
            var value = eq < 0 ? "" : pair[(eq + 1)..];
            result[Unescape(key)] = Unescape(value);
        }
        return result;
    }

    private static string Unescape(string s) => Uri.UnescapeDataString(s.Replace("+", " "));

    private static void WriteHtmlResponse(TcpClient client, string html)
    {
        try
        {
            var body = Encoding.UTF8.GetBytes(html);
            var header = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                "Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "Connection: close\r\n\r\n");
            var stream = client.GetStream();
            stream.Write(header, 0, header.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }
        catch { }
    }
}
