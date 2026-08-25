using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

public sealed class ServerStatusResult
{
    public string Motd { get; set; } = "";
    public string Version { get; set; } = "";
    public int Online { get; set; }
    public int Max { get; set; }
    public byte[]? IconBytes { get; set; }
}

/// <summary>Queries a Minecraft Java server via the Server List Ping / status protocol (handshake + status request).</summary>
public static class MinecraftServerPinger
{
    private const int DefaultPort = 25565;
    private const int MaxPayload = 64 * 1024;

    private static (string Host, int Port) Parse(string hostPort)
    {
        var host = hostPort;
        var port = DefaultPort;
        var idx = hostPort.LastIndexOf(':');
        if (idx > 0 && int.TryParse(hostPort[(idx + 1)..], out var p) && p > 0 && p < 65536)
        {
            host = hostPort[..idx];
            port = p;
        }
        return (host.Trim(), port);
    }

    public static async Task<ServerStatusResult?> PingAsync(string hostPort, int timeoutMs = 4000)
    {
        var (host, port) = Parse(hostPort);
        if (string.IsNullOrWhiteSpace(host)) return null;

        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(host, port);
            var timedOut = await Task.WhenAny(connect, Task.Delay(timeoutMs));
            if (timedOut != connect) return null;
            await connect;
            if (!client.Connected) return null;

            using var stream = client.GetStream();
            stream.ReadTimeout = timeoutMs;
            stream.WriteTimeout = timeoutMs;

            // --- Handshake (state 1 = status) ---
            var hostBytes = Encoding.UTF8.GetBytes(host);
            using (var hs = new MemoryStream())
            {
                WriteVarInt(hs, -1);
                WriteVarInt(hs, hostBytes.Length);
                hs.Write(hostBytes, 0, hostBytes.Length);
                hs.WriteByte((byte)(port >> 8));
                hs.WriteByte((byte)(port & 0xFF));
                WriteVarInt(hs, 1);
                var payload = hs.ToArray();
                WriteVarInt(stream, payload.Length + 1);
                WriteVarInt(stream, 0); // packet id 0x00
                stream.Write(payload, 0, payload.Length);
            }

            // --- Status request (packet id 0x00, empty payload) ---
            stream.WriteByte(1);   // length
            stream.WriteByte(0);   // packet id 0x00
            stream.Flush();

            int len = ReadVarInt(stream); if (len < 0 || len > MaxPayload) return null;
            if (ReadVarInt(stream) != 0) return null;                                  // status response id
            int jsonLen = ReadVarInt(stream); if (jsonLen < 0 || jsonLen > MaxPayload) return null;
            var jsonBytes = new byte[jsonLen];
            var read = 0;
            while (read < jsonLen)
            {
                var n = stream.Read(jsonBytes, read, jsonLen - read);
                if (n <= 0) return null;
                read += n;
            }

            return ParseJson(Encoding.UTF8.GetString(jsonBytes, 0, jsonLen));
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to ping server '{hostPort}'", ex);
            return null;
        }
    }

    private static ServerStatusResult? ParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new ServerStatusResult();
            if (root.TryGetProperty("description", out var desc))
                result.Motd = FlattenChat(desc);
            if (root.TryGetProperty("version", out var ver) && ver.TryGetProperty("name", out var vn))
                result.Version = vn.GetString() ?? "";
            if (root.TryGetProperty("players", out var players))
            {
                if (players.TryGetProperty("online", out var on)) result.Online = on.GetInt32();
                if (players.TryGetProperty("max", out var mx)) result.Max = mx.GetInt32();
            }
            if (root.TryGetProperty("favicon", out var fav) && fav.ValueKind == JsonValueKind.String)
            {
                var icon = fav.GetString() ?? "";
                var idx = icon.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
                var data = idx >= 0 ? icon[(idx + 7)..] : icon;
                try { result.IconBytes = Convert.FromBase64String(data); }
                catch { }
            }
            return result;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to parse server status JSON", ex);
            return null;
        }
    }

    /// <summary>Flattens any chat (string, object {text}, or array of texts) into plain §-stripped text.</summary>
    private static string FlattenChat(JsonElement element)
    {
        var sb = new StringBuilder();
        AppendChat(sb, element);
        return ServerText.StripColorCodes(sb.ToString()).Trim();
    }

    private static void AppendChat(StringBuilder sb, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                sb.Append(element.GetString());
                break;
            case JsonValueKind.Object:
                if (element.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    sb.Append(t.GetString());
                if (element.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
                    foreach (var child in extra.EnumerateArray())
                        AppendChat(sb, child);
                if (element.TryGetProperty("translate", out var tr) && tr.ValueKind == JsonValueKind.String
                    && element.TryGetProperty("with", out var with) && with.ValueKind == JsonValueKind.Array)
                    foreach (var child in with.EnumerateArray())
                        AppendChat(sb, child);
                break;
            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                    AppendChat(sb, child);
                break;
        }
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        var v = (uint)value;
        do
        {
            var b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0) b |= 0x80;
            stream.WriteByte(b);
        } while (v != 0);
    }

    private static int ReadVarInt(Stream stream)
    {
        var value = 0;
        var shift = 0;
        for (var i = 0; i < 5; i++)
        {
            var b = stream.ReadByte();
            if (b < 0) throw new EndOfStreamException();
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
        throw new IOException("VarInt too long");
    }
}