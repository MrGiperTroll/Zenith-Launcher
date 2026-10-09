using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace CustomMcLauncher.Services;

/// <summary>
/// Extracts the stable mod/plugin identifier from a jar, used for duplicate
/// detection. Fabric/Quilt expose "id", Forge/NeoForge use the first
/// [[mods]] modId, legacy mods use mcmod.info modid, Bukkit plugins use name.
/// </summary>
public static class ModMetaReader
{
    public static bool TryGetModId(string jarPath, out string modId, out string? version)
    {
        modId = "";
        version = null;
        try
        {
            if (!File.Exists(jarPath)) return false;
            using var zip = ZipFile.OpenRead(jarPath);

            var fabric = FindEntry(zip, "fabric.mod.json");
            if (fabric != null)
            {
                using var s = fabric.Open();
                using var doc = JsonDocument.Parse(s);
                var r = doc.RootElement;
                if (r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    modId = (id.GetString() ?? "").Trim().ToLowerInvariant();
                    if (r.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                        version = v.GetString();
                    return modId.Length > 0;
                }
            }

            var toml = FindEntry(zip, "META-INF/mods.toml");
            if (toml != null)
            {
                using var s = new StreamReader(toml.Open());
                if (TryParseFirstModId(s.ReadToEnd(), out var tid, out var tver))
                {
                    modId = tid;
                    version = tver;
                    return true;
                }
            }

            var mcmod = FindEntry(zip, "mcmod.info");
            if (mcmod != null)
            {
                using var s = mcmod.Open();
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        if (e.TryGetProperty("modid", out var id) && id.ValueKind == JsonValueKind.String)
                        {
                            modId = (id.GetString() ?? "").Trim().ToLowerInvariant();
                            if (e.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                                version = v.GetString();
                            if (modId.Length > 0) return true;
                        }
                    }
                }
            }

            var pluginYml = FindEntry(zip, "plugin.yml") ?? FindEntry(zip, "paper-plugin.yml");
            if (pluginYml != null)
            {
                using var s = new StreamReader(pluginYml.Open());
                string? line;
                while ((line = s.ReadLine()) != null)
                {
                    var t = line.Trim();
                    if (t.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                    {
                        modId = t[5..].Trim(' ', '"', '\'').ToLowerInvariant();
                        return modId.Length > 0;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    private static bool TryParseFirstModId(string text, out string modId, out string? version)
    {
        modId = "";
        version = null;
        try
        {
            var inMods = false;
            string? id = null, ver = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("[[mods]]", StringComparison.Ordinal)) { inMods = true; continue; }
                if (line.StartsWith("[", StringComparison.Ordinal)) { if (inMods) break; continue; }
                if (!inMods || line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim().Trim('"');
                var val = line[(eq + 1)..].Trim().Trim('"');
                if (key.Equals("modId", StringComparison.OrdinalIgnoreCase)) id = val;
                else if (key.Equals("version", StringComparison.OrdinalIgnoreCase)) ver = val;
                if (id != null && ver != null) break;
            }
            if (string.IsNullOrWhiteSpace(id)) return false;
            modId = id.Trim().ToLowerInvariant();
            version = ver;
            return true;
        }
        catch { return false; }
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string name)
    {
        return zip.Entries.FirstOrDefault(e =>
            e.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
