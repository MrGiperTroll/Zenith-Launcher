using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CustomMcLauncher.Services;

public class ServerEntry
{
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public byte[] IconBytes { get; set; } = Array.Empty<byte>();
    public bool AcceptTextures { get; set; }
    public bool HideAddress { get; set; }
    public string Motd { get; set; } = "";
}

public static class ServerText
{
    /// <summary>Strips Minecraft § color/format codes (e.g. "§aHello" → "Hello").</summary>
    public static string StripColorCodes(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '§' && i + 1 < s.Length) { i++; continue; }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}

public static class ServersDatService
{
    private const byte TagEnd = 0;
    private const byte TagByte = 1;
    private const byte TagShort = 2;
    private const byte TagInt = 3;
    private const byte TagLong = 4;
    private const byte TagFloat = 5;
    private const byte TagDouble = 6;
    private const byte TagByteArray = 7;
    private const byte TagString = 8;
    private const byte TagList = 9;
    private const byte TagCompound = 10;
    private const byte TagIntArray = 11;
    private const byte TagLongArray = 12;

    public static bool FileExists(string serversDatPath) => File.Exists(serversDatPath);

    /// <summary>Decompress stream if the file starts with a gzip magic header (0x1F 0x8B), else return as-is.
    /// Modern Minecraft (1.20+) writes servers.dat uncompressed, so assuming gzip and catching later fails silently.</summary>
    private static Stream OpenNbtStream(Stream fs)
    {
        if (fs.Length >= 2)
        {
            var b0 = fs.ReadByte();
            var b1 = fs.ReadByte();
            fs.Position = 0;
            if (b0 == 0x1F && b1 == 0x8B)
                return new GZipStream(fs, CompressionMode.Decompress);
        }
        return fs;
    }

    /// <summary>Reads a (optionally gzipped) root NBT compound, e.g. level.dat or servers.dat.</summary>
    public static Dictionary<string, object?>? ReadNbtFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            return new NbtReader(OpenNbtStream(fs)).ReadCompound();
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to read NBT file '{path}'", ex);
            return null;
        }
    }

    public static List<ServerEntry> Read(string path)
    {
        if (!File.Exists(path)) return new List<ServerEntry>();
        try
        {
            using var fs = File.OpenRead(path);
            using var stream = OpenNbtStream(fs);

            var root = new NbtReader(stream).ReadCompound();
            if (root == null) return new List<ServerEntry>();

            var servers = new List<ServerEntry>();
            if (root.TryGetValue("servers", out var list) && list is List<(string Name, object? Payload)> entries)
            {
                foreach (var (_, payload) in entries)
                {
                    if (payload is not Dictionary<string, object?> comp) continue;
                    var entry = new ServerEntry
                    {
                        Name = ServerText.StripColorCodes(GetString(comp, "name")),
                        Ip = GetString(comp, "ip"),
                        IconBytes = GetByteArray(comp, "icon"),
                        AcceptTextures = GetByte(comp, "acceptTextures") != 0,
                        HideAddress = GetByte(comp, "hidden") != 0 || GetByte(comp, "hideAddress") != 0,
                        Motd = ServerText.StripColorCodes(GetString(comp, "motd")).Trim()
                    };
                    servers.Add(entry);
                }
            }
            return servers;
        }
        catch { return new List<ServerEntry>(); }
    }

    public static void Write(string path, List<ServerEntry> servers)
    {
        try { if (File.Exists(path)) File.Copy(path, path + ".bak", true); } catch { }

        var list = new List<object?>();
        foreach (var s in servers)
        {
            var d = new Dictionary<string, object?>
            {
                ["name"] = s.Name ?? "Server",
                ["ip"] = s.Ip ?? "",
                ["acceptTextures"] = (byte)(s.AcceptTextures ? 1 : 0),
                ["hideAddress"] = (byte)(s.HideAddress ? 1 : 0),
                ["hidden"] = (byte)(s.HideAddress ? 1 : 0)
            };
            if (s.IconBytes is { Length: > 0 }) d["icon"] = s.IconBytes;
            if (!string.IsNullOrWhiteSpace(s.Motd)) d["motd"] = s.Motd;
            list.Add(d);
        }

        var payload = new Dictionary<string, object?> { ["servers"] = list };

        var builder = new NbtWriter();
        builder.WriteCompound(payload);
        var bytes = builder.ToBytes();

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
        using (var fs = File.Create(path))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        {
            gz.Write(bytes, 0, bytes.Length);
        }
    }

    private static string GetString(Dictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v is string s ? s : "";
    private static byte GetByte(Dictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v is byte b ? b
           : (byte)(d.TryGetValue(key, out var vi) && vi is int i ? i : 0);
    private static byte[] GetByteArray(Dictionary<string, object?> d, string key)
    {
        if (d.TryGetValue(key, out var v))
        {
            if (v is byte[] b) return b;
            if (v is string s && s.Length > 22)
            {
                try
                {
                    var idx = s.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
                    var data = idx >= 0 ? s[(idx + 7)..] : s;
                    var decoded = Convert.FromBase64String(data);
                    if (decoded.Length > 0) return decoded;
                }
                catch { }
            }
        }
        return Array.Empty<byte>();
    }

    private sealed class NbtReader
    {
        private readonly Stream _s;
        private readonly byte[] _buf = new byte[8];

        public NbtReader(Stream s) => _s = s;

        public Dictionary<string, object?>? ReadCompound()
        {
            var type = _s.ReadByte();
            if (type == TagEnd) return null;
            _ = ReadString();
            if (type != TagCompound) return null;
            return (Dictionary<string, object?>)ReadPayload(TagCompound)!;
        }

        private object? ReadPayload(int type)
        {
            switch (type)
            {
                case TagByte: return (byte)_s.ReadByte();
                case TagShort: { var v = (short)ReadUInt16(); return (short)v; }
                case TagInt: return ReadInt32();
                case TagLong: return ReadInt64();
                case TagFloat: return ReadSingle();
                case TagDouble: return ReadDouble();
                case TagByteArray:
                {
                    var len = ReadInt32();
                    if (len < 0 || len > 100_000_000) return Array.Empty<byte>();
                    var arr = new byte[len];
                    ReadN(arr, len);
                    return arr;
                }
                case TagString: return ReadString();
                case TagList:
                {
                    var elemType = _s.ReadByte();
                    var count = ReadInt32();
                    if (count < 0 || count > 1_000_000) return new List<(string, object?)>();
                    var list = new List<(string, object?)>();
                    for (var i = 0; i < count; i++)
                        list.Add(("", ReadPayload(elemType)));
                    return list;
                }
                case TagCompound:
                {
                    var d = new Dictionary<string, object?>();
                    while (true)
                    {
                        var t = _s.ReadByte();
                        if (t == TagEnd) break;
                        var name = ReadString();
                        d[name] = ReadPayload(t);
                    }
                    return d;
                }
                case TagIntArray:
                {
                    var len = ReadInt32();
                    if (len < 0 || len > 10_000_000) return Array.Empty<int>();
                    var arr = new int[len];
                    for (var i = 0; i < len; i++) arr[i] = ReadInt32();
                    return arr;
                }
                case TagLongArray:
                {
                    var len = ReadInt32();
                    if (len < 0 || len > 10_000_000) return Array.Empty<long>();
                    var arr = new long[len];
                    for (var i = 0; i < len; i++) arr[i] = ReadInt64();
                    return arr;
                }
                default: return null;
            }
        }

        private string ReadString()
        {
            var len = ReadUInt16();
            var bytes = new byte[len];
            ReadN(bytes, len);
            return Encoding.UTF8.GetString(bytes);
        }

        private void ReadN(byte[] buffer, int len)
        {
            var read = 0;
            while (read < len)
            {
                var r = _s.Read(buffer, read, len - read);
                if (r <= 0) throw new EndOfStreamException();
                read += r;
            }
        }

        private int ReadUInt16() { ReadN(_buf, 2); return (_buf[0] << 8) | _buf[1]; }
        private int ReadInt32() { ReadN(_buf, 4); return (_buf[0] << 24) | (_buf[1] << 16) | (_buf[2] << 8) | _buf[3]; }
        private long ReadInt64() { ReadN(_buf, 8); long v = 0; for (var i = 0; i < 8; i++) v = (v << 8) | _buf[i]; return v; }
        private float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());
        private double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());
    }

    private sealed class NbtWriter
    {
        private readonly List<byte> _bytes = new();
        private readonly byte[] _tmp = new byte[8];

        public byte[] ToBytes() => _bytes.ToArray();

        public void WriteCompound(Dictionary<string, object?> root)
        {
            _bytes.Add(TagCompound);
            WriteString("");
            WriteCompoundPayload(root);
            _bytes.Add(TagEnd);
        }

        private void WriteCompoundPayload(Dictionary<string, object?> dict)
        {
            foreach (var (name, value) in dict)
            {
                WriteTag(name, value);
            }
            _bytes.Add(TagEnd);
        }

        private void WriteTag(string name, object? value)
        {
            switch (value)
            {
                case byte b: _bytes.Add(TagByte); WriteString(name); _bytes.Add(b); break;
                case short s: _bytes.Add(TagShort); WriteString(name); WriteInt16(s); break;
                case int i: _bytes.Add(TagInt); WriteString(name); WriteInt32(i); break;
                case long l: _bytes.Add(TagLong); WriteString(name); WriteInt64(l); break;
                case float f: _bytes.Add(TagFloat); WriteString(name); WriteInt32(BitConverter.SingleToInt32Bits(f)); break;
                case double d: _bytes.Add(TagDouble); WriteString(name); WriteInt64(BitConverter.DoubleToInt64Bits(d)); break;
                case byte[] ba:
                    if (ba.Length == 0) break;
                    _bytes.Add(TagByteArray); WriteString(name); WriteInt32(ba.Length); _bytes.AddRange(ba);
                    break;
                case string str:
                    _bytes.Add(TagString); WriteString(name); WriteString(str);
                    break;
                case List<object?> list:
                    _bytes.Add(TagList); WriteString(name);
                    _bytes.Add(list.Count > 0 ? InferListType(list[0]) : TagEnd);
                    WriteInt32(list.Count);
                    foreach (var item in list) WritePayload(item);
                    break;
                case Dictionary<string, object?> sub:
                    _bytes.Add(TagCompound); WriteString(name); WriteCompoundPayload(sub);
                    break;
            }
        }

        private byte InferListType(object? value) => value switch
        {
            byte => TagByte,
            short => TagShort,
            int => TagInt,
            long => TagLong,
            float => TagFloat,
            double => TagDouble,
            byte[] => TagByteArray,
            string => TagString,
            List<object?> => TagList,
            Dictionary<string, object?> => TagCompound,
            _ => TagEnd
        };

        private void WritePayload(object? value)
        {
            switch (value)
            {
                case byte b: _bytes.Add(b); break;
                case short s: WriteInt16(s); break;
                case int i: WriteInt32(i); break;
                case long l: WriteInt64(l); break;
                case float f: WriteInt32(BitConverter.SingleToInt32Bits(f)); break;
                case double d: WriteInt64(BitConverter.DoubleToInt64Bits(d)); break;
                case byte[] ba: WriteInt32(ba.Length); _bytes.AddRange(ba); break;
                case string str: WriteString(str); break;
                case List<object?> list:
                    _bytes.Add(list.Count > 0 ? InferListType(list[0]) : TagEnd);
                    WriteInt32(list.Count);
                    foreach (var item in list) WritePayload(item);
                    break;
                case Dictionary<string, object?> sub: WriteCompoundPayload(sub); break;
            }
        }

        private void WriteString(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            WriteInt16((short)bytes.Length);
            _bytes.AddRange(bytes);
        }

        private void WriteInt16(short v) { _bytes.Add((byte)(v >> 8)); _bytes.Add((byte)v); }
        private void WriteInt32(int v) { _bytes.Add((byte)(v >> 24)); _bytes.Add((byte)(v >> 16)); _bytes.Add((byte)(v >> 8)); _bytes.Add((byte)v); }
        private void WriteInt64(long v) { for (var i = 7; i >= 0; i--) _bytes.Add((byte)(v >> (i * 8))); }
    }
}