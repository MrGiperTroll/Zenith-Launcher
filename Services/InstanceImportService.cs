using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Services;

public enum ImportSourceKind
{
    None,
    Prism,
    MultiMC,
    CurseForge,
    Modrinth,
    Vanilla,
    Archive,
    Generic,
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
/// Universal instance import engine supporting:
/// - Prism Launcher & MultiMC instance folders, root directories, or zip exports
/// - Modrinth modpack archives (.mrpack) and uncompressed folders
/// - CurseForge modpack archives (.zip) and uncompressed folders
/// - Vanilla .minecraft root or version directories
/// - Generic folders or zip archives with mods, config, saves, options
/// Auto-detects Minecraft version and modloader from configs, mod jars, or logs.
/// </summary>
public static class InstanceImportService
{
    private static readonly string[] CopyDirs =
    {
        "mods", "coremods", "config", "defaultconfigs", "saves", "resourcepacks",
        "shaderpacks", "scripts", "kubejs", "datapacks", "openloader", "schematics",
        "journeymap", "xaero", "patchouli_books", "local"
    };

    private static readonly string[] CopyFiles =
    {
        "options.txt", "optionsof.txt", "servers.dat", "servers.dat_old"
    };

    private static readonly Regex McVersionRegex = new(@"(?:mc|minecraft)[-_]?(1\.\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GeneralVersionRegex = new(@"[-_](1\.\d{2}(?:\.\d+)?)[-_.]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ImportSourceKind Detect(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return ImportSourceKind.None;

            if (File.Exists(path))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is ".zip" or ".mrpack" or ".rar")
                    return ImportSourceKind.Archive;
                return ImportSourceKind.None;
            }

            if (!Directory.Exists(path))
                return ImportSourceKind.None;

            if (File.Exists(Path.Combine(path, "prismlauncher.cfg")))
                return ImportSourceKind.Prism;
            if (File.Exists(Path.Combine(path, "multimc.cfg")))
                return ImportSourceKind.MultiMC;
            if (File.Exists(Path.Combine(path, "instance.cfg")) && File.Exists(Path.Combine(path, "mmc-pack.json")))
                return ImportSourceKind.Single;
            if (File.Exists(Path.Combine(path, "modrinth.index.json")))
                return ImportSourceKind.Modrinth;
            if (File.Exists(Path.Combine(path, "manifest.json")) && Directory.Exists(Path.Combine(path, "overrides")))
                return ImportSourceKind.CurseForge;
            if (Directory.Exists(Path.Combine(path, "versions")) || File.Exists(Path.Combine(path, "launcher_profiles.json")))
                return ImportSourceKind.Vanilla;

            if (Directory.Exists(Path.Combine(path, "mods")) ||
                Directory.Exists(Path.Combine(path, "config")) ||
                Directory.Exists(Path.Combine(path, "saves")) ||
                File.Exists(Path.Combine(path, "options.txt")) ||
                File.Exists(Path.Combine(path, "instance.cfg")))
                return ImportSourceKind.Generic;
        }
        catch { }
        return ImportSourceKind.None;
    }

    public static IReadOnlyList<FoundInstance> Scan(string path)
    {
        var out_ = new List<FoundInstance>();
        try
        {
            if (File.Exists(path))
            {
                var inst = ReadArchiveInstance(path);
                if (inst != null) out_.Add(inst);
                return out_;
            }

            var kind = Detect(path);
            var fallbackName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            switch (kind)
            {
                case ImportSourceKind.Single:
                case ImportSourceKind.Prism when File.Exists(Path.Combine(path, "instance.cfg")):
                case ImportSourceKind.MultiMC when File.Exists(Path.Combine(path, "instance.cfg")):
                    var single = ReadMmcInstanceDir(path, fallbackName);
                    if (single != null) out_.Add(single);
                    break;

                case ImportSourceKind.Prism:
                case ImportSourceKind.MultiMC:
                    var instancesDir = Path.Combine(path, "instances");
                    if (Directory.Exists(instancesDir))
                    {
                        foreach (var dir in Directory.GetDirectories(instancesDir).OrderBy(d => d))
                        {
                            var found = ReadMmcInstanceDir(dir, Path.GetFileName(dir));
                            if (found != null) out_.Add(found);
                        }
                    }
                    break;

                case ImportSourceKind.Modrinth:
                    var mr = ReadModrinthDir(path, fallbackName);
                    if (mr != null) out_.Add(mr);
                    break;

                case ImportSourceKind.CurseForge:
                    var cf = ReadCurseForgeDir(path, fallbackName);
                    if (cf != null) out_.Add(cf);
                    break;

                case ImportSourceKind.Vanilla:
                    out_.AddRange(ScanVanillaDir(path));
                    break;

                case ImportSourceKind.Generic:
                    var gen = ReadGenericDir(path, fallbackName);
                    if (gen != null) out_.Add(gen);
                    break;
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"InstanceImportService.Scan error: {ex.Message}");
        }
        return out_;
    }

    public static async Task<ImportResult> ImportAsync(
        IInstanceService svc, string path, CancellationToken ct = default)
    {
        string? tempExtractDir = null;
        try
        {
            var targetPath = path;
            if (File.Exists(path))
            {
                tempExtractDir = Path.Combine(Path.GetTempPath(), "ZenithImport_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempExtractDir);
                ZipFile.ExtractToDirectory(path, tempExtractDir);
                targetPath = tempExtractDir;
            }

            var found = Scan(targetPath);
            if (found.Count == 0 && tempExtractDir != null)
            {
                // In case archive had a single root folder inside
                var subDirs = Directory.GetDirectories(tempExtractDir);
                if (subDirs.Length == 1)
                {
                    found = Scan(subDirs[0]);
                }
            }

            var warnings = new List<string>();
            if (found.Count == 0)
                return new ImportResult(0, 0, warnings);

            int imported = 0, skipped = 0;
            foreach (var f in found)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var mcVer = !string.IsNullOrWhiteSpace(f.McVersion) ? f.McVersion : "1.20.1";
                    var loaderType = !string.IsNullOrWhiteSpace(f.LoaderType) ? f.LoaderType : "Vanilla";
                    var loaderBuild = f.LoaderBuild ?? "";

                    var name = UniqueName(svc, f.Name);
                    var inst = await svc.CreateInstanceAsync(name, mcVer, loaderType, loaderBuild);
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
        finally
        {
            if (tempExtractDir != null && Directory.Exists(tempExtractDir))
            {
                try { Directory.Delete(tempExtractDir, true); } catch { }
            }
        }
    }

    private static FoundInstance? ReadArchiveInstance(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var name = Path.GetFileNameWithoutExtension(zipPath);

            // 1. Modrinth mrpack
            var mrEntry = archive.GetEntry("modrinth.index.json");
            if (mrEntry != null)
            {
                using var stream = mrEntry.Open();
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;
                if (root.TryGetProperty("name", out var nEl)) name = nEl.GetString() ?? name;
                var mcVer = "";
                var loader = "Vanilla";
                var build = "";
                if (root.TryGetProperty("dependencies", out var deps))
                {
                    if (deps.TryGetProperty("minecraft", out var mcEl)) mcVer = mcEl.GetString() ?? "";
                    if (deps.TryGetProperty("fabric-loader", out var fEl)) { loader = "Fabric"; build = fEl.GetString() ?? ""; }
                    else if (deps.TryGetProperty("forge", out var foEl)) { loader = "Forge"; build = foEl.GetString() ?? ""; }
                    else if (deps.TryGetProperty("neoforge", out var nEl2)) { loader = "NeoForge"; build = nEl2.GetString() ?? ""; }
                    else if (deps.TryGetProperty("quilt-loader", out var qEl)) { loader = "Quilt"; build = qEl.GetString() ?? ""; }
                }
                return new FoundInstance(name, mcVer, loader, build, 0, "", "", zipPath);
            }

            // 2. CurseForge manifest.json
            var cfEntry = archive.GetEntry("manifest.json");
            if (cfEntry != null)
            {
                using var stream = cfEntry.Open();
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;
                if (root.TryGetProperty("name", out var nEl)) name = nEl.GetString() ?? name;
                var mcVer = "";
                var loader = "Vanilla";
                var build = "";
                if (root.TryGetProperty("minecraft", out var mcObj))
                {
                    if (mcObj.TryGetProperty("version", out var vEl)) mcVer = vEl.GetString() ?? "";
                    if (mcObj.TryGetProperty("modLoaders", out var mlArr) && mlArr.GetArrayLength() > 0)
                    {
                        var lid = mlArr[0].GetProperty("id").GetString() ?? "";
                        if (lid.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase)) { loader = "Fabric"; build = lid[7..]; }
                        else if (lid.StartsWith("forge-", StringComparison.OrdinalIgnoreCase)) { loader = "Forge"; build = lid[6..]; }
                        else if (lid.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase)) { loader = "NeoForge"; build = lid[9..]; }
                        else if (lid.StartsWith("quilt-", StringComparison.OrdinalIgnoreCase)) { loader = "Quilt"; build = lid[6..]; }
                    }
                }
                return new FoundInstance(name, mcVer, loader, build, 0, "", "", zipPath);
            }

            // 3. MultiMC mmc-pack.json inside zip
            var mmcEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("mmc-pack.json", StringComparison.OrdinalIgnoreCase));
            if (mmcEntry != null)
            {
                using var stream = mmcEntry.Open();
                using var doc = JsonDocument.Parse(stream);
                var mcVer = "";
                var loader = "Vanilla";
                var build = "";
                if (doc.RootElement.TryGetProperty("components", out var comps))
                {
                    foreach (var c in comps.EnumerateArray())
                    {
                        var uid = c.GetProperty("uid").GetString() ?? "";
                        var ver = c.TryGetProperty("version", out var vEl) ? vEl.GetString() ?? "" : "";
                        if (uid == "net.minecraft") mcVer = ver;
                        else if (uid == "net.fabricmc.fabric-loader") { loader = "Fabric"; build = ver; }
                        else if (uid == "net.minecraftforge") { loader = "Forge"; build = ver; }
                        else if (uid == "net.neoforged") { loader = "NeoForge"; build = ver; }
                        else if (uid == "org.quiltmc.quilt-loader") { loader = "Quilt"; build = ver; }
                    }
                }
                return new FoundInstance(name, mcVer, loader, build, 0, "", "", zipPath);
            }

            // 4. Inspect entries for mod jars or logs to deduce version & loader
            var (deducedVer, deducedLoader) = DeduceVersionAndLoaderFromArchive(archive);
            return new FoundInstance(name, deducedVer, deducedLoader, "", 0, "", "", zipPath);
        }
        catch { return null; }
    }

    private static (string mcVer, string loader) DeduceVersionAndLoaderFromArchive(ZipArchive archive)
    {
        string bestVer = "";
        string loader = "Vanilla";

        foreach (var entry in archive.Entries)
        {
            var n = entry.FullName.ToLowerInvariant();
            if (n.Contains("fabric")) loader = "Fabric";
            else if (n.Contains("neoforge")) loader = "NeoForge";
            else if (n.Contains("forge") && loader == "Vanilla") loader = "Forge";
            else if (n.Contains("quilt")) loader = "Quilt";

            if (string.IsNullOrEmpty(bestVer))
            {
                var match = McVersionRegex.Match(entry.Name);
                if (match.Success) bestVer = match.Groups[1].Value;
                else
                {
                    var matchGen = GeneralVersionRegex.Match(entry.Name);
                    if (matchGen.Success) bestVer = matchGen.Groups[1].Value;
                }
            }
        }

        return (!string.IsNullOrEmpty(bestVer) ? bestVer : "1.20.1", loader);
    }

    private static FoundInstance? ReadMmcInstanceDir(string dir, string fallbackName)
    {
        try
        {
            var cfg = ReadIni(Path.Combine(dir, "instance.cfg"));
            var name = cfg.TryGetValue("name", out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : fallbackName;
            var mcVersion = cfg.GetValueOrDefault("IntendedVersion") ?? cfg.GetValueOrDefault("MCVersion") ?? "";
            var loaderType = "Vanilla";
            var loaderBuild = "";

            var packPath = Path.Combine(dir, "mmc-pack.json");
            if (File.Exists(packPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(packPath));
                    if (doc.RootElement.TryGetProperty("components", out var comps))
                    {
                        foreach (var c in comps.EnumerateArray())
                        {
                            var uid = c.GetProperty("uid").GetString() ?? "";
                            var ver = c.TryGetProperty("version", out var vEl) ? vEl.GetString() ?? "" : "";
                            if (uid == "net.minecraft" && !string.IsNullOrWhiteSpace(ver)) mcVersion = ver;
                            else if (uid == "net.fabricmc.fabric-loader") { loaderType = "Fabric"; loaderBuild = ver; }
                            else if (uid == "net.minecraftforge") { loaderType = "Forge"; loaderBuild = StripMcPrefix(mcVersion, ver); }
                            else if (uid == "net.neoforged") { loaderType = "NeoForge"; loaderBuild = StripMcPrefix(mcVersion, ver); }
                            else if (uid == "org.quiltmc.quilt-loader") { loaderType = "Quilt"; loaderBuild = ver; }
                        }
                    }
                }
                catch { }
            }

            var gameDir = ResolveGameDirectory(dir);
            if (string.IsNullOrEmpty(mcVersion))
            {
                var (v, l) = InspectGameDirForVersion(gameDir);
                mcVersion = v;
                if (loaderType == "Vanilla") loaderType = l;
            }

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

    private static FoundInstance? ReadModrinthDir(string dir, string fallbackName)
    {
        try
        {
            var idxPath = Path.Combine(dir, "modrinth.index.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(idxPath));
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? fallbackName : fallbackName;
            var mcVer = "";
            var loader = "Vanilla";
            var build = "";

            if (root.TryGetProperty("dependencies", out var deps))
            {
                if (deps.TryGetProperty("minecraft", out var mcEl)) mcVer = mcEl.GetString() ?? "";
                if (deps.TryGetProperty("fabric-loader", out var fEl)) { loader = "Fabric"; build = fEl.GetString() ?? ""; }
                else if (deps.TryGetProperty("forge", out var foEl)) { loader = "Forge"; build = foEl.GetString() ?? ""; }
                else if (deps.TryGetProperty("neoforge", out var nEl2)) { loader = "NeoForge"; build = nEl2.GetString() ?? ""; }
                else if (deps.TryGetProperty("quilt-loader", out var qEl)) { loader = "Quilt"; build = qEl.GetString() ?? ""; }
            }

            var sourceDir = Directory.Exists(Path.Combine(dir, "overrides"))
                ? Path.Combine(dir, "overrides")
                : (Directory.Exists(Path.Combine(dir, "client-overrides")) ? Path.Combine(dir, "client-overrides") : dir);

            return new FoundInstance(name, mcVer, loader, build, 0, "", "", sourceDir);
        }
        catch { return null; }
    }

    private static FoundInstance? ReadCurseForgeDir(string dir, string fallbackName)
    {
        try
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? fallbackName : fallbackName;
            var mcVer = "";
            var loader = "Vanilla";
            var build = "";

            if (root.TryGetProperty("minecraft", out var mcObj))
            {
                if (mcObj.TryGetProperty("version", out var vEl)) mcVer = vEl.GetString() ?? "";
                if (mcObj.TryGetProperty("modLoaders", out var mlArr) && mlArr.GetArrayLength() > 0)
                {
                    var lid = mlArr[0].GetProperty("id").GetString() ?? "";
                    if (lid.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase)) { loader = "Fabric"; build = lid[7..]; }
                    else if (lid.StartsWith("forge-", StringComparison.OrdinalIgnoreCase)) { loader = "Forge"; build = lid[6..]; }
                    else if (lid.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase)) { loader = "NeoForge"; build = lid[9..]; }
                    else if (lid.StartsWith("quilt-", StringComparison.OrdinalIgnoreCase)) { loader = "Quilt"; build = lid[6..]; }
                }
            }

            var sourceDir = Directory.Exists(Path.Combine(dir, "overrides"))
                ? Path.Combine(dir, "overrides")
                : dir;

            return new FoundInstance(name, mcVer, loader, build, 0, "", "", sourceDir);
        }
        catch { return null; }
    }

    private static List<FoundInstance> ScanVanillaDir(string dir)
    {
        var result = new List<FoundInstance>();
        var versionsDir = Path.Combine(dir, "versions");
        if (Directory.Exists(versionsDir))
        {
            foreach (var vDir in Directory.GetDirectories(versionsDir))
            {
                var vName = Path.GetFileName(vDir);
                var jsonPath = Path.Combine(vDir, vName + ".json");
                if (!File.Exists(jsonPath)) continue;

                var (mcVer, loader, build) = ParseVanillaVersionJson(jsonPath);
                result.Add(new FoundInstance(vName, mcVer, loader, build, 0, "", "", dir));
            }
        }
        else
        {
            var gen = ReadGenericDir(dir, Path.GetFileName(dir));
            if (gen != null) result.Add(gen);
        }
        return result;
    }

    private static (string mcVer, string loader, string build) ParseVanillaVersionJson(string jsonPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
            var mainClass = root.TryGetProperty("mainClass", out var mcEl) ? mcEl.GetString() ?? "" : "";
            var inheritsFrom = root.TryGetProperty("inheritsFrom", out var inhEl) ? inhEl.GetString() ?? "" : "";

            var loader = "Vanilla";
            var build = "";
            var mcVer = !string.IsNullOrEmpty(inheritsFrom) ? inheritsFrom : id;

            if (mainClass.Contains("fabric", StringComparison.OrdinalIgnoreCase) || id.Contains("fabric", StringComparison.OrdinalIgnoreCase))
                loader = "Fabric";
            else if (mainClass.Contains("neoforge", StringComparison.OrdinalIgnoreCase) || id.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
                loader = "NeoForge";
            else if (mainClass.Contains("forge", StringComparison.OrdinalIgnoreCase) || id.Contains("forge", StringComparison.OrdinalIgnoreCase))
                loader = "Forge";
            else if (mainClass.Contains("quilt", StringComparison.OrdinalIgnoreCase) || id.Contains("quilt", StringComparison.OrdinalIgnoreCase))
                loader = "Quilt";

            var match = McVersionRegex.Match(id);
            if (match.Success) mcVer = match.Groups[1].Value;

            return (mcVer, loader, build);
        }
        catch { return ("1.20.1", "Vanilla", ""); }
    }

    private static FoundInstance? ReadGenericDir(string dir, string fallbackName)
    {
        var gameDir = ResolveGameDirectory(dir);
        var (ver, loader) = InspectGameDirForVersion(gameDir);
        return new FoundInstance(fallbackName, ver, loader, "", 0, "", "", gameDir);
    }

    private static string ResolveGameDirectory(string dir)
    {
        if (Directory.Exists(Path.Combine(dir, ".minecraft"))) return Path.Combine(dir, ".minecraft");
        if (Directory.Exists(Path.Combine(dir, "minecraft"))) return Path.Combine(dir, "minecraft");
        if (Directory.Exists(Path.Combine(dir, "overrides"))) return Path.Combine(dir, "overrides");
        return dir;
    }

    private static (string mcVer, string loader) InspectGameDirForVersion(string gameDir)
    {
        string bestVer = "";
        string loader = "Vanilla";

        // 1. Inspect logs
        var logFile = Path.Combine(gameDir, "logs", "latest.log");
        if (File.Exists(logFile))
        {
            try
            {
                var lines = File.ReadLines(logFile).Take(200);
                foreach (var line in lines)
                {
                    if (line.Contains("Fabric", StringComparison.OrdinalIgnoreCase)) loader = "Fabric";
                    else if (line.Contains("NeoForge", StringComparison.OrdinalIgnoreCase)) loader = "NeoForge";
                    else if (line.Contains("Forge", StringComparison.OrdinalIgnoreCase) && loader == "Vanilla") loader = "Forge";

                    var m = McVersionRegex.Match(line);
                    if (m.Success)
                    {
                        bestVer = m.Groups[1].Value;
                        break;
                    }
                }
            }
            catch { }
        }

        // 2. Inspect mods directory
        var modsDir = Path.Combine(gameDir, "mods");
        if (Directory.Exists(modsDir))
        {
            var jarFiles = Directory.GetFiles(modsDir, "*.jar");
            foreach (var jar in jarFiles)
            {
                var name = Path.GetFileName(jar).ToLowerInvariant();
                if (name.Contains("fabric")) loader = "Fabric";
                else if (name.Contains("neoforge")) loader = "NeoForge";
                else if (name.Contains("forge") && loader == "Vanilla") loader = "Forge";
                else if (name.Contains("quilt")) loader = "Quilt";

                if (string.IsNullOrEmpty(bestVer))
                {
                    var m = McVersionRegex.Match(name);
                    if (m.Success) bestVer = m.Groups[1].Value;
                    else
                    {
                        var mGen = GeneralVersionRegex.Match(name);
                        if (mGen.Success) bestVer = mGen.Groups[1].Value;
                    }
                }
            }
        }

        return (!string.IsNullOrEmpty(bestVer) ? bestVer : "1.20.1", loader);
    }

    private static string StripMcPrefix(string mcVersion, string loaderVersion)
    {
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
}
