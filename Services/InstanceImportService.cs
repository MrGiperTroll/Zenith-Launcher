using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Services;

public enum ImportSourceKind
{
    None,
    Prism,
    MultiMC,
    /// <summary>A single exported instance folder (instance.cfg + mmc-pack.json).</summary>
    Single,
}

public sealed record FoundInstance(
    string Name,
    string McVersion,
    string LoaderType,
    string LoaderBuild,
    int MaxMemMb,
    string JavaPath,
    string JvmArgs,
    string SourceDir);

public sealed record ImportResult(
    int Imported,
    int Skipped,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Imports ready-made instances from Prism Launcher / MultiMC in a couple of
/// clicks: versions, loaders, RAM and Java settings are read from
/// mmc-pack.json + instance.cfg, user content (mods, saves, configs,
/// resource/shader packs) is copied over. Game assets download on first
/// launch as usual.
/// </summary>
public static class InstanceImportService
{
    private static readonly string[] CopyDirs =
    {
        "mods", "coremods", "config", "saves", "resourcepacks", "shaderpacks",
        "scripts", "kubejs", "datapacks", "openloader"
    };

    private static readonly string[] CopyFiles =
    {
        "options.txt", "servers.dat", "servers.dat_old"
    };

    public static ImportSourceKind Detect(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return ImportSourceKind.None;
            if (File.Exists(Path.Combine(path, "prismlauncher.cfg")))
                return ImportSourceKind.Prism;
            if (File.Exists(Path.Combine(path, "multimc.cfg")))
                return ImportSourceKind.MultiMC;
            if (File.Exists(Path.Combine(path, "instance.cfg")) &&
                File.Exists(Path.Combine(path, "mmc-pack.json")))
                return ImportSourceKind.Single;
        }
        catch { }
        return ImportSourceKind.None;
    }

    public static IReadOnlyList<FoundInstance> Scan(string path)
    {
        var kind = Detect(path);
        var out_ = new List<FoundInstance>();
        try
        {
            if (kind == ImportSourceKind.Single)
            {
                var one = ReadInstanceDir(path, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                if (one != null) out_.Add(one);
                return out_;
            }
            if (kind is ImportSourceKind.Prism or ImportSourceKind.MultiMC)
            {
                var instancesDir = Path.Combine(path, "instances");
                if (!Directory.Exists(instancesDir)) return out_;
                foreach (var dir in Directory.GetDirectories(instancesDir).OrderBy(d => d))
                {
                    var found = ReadInstanceDir(dir, Path.GetFileName(dir));
                    if (found != null) out_.Add(found);
                }
            }
        }
        catch { }
        return out_;
    }

    public static async Task<ImportResult> ImportAsync(
        IInstanceService svc, string path, CancellationToken ct = default)
    {
        var found = Scan(path);
        var warnings = new List<string>();
        if (found.Count == 0)
            return new ImportResult(0, 0, warnings);

        int imported = 0, skipped = 0;
        foreach (var f in found)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (string.IsNullOrWhiteSpace(f.McVersion))
                {
                    warnings.Add($"{f.Name}: no game version found, skipped.");
                    skipped++;
                    continue;
                }
                var name = UniqueName(svc, f.Name);
                var inst = await svc.CreateInstanceAsync(name, f.McVersion, f.LoaderType, f.LoaderBuild ?? "");
                if (inst == null)
                {
                    warnings.Add($"{f.Name}: name is taken, skipped.");
                    skipped++;
                    continue;
                }
                if (f.MaxMemMb >= 512) inst.MaxMemoryMb = f.MaxMemMb;
                if (!string.IsNullOrWhiteSpace(f.JavaPath)) inst.CustomJavaPath = f.JavaPath;
                if (!string.IsNullOrWhiteSpace(f.JvmArgs)) inst.JvmArgs = f.JvmArgs;
                CopyUserContent(f.SourceDir, inst.Path);
                await svc.SaveInstanceAsync(inst);
                imported++;
            }
            catch (Exception ex)
            {
                warnings.Add($"{f.Name}: {ex.Message}");
                skipped++;
            }
        }
        return new ImportResult(imported, skipped, warnings);
    }

    private static string UniqueName(IInstanceService svc, string baseName)
    {
        var existing = svc.GetInstances().Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(baseName)) return baseName;
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    private static FoundInstance? ReadInstanceDir(string dir, string fallbackName)
    {
        try
        {
            var cfg = ReadIni(Path.Combine(dir, "instance.cfg"));
            var name = fallbackName;
            var mcVersion = cfg.GetValueOrDefault("IntendedVersion") ?? cfg.GetValueOrDefault("MCVersion") ?? "";
            var loaderType = "Vanilla";
            var loaderBuild = "";
            var packPath = Path.Combine(dir, "mmc-pack.json");
            if (File.Exists(packPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(packPath));
                    if (doc.RootElement.TryGetProperty("components", out var comps) &&
                        comps.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in comps.EnumerateArray())
                        {
                            if (!c.TryGetProperty("uid", out var uidEl)) continue;
                            var uid = uidEl.GetString() ?? "";
                            var ver = c.TryGetProperty("version", out var vEl) ? vEl.GetString() ?? "" : "";
                            if (uid == "net.minecraft" && !string.IsNullOrWhiteSpace(ver))
                                mcVersion = ver;
                            else if (uid == "net.fabricmc.fabric-loader")
                            { loaderType = "Fabric"; loaderBuild = ver; }
                            else if (uid == "net.minecraftforge")
                            { loaderType = "Forge"; loaderBuild = StripMcPrefix(mcVersion, ver); }
                            else if (uid == "net.neoforged")
                            { loaderType = "NeoForge"; loaderBuild = StripMcPrefix(mcVersion, ver); }
                            else if (uid == "org.quiltmc.quilt-loader")
                            { loaderType = "Quilt"; loaderBuild = ver; }
                        }
                    }
                }
                catch { }
            }

            var gameDir = Path.Combine(dir, ".minecraft");
            if (!Directory.Exists(gameDir)) return null; // not a runnable instance

            int maxMem = 0;
            if (cfg.TryGetValue("MaxMemAlloc", out var mm) && int.TryParse(mm, out var parsed) && parsed >= 512)
                maxMem = parsed;
            cfg.TryGetValue("JavaPath", out var javaPath);
            cfg.TryGetValue("JvmArgs", out var jvmArgs);
            return new FoundInstance(name, mcVersion.Trim(), loaderType, (loaderBuild ?? "").Trim(),
                maxMem, (javaPath ?? "").Trim(), (jvmArgs ?? "").Trim(), gameDir);
        }
        catch { return null; }
    }

    private static string StripMcPrefix(string mcVersion, string loaderVersion)
    {
        // Forge builds look like "1.20.1-47.2.0"; keep the part after the dash.
        if (!string.IsNullOrWhiteSpace(mcVersion) &&
            loaderVersion.StartsWith(mcVersion + "-", StringComparison.OrdinalIgnoreCase))
            return loaderVersion[(mcVersion.Length + 1)..];
        return loaderVersion;
    }

    private static Dictionary<string, string> ReadIni(string path)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path)) return d;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                d[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
        }
        catch { }
        return d;
    }

    private static void CopyUserContent(string gameDir, string destRoot)
    {
        foreach (var sub in CopyDirs)
        {
            var src = Path.Combine(gameDir, sub);
            if (!Directory.Exists(src)) continue;
            CopyDirectory(src, Path.Combine(destRoot, sub));
        }
        foreach (var file in CopyFiles)
        {
            var src = Path.Combine(gameDir, file);
            if (!File.Exists(src)) continue;
            try
            {
                Directory.CreateDirectory(destRoot);
                File.Copy(src, Path.Combine(destRoot, file), true);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Instance import: failed to copy {file}: {ex.Message}");
            }
        }
    }

    private static void CopyDirectory(string src, string dest)
    {
        try
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.GetFiles(src))
            {
                try { File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true); }
                catch (Exception ex) { LauncherLog.Warn($"Instance import: failed to copy file: {ex.Message}"); }
            }
            foreach (var d in Directory.GetDirectories(src))
                CopyDirectory(d, Path.Combine(dest, Path.GetFileName(d)));
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Instance import: failed to copy folder {src}: {ex.Message}");
        }
    }
}
