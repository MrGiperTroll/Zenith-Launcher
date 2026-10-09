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
    /// <summary>
    /// Raw NBT of tags the editor does not manage (future flags, game-written
    /// data...), carried through verbatim so saving never strips them.
    /// </summary>
    public List<byte[]> UnknownTags { get; } = new();
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
            var fileBytes = File.ReadAllBytes(path);
            var data = IsGzipMagic(fileBytes) ? DecompressAll(fileBytes) : fileBytes;
            return ParseServers(data);
        }
        catch { return new List<ServerEntry>(); }
    }

    public static void Write(string path, List<ServerEntry> servers)
    {
        try { if (File.Exists(path)) File.Copy(path, path + ".bak", true); } catch { }

        // Preserve the file's existing encoding: the modern game writes
        // servers.dat uncompressed, older ones gzipped. New files default to
        // uncompressed.
        bool useGzip = false;
        try
        {
            if (File.Exists(path))
            {
                var head = new byte[2];
                using (var fs = File.OpenRead(path))
                    if (fs.Read(head, 0, 2) == 2 && head[0] == 0x1F && head[1] == 0x8B)
                        useGzip = true;
            }
        }
        catch { }

        var out_ = new List<byte>();
        out_.Add(TagCompound);
        WriteRawString(out_, "");
        out_.Add(TagList);
        WriteRawString(out_, "servers");
        out_.Add(TagCompound);
        WriteRawInt32(out_, servers.Count);
        foreach (var s in servers)
        {
            WriteRawTagString(out_, "name", string.IsNullOrWhiteSpace(s.Name) ? "Server" : s.Name);
            WriteRawTagString(out_, "ip", s.Ip ?? "");
            WriteRawTagByte(out_, "acceptTextures", (byte)(s.AcceptTextures ? 1 : 0));
            WriteRawTagByte(out_, "hideAddress", (byte)(s.HideAddress ? 1 : 0));
            WriteRawTagByte(out_, "hidden", (byte)(s.HideAddress ? 1 : 0));
            if (s.IconBytes is { Length: > 0 })
                WriteRawTagString(out_, "icon", "data:image/png;base64," + Convert.ToBase64String(s.IconBytes));
            if (!string.IsNullOrWhiteSpace(s.Motd))
                WriteRawTagString(out_, "motd", s.Motd);
            foreach (var raw in s.UnknownTags)
                out_.AddRange(raw);
            out_.Add(TagEnd);
        }
        out_.Add(TagEnd);

        var bytes = out_.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
        using (var fs = File.Create(path))
        {
            if (useGzip)
            {
                using var gz = new GZipStream(fs, CompressionLevel.Optimal);
                gz.Write(bytes, 0, bytes.Length);
            }
            else
            {
                fs.Write(bytes, 0, bytes.Length);
            }
        }
    }

    private static bool IsGzipMagic(byte[] b) => b.Length >= 2 && b[0] == 0x1F && b[1] == 0x8B;

    private static byte[] DecompressAll(byte[] gzip)
    {
        using var ms = new MemoryStream(gzip);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        gz.CopyTo(outMs);
        return outMs.ToArray();
    }

    private static void WriteRawString(List<byte> o, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        o.Add((byte)(b.Length >> 8)); o.Add((byte)b.Length);
        o.AddRange(b);
    }

    private static void WriteRawInt32(List<byte> o, int v)
    {
        o.Add((byte)(v >> 24)); o.Add((byte)(v >> 16)); o.Add((byte)(v >> 8)); o.Add((byte)v);
    }

    private static void WriteRawTagString(List<byte> o, string name, string value)
    {
        o.Add(TagString); WriteRawString(o, name); WriteRawString(o, value);
    }

    private static void WriteRawTagByte(List<byte> o, string name, byte value)
    {
        o.Add(TagByte); WriteRawString(o, name); o.Add(value);
    }

    /// <summary>Tags managed by the editor; everything else round-trips verbatim.</summary>
    private static readonly HashSet<string> ManagedTags = new(StringComparer.Ordinal)
    {
        "name", "ip", "icon", "acceptTextures", "hidden", "hideAddress", "motd"
    };

    /// <summary>
    /// Index-based NBT parser over the (already decompressed) file bytes.
    /// Unknown tags are sliced out as raw bytes for verbatim passthrough.
    /// </summary>
    private static List<ServerEntry> ParseServers(byte[] data)
    {
        var servers = new List<ServerEntry>();
        int pos = 0;
        int U8() => pos < data.Length ? data[pos++] : throw new EndOfStreamException();
        int U16() { var a = U8(); var b = U8(); return (a << 8) | b; }
        int I32() { var a = U8(); var b = U8(); var c = U8(); var d = U8(); return (a << 24) | (b << 16) | (c << 8) | d; }
        string Str() { var n = U16(); if (pos + n > data.Length) throw new EndOfStreamException(); var s = Encoding.UTF8.GetString(data, pos, n); pos += n; return s; }
        void SkipPayload(int id)
        {
            switch (id)
            {
                case 1: U8(); break;
                case 2: U16(); break;
                case 3: case 5: I32(); break;
                case 4: case 6: for (var k = 0; k < 8; k++) U8(); break;
                case 7: { var n = Math.Max(0, I32()); for (var k = 0; k < n; k++) U8(); break; }
                case 8: { var n = U16(); for (var k = 0; k < n; k++) U8(); break; }
                case 9:
                {
                    var et = U8(); var cnt = Math.Max(0, I32());
                    for (var k = 0; k < cnt; k++) SkipPayload(et);
                    break;
                }
                case 10:
                    while (true) { var t = U8(); if (t == 0) break; Str(); SkipPayload(t); }
                    break;
                case 11: { var n = Math.Max(0, I32()); for (var k = 0; k < n; k++) I32(); break; }
                case 12: { var n = Math.Max(0, I32()); for (var k = 0; k < n * 8; k++) U8(); break; }
                default: throw new InvalidDataException($"Unknown NBT tag {id}");
            }
        }

        if (U8() != TagCompound) return servers;
        Str(); // root name
        while (true)
        {
            var tag = U8();
            if (tag == TagEnd) break;
            var name = Str();
            if (tag == TagList && name == "servers")
            {
                var elemType = U8();
                var count = Math.Max(0, Math.Min(I32(), 10000));
                if (elemType != TagCompound) return servers;
                for (var i = 0; i < count; i++)
                {
                    var entry = new ServerEntry();
                    while (true)
                    {
                        var start = pos;
                        var t = U8();
                        if (t == TagEnd) break;
                        var key = Str();
                        if (t == TagString)
                        {
                            var val = Str();
                            if (!ManagedTags.Contains(key))
                            {
                                entry.UnknownTags.Add(SubArray(data, start, pos));
                            }
                            else switch (key)
                            {
                                case "name": entry.Name = ServerText.StripColorCodes(val); break;
                                case "ip": entry.Ip = val; break;
                                case "motd": entry.Motd = ServerText.StripColorCodes(val).Trim(); break;
                                case "icon": entry.IconBytes = DecodeIcon(val); break;
                            }
                        }
                        else if (t == TagByte)
                        {
                            var val = (byte)U8();
                            if (!ManagedTags.Contains(key))
                            {
                                entry.UnknownTags.Add(SubArray(data, start, pos));
                            }
                            else if (key == "acceptTextures") entry.AcceptTextures = val != 0;
                            else if (key == "hidden" || key == "hideAddress") entry.HideAddress = entry.HideAddress || val != 0;
                        }
                        else
                        {
                            SkipPayload(t);
                            if (!ManagedTags.Contains(key))
                                entry.UnknownTags.Add(SubArray(data, start, pos));
                        }
                    }
                    servers.Add(entry);
                }
            }
            else
            {
                SkipPayload(tag);
            }
        }
        return servers;
    }

    private static byte[] SubArray(byte[] data, int start, int end)
    {
        var n = Math.Max(0, end - start);
        var out_ = new byte[n];
        Buffer.BlockCopy(data, start, out_, 0, n);
        return out_;
    }

    private static byte[] DecodeIcon(string val)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(val) || val.Length < 22) return Array.Empty<byte>();
            var idx = val.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            var data = idx >= 0 ? val[(idx + 7)..] : val;
            var decoded = Convert.FromBase64String(data);
            return decoded.Length > 0 ? decoded : Array.Empty<byte>();
        }
        catch { return Array.Empty<byte>(); }
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
}