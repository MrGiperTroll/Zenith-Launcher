using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Services;

/// <summary>
/// Reads rich metadata + preview images for instance content so the file manager can show
/// real cards instead of raw filenames:
///  • Mods:           fabric.mod.json / META-INF/mods.toml / mcmod.info (+ icon.png)
///  • ResourcePacks / ShaderPacks: pack.png + pack.mcmeta (folders or archives)
///  • Worlds:         level.dat (name, gamemode, version, last played) + icon.png
///  • Screenshots:    just the image itself (decoded as a thumbnail)
/// </summary>
public static class ContentMetadataReader
{
    public static void Enrich(InstanceFileEntry entry)
    {
        if (entry.IsDirectory && entry.Kind is InstanceFileKind.Mod or InstanceFileKind.Screenshot or InstanceFileKind.Server) return;

        try
        {
            switch (entry.Kind)
            {
                case InstanceFileKind.Mod: EnrichMod(entry); break;
                case InstanceFileKind.ResourcePack:
                case InstanceFileKind.ShaderPack:
                case InstanceFileKind.DataPack: EnrichPack(entry); break;
                case InstanceFileKind.World: EnrichWorld(entry); break;
                case InstanceFileKind.Screenshot: EnrichScreenshot(entry); break;
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to read metadata for '{entry.FullPath}'", ex);
        }
    }

    // ------------------------------------------------------------------ mods

    private static string? ReadEntryText(ZipArchiveEntry? entry)
    {
        if (entry == null) return null;
        try
        {
            using var s = entry.Open();
            using var reader = new StreamReader(s);
            return reader.ReadToEnd();
        }
        catch { return null; }
    }

    private static void EnrichMod(InstanceFileEntry entry)
    {
        var path = ResolveDisabled(entry.FullPath);
        if (path == null || !File.Exists(path)) { FallbackTitle(entry); return; }
        string? name = null, version = null, description = null, iconPath = null;

        try
        {
            using var zip = ZipFile.OpenRead(path);
            var fabric = FindEntry(zip, "fabric.mod.json");
            if (fabric != null)
            {
                var (n, v, d, icon) = ParseFabricModJson(ReadEntryText(fabric) ?? "");
                name = n; version = v; description = d; iconPath = icon;
            }
            else
            {
                var toml = FindEntry(zip, "META-INF/mods.toml");
                if (toml != null)
                {
                    var (n, v, d, icon) = ParseNeoForgeToml(ReadEntryText(toml) ?? "");
                    name = n; version = v; description = d; iconPath = icon;
                }
                else
                {
                    var mcmod = FindEntry(zip, "mcmod.info");
                    if (mcmod != null)
                    {
                        var (n, v, d, icon) = ParseMcmodInfo(ReadEntryText(mcmod) ?? "");
                        name = n; version = v; description = d; iconPath = icon;
                    }
                }
            }

            Apply(entry, path, name, version, description);

            var iconBytes = iconPath != null ? ExtractEntry(zip, iconPath) : ExtractEntry(zip, "icon.png");
            if (iconBytes == null && iconPath != null)
            {
                var f = FindEntry(zip, Path.GetFileName(iconPath));
                if (f != null) iconBytes = ExtractEntry(zip, f.FullName);
            }
            iconBytes ??= ExtractFirstNamedImage(zip, "icon.png");
            if (iconBytes != null) entry.SetPreview(iconBytes, 64);
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to read mod jar '{path}'", ex);
            FallbackTitle(entry);
        }
    }

    private static (string? name, string? version, string? description, string? icon) ParseFabricModJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return (Str(r, "name"), Str(r, "version"), Str(r, "description"), Str(r, "icon"));
    }

    private static (string? name, string? version, string? description, string? icon) ParseMcmodInfo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return (null, null, null, null);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var name = Str(e, "name") ?? Str(e, "modid");
            if (name == null) continue;
            return (name, Str(e, "version"), Str(e, "description"), Str(e, "logoFile"));
        }
        return (null, null, null, null);
    }

    private static (string? name, string? version, string? description, string? icon) ParseNeoForgeToml(string text)
    {
        var first = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inMods = false;
        var triple = false;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (triple)
            {
                if (line.Contains("\"\"\"")) { triple = false; }
                else first["description"] = first.GetValueOrDefault("description") + line + " ";
                continue;
            }
            if (line.StartsWith("[[mods]]", StringComparison.Ordinal)) { inMods = true; continue; }
            if (line.StartsWith("[", StringComparison.Ordinal)) { inMods = false; continue; }
            if (!inMods || string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim().Trim('"');
            var value = line[(eq + 1)..].Trim();
            if (value.StartsWith("\"\"\"", StringComparison.Ordinal))
            {
                var rest = value[3..];
                if (rest.Contains("\"\"\"")) value = rest[..rest.IndexOf("\"\"\"", StringComparison.Ordinal)].Trim();
                else { triple = true; value = rest.Trim(); }
            }
            first[key] = Unquote(value);
            if (first.Count > 0 && key.Equals("displayName", StringComparison.OrdinalIgnoreCase)) break;
        }

        var name = Pick(first, "displayName", "name", "modId", "modid") ?? "";
        var version = Pick(first, "version");
        var description = first.GetValueOrDefault("description")?.Trim();
        var icon = Pick(first, "logoFile", "logo");
        return (name.Length > 0 ? name : null, version, description, icon);
    }

    private static void FallbackTitle(InstanceFileEntry entry)
    {
        entry.Title = Path.GetFileNameWithoutExtension(ResolveDisabled(entry.FullPath) ?? entry.FullPath);
        entry.Subtitle = "";
        entry.Description = "";
    }

    private static void Apply(InstanceFileEntry entry, string path, string? name, string? version, string? description)
    {
        entry.Title = !string.IsNullOrWhiteSpace(name) ? name : Path.GetFileNameWithoutExtension(path);
        entry.Subtitle = string.IsNullOrWhiteSpace(version) ? "" : $"v{version}";
        entry.Description = CleanDescription(description) ?? "";
    }

    // --------------------------------------------------------------- packs

    private static void EnrichPack(InstanceFileEntry entry)
    {
        var path = ResolveDisabled(entry.FullPath);
        if (string.IsNullOrEmpty(path)) return;
        if (entry.IsDirectory && !Directory.Exists(path)) { FallbackTitle(entry); return; }
        if (!entry.IsDirectory && !File.Exists(path)) { FallbackTitle(entry); return; }

        byte[]? packPng = null;
        string? description = null;
        int? packFormat = null;

        if (!entry.IsDirectory)
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                packPng = ExtractEntry(zip, "pack.png") ?? ExtractEntry(zip, "icon.png") ?? ExtractEntry(zip, "preview.png") ?? ExtractEntry(zip, "thumb.png");
                if (packPng == null)
                    packPng = ExtractFirstNamedImage(zip, "pack.png") ?? ExtractFirstNamedImage(zip, "icon.png");
                if (packPng == null)
                    packPng = ExtractFirstRootPng(zip);
                if (packPng == null)
                {
                    // Strictly cache-based preview in %APPDATA%\.zenith\cache\icons\ by project ID
                    var sidecars = new[]
                    {
                        Path.Combine(ZenithPaths.AppDataDir, "cache", "icons", Path.GetFileNameWithoutExtension(path) + ".png"),
                        Path.Combine(ZenithPaths.AppDataDir, "cache", "icons", Path.GetFileName(path) + ".png"),
                        Path.Combine(ZenithPaths.AppDataDir, "cache", "previews", Path.GetFileNameWithoutExtension(path) + ".png"),
                        Path.Combine(ZenithPaths.AppDataDir, "cache", "previews", Path.GetFileName(path) + ".png")
                    };
                    foreach (var sc in sidecars)
                    {
                        if (File.Exists(sc))
                        {
                            try { packPng = File.ReadAllBytes(sc); if (packPng != null) break; } catch { }
                        }
                    }
                }
                var mcmeta = ExtractEntry(zip, "pack.mcmeta");
                if (mcmeta != null) ParsePackMcmeta(mcmeta, out description, out packFormat);
            }
            catch { }
        }
        else
        {
            foreach (var iconName in new[] { "pack.png", "icon.png", "preview.png", "thumb.png" })
            {
                var png = Path.Combine(path, iconName);
                if (File.Exists(png)) { try { packPng = File.ReadAllBytes(png); break; } catch { } }
                // case-insensitive fallback
                try
                {
                    var files = Directory.GetFiles(path, iconName, SearchOption.TopDirectoryOnly);
                    if (files.Length > 0) { packPng = File.ReadAllBytes(files[0]); break; }
                }
                catch { }
            }
            if (packPng == null)
            {
                // case-insensitive search for any pack.png variant
                try
                {
                    foreach (var f in Directory.GetFiles(path))
                    {
                        if (Path.GetFileName(f).Equals("pack.png", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(f).Equals("icon.png", StringComparison.OrdinalIgnoreCase))
                        {
                            packPng = File.ReadAllBytes(f);
                            break;
                        }
                    }
                }
                catch { }
            }
            if (packPng == null)
            {
                // fallback: first png in root (e.g., any preview image)
                try
                {
                    foreach (var f in Directory.GetFiles(path, "*.png"))
                    {
                        var info = new FileInfo(f);
                        if (info.Length > 5 * 1024 * 1024) continue;
                        packPng = File.ReadAllBytes(f);
                        break;
                    }
                }
                catch { }
            }
            if (packPng == null)
            {
                var sidecars = new[]
                {
                    Path.Combine(ZenithPaths.AppDataDir, "cache", "icons", Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) + ".png"),
                    Path.Combine(ZenithPaths.AppDataDir, "cache", "previews", Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) + ".png"),
                    Path.Combine(Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar))!, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) + ".png")
                };
                foreach (var sc in sidecars)
                {
                    if (File.Exists(sc))
                    {
                        try { packPng = File.ReadAllBytes(sc); if (packPng != null) break; } catch { }
                    }
                }
            }
            var mcmeta = Path.Combine(path, "pack.mcmeta");
            if (File.Exists(mcmeta)) { try { ParsePackMcmeta(File.ReadAllBytes(mcmeta), out description, out packFormat); } catch { } }
            else
            {
                // case-insensitive mcmeta
                try
                {
                    foreach (var f in Directory.GetFiles(path))
                        if (Path.GetFileName(f).Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase))
                        { ParsePackMcmeta(File.ReadAllBytes(f), out description, out packFormat); break; }
                }
                catch { }
            }
        }

        var title = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(title)) title = Path.GetFileName(path);
        entry.Title = title;
        entry.Subtitle = entry.Kind switch
        {
            InstanceFileKind.ResourcePack => "Resource Pack",
            InstanceFileKind.ShaderPack => "Shader Pack",
            InstanceFileKind.DataPack => "Data Pack",
            _ => ""
        };
        entry.Meta = packFormat.HasValue ? $"pack_format {packFormat.Value}" : "";
        entry.Description = CleanDescription(description) ?? "";
        if (packPng != null) entry.SetPreview(packPng, 64, allowDirectory: true);
    }

    private static void ParsePackMcmeta(byte[] bytes, out string? description, out int? packFormat)
    {
        description = null;
        packFormat = null;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.TryGetProperty("pack", out var pack))
            {
                if (Str(pack, "description") is { } d) description = d;
                else if (pack.TryGetProperty("description", out var comp) && comp.ValueKind == JsonValueKind.Object
                         && comp.TryGetProperty("text", out var txt))
                    description = txt.ToString();
                if (pack.TryGetProperty("pack_format", out var pf) && pf.ValueKind == JsonValueKind.Number)
                    packFormat = pf.GetInt32();
            }
        }
        catch { }
    }

    // ---------------------------------------------------------------- worlds

    private static void EnrichWorld(InstanceFileEntry entry)
    {
        var path = ResolveDisabled(entry.FullPath);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) { FallbackTitle(entry); return; }

        var levelDat = Path.Combine(path, "level.dat");
        if (!File.Exists(levelDat)) { FallbackTitle(entry); return; }

        var data = ServersDatService.ReadNbtFile(levelDat);
        var level = data;
        if (level != null && level.TryGetValue("Data", out var inner) && inner is Dictionary<string, object?> innerDict)
            level = innerDict;

        entry.Title = GetStr(level, "LevelName") ?? Path.GetFileName(path);

        var gametype = GetInt(level, "GameType");
        entry.Meta = gametype switch
        {
            1 => "Creative",
            2 => "Adventure",
            3 => "Spectator",
            _ => "Survival"
        };

        var lastPlayed = GetLong(level, "LastPlayed");
        if (lastPlayed is > 0)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(lastPlayed.Value).LocalDateTime;
            entry.Subtitle = dt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        if (level != null && level.TryGetValue("Version", out var ver) && ver is Dictionary<string, object?> verDict)
            entry.Subtitle = string.IsNullOrEmpty(entry.Subtitle) ? "" : entry.Subtitle;

        var versionName = GetStringOf(level, "Version", "Name");
        if (versionName != null)
            entry.Description = string.IsNullOrEmpty(entry.Description) ? $"Minecraft {versionName}" : entry.Description;

        var iconPath = Path.Combine(path, "icon.png");
        if (File.Exists(iconPath))
        {
            try { entry.SetPreview(File.ReadAllBytes(iconPath), 64, allowDirectory: true); } catch { }
        }
    }

    // ----------------------------------------------------------- screenshots

    private static void EnrichScreenshot(InstanceFileEntry entry)
    {
        var path = ResolveDisabled(entry.FullPath);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            entry.SetPreview(File.ReadAllBytes(path), 400);
            var size = new FileInfo(path).Length;
            entry.Meta = size > 1024 * 1024 ? $"{size / 1024d / 1024d:F1} MB" : $"{Math.Max(1, size / 1024)} KB";
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to load screenshot '{path}'", ex);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static string? ResolveDisabled(string path)
    {
        if (path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            return path[..^9];
        return path;
    }

    private static string? CleanDescription(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = StripColorCodes(s).Trim();
        if (t.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(t);
                foreach (var key in new[] { "text", "translate" })
                    if (doc.RootElement.TryGetProperty(key, out var v))
                        return v.ValueKind == JsonValueKind.String ? StripColorCodes(v.GetString() ?? "").Trim() : null;
            }
            catch { }
        }
        return t;
    }

    /// <summary>Removes Minecraft section-sign color/formatting codes (§a, §l, …).</summary>
    private static string StripColorCodes(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return System.Text.RegularExpressions.Regex.Replace(s, @"\u00A7[0-9a-fk-orA-FK-OR]", "");
    }

    private static string? Str(JsonElement el, string name)
    {
        return el.ValueKind == JsonValueKind.Object
               && el.TryGetProperty(name, out var v)
               && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];
        value = value.Replace("\\\"", "\"");
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            value = value[1..^1];
        return value;
    }

    private static string? Pick(Dictionary<string, string> d, params string[] keys)
    {
        foreach (var k in keys)
            if (d.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
        return null;
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string target)
    {
        var norm = Normalize(target);
        foreach (var e in zip.Entries)
        {
            if (Normalize(e.FullName).Equals(norm, StringComparison.OrdinalIgnoreCase)) return e;
        }
        // nested copy (e.g. bundled sub-jar metadata) — accept a path suffix
        foreach (var e in zip.Entries)
        {
            var n = Normalize(e.FullName);
            if (n.EndsWith("/" + norm, StringComparison.OrdinalIgnoreCase) && n.Count(c => c == '/') <= 2) return e;
        }
        return null;
    }

    private static string Normalize(string s) => s.Replace('\\', '/').TrimStart('/');

    private static byte[]? ExtractEntry(ZipArchive zip, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var entry = FindEntry(zip, path);
        if (entry == null) return null;
        try
        {
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    private static byte[]? ExtractFirstNamedImage(ZipArchive zip, string imageName)
    {
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith(imageName, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var s = e.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    if (ms.Length > 1024 * 1024) continue;
                    var arr = ms.ToArray();
                    return arr;
                }
                catch { continue; }
            }
        }
        return null;
    }

    private static byte[]? ExtractFirstRootPng(ZipArchive zip)
    {
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && e.Length is > 100 and < 5 * 1024 * 1024)
            {
                var norm = Normalize(e.FullName);
                if (norm.Count(c => c == '/') <= 1)
                {
                    try
                    {
                        using var s = e.Open();
                        using var ms = new MemoryStream();
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                    catch { continue; }
                }
            }
        }
        return null;
    }

    private static string? GetStr(Dictionary<string, object?>? d, string key)
        => d != null && d.TryGetValue(key, out var v) && v is string s ? s : null;

    private static int? GetInt(Dictionary<string, object?>? d, string key)
    {
        if (d == null || !d.TryGetValue(key, out var v)) return null;
        return v switch
        {
            int i => i,
            byte b => b,
            short s => s,
            long l => (int)l,
            _ => null
        };
    }

    private static long? GetLong(Dictionary<string, object?>? d, string key)
    {
        if (d == null || !d.TryGetValue(key, out var v)) return null;
        return v switch
        {
            long l => l,
            int i => i,
            _ => null
        };
    }

    private static string? GetStringOf(Dictionary<string, object?>? d, string compoundKey, string key)
    {
        if (d != null && d.TryGetValue(compoundKey, out var c) && c is Dictionary<string, object?> cd)
            return GetStr(cd, key);
        return null;
    }
}