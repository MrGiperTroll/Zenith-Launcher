using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

public record ServerSoftwareOption(string Id, string DisplayName);
public record ServerNetworkEndpoint(string Label, string Address, string TypeName);

public class ServerPluginItem
{
    public string FileName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string FileSizeDisplay { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsPlugin { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string Size => FileSizeDisplay;
    public string StatusText => IsEnabled ? "Active" : "Disabled";
    public string ToggleButtonText => IsEnabled ? L10n.T("cs_plugin_disable") : L10n.T("cs_plugin_enable");

    public ServerPluginItem() { }
    public ServerPluginItem(string fileName, string fileSizeDisplay, string fullPath, bool isPlugin)
    {
        FileName = fileName;
        Name = Path.GetFileNameWithoutExtension(fileName);
        FileSizeDisplay = fileSizeDisplay;
        FullPath = fullPath;
        IsPlugin = isPlugin;
        IsEnabled = !fullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
    }
}

public class ServerFileItem
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long SizeBytes { get; set; }
    public string SizeDisplay { get; set; } = "";
    public string ModifiedDisplay { get; set; } = "";
    public string IconKind { get; set; } = "file";
    public string IconData { get; set; } = "";
    public string IconColor { get; set; } = "#9CA3AF";
}

public class ServerPlayerEntry
{
    public string Uuid { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public int Level { get; set; } = 4;
    public bool BypassesPlayerLimit { get; set; } = false;
    public string Reason { get; set; } = "Banned by an operator.";
    public string Expires { get; set; } = "forever";
}

public class ServerCreatorService
{
    private const string MojangManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static string DefaultServersDirectory => Path.Combine(ZenithPaths.AppDataDir, "servers");

    public static IReadOnlyList<ServerSoftwareOption> AvailableSoftware { get; } = new List<ServerSoftwareOption>
    {
        new("vanilla", "Vanilla"),
        new("paper", "Paper"),
        new("purpur", "Purpur"),
        new("spigot", "Spigot"),
        new("fabric", "Fabric"),
        new("forge", "Forge"),
        new("neoforge", "NeoForge")
    };

    public static IReadOnlyList<ServerSoftwareOption> GetCompatibleSoftware(string mcVersion)
    {
        var list = new List<ServerSoftwareOption>
        {
            new("vanilla", "Vanilla")
        };

        if (string.IsNullOrWhiteSpace(mcVersion)) return AvailableSoftware;

        var v = ParseVersionParts(mcVersion);
        bool isSnapshotOrTest = mcVersion.Contains("w") || mcVersion.Contains("pre") || mcVersion.Contains("rc") || mcVersion.Contains("Snapshot");

        // Spigot: >= 1.8 (or 1.4.7+)
        if (v.Minor >= 8 || v.Major > 1)
        {
            list.Add(new("spigot", "Spigot"));
        }

        // Paper: >= 1.8.8
        if (v.Minor > 8 || (v.Minor == 8 && v.Patch >= 8) || v.Major > 1)
        {
            list.Add(new("paper", "Paper"));
        }

        // Purpur: >= 1.14.1
        if (v.Minor >= 14 || v.Major > 1)
        {
            list.Add(new("purpur", "Purpur"));
        }

        // Fabric: >= 1.14 or modern snapshots
        if (v.Minor >= 14 || v.Major > 1 || (isSnapshotOrTest && (mcVersion.StartsWith("19w") || mcVersion.StartsWith("2"))))
        {
            list.Add(new("fabric", "Fabric"));
        }

        // Forge: >= 1.1 up to 1.20.6 (Forge does not support vanilla snapshots without port)
        if (v.Minor >= 1 && !isSnapshotOrTest)
        {
            list.Add(new("forge", "Forge"));
        }

        // NeoForge: >= 1.20.2
        if (!isSnapshotOrTest && ((v.Minor == 20 && v.Patch >= 2) || v.Minor > 20 || v.Major > 1))
        {
            list.Add(new("neoforge", "NeoForge"));
        }

        return list;
    }

    private static (int Major, int Minor, int Patch) ParseVersionParts(string version)
    {
        var parts = version.Split('.');
        int maj = 1, min = 0, patch = 0;
        if (parts.Length > 0 && int.TryParse(parts[0], out var p0)) maj = p0;
        if (parts.Length > 1 && int.TryParse(parts[1], out var p1)) min = p1;
        if (parts.Length > 2 && int.TryParse(parts[2], out var p2)) patch = p2;
        return (maj, min, patch);
    }

    public async Task<List<string>> GetPopularReleaseVersionsAsync(CancellationToken ct = default)
    {
        return await GetServerVersionsAsync(true, false, false, false, ct);
    }

    public async Task<List<string>> GetServerVersionsAsync(bool showReleases, bool showSnapshots, bool showBetas, bool showAlphas, CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync(MojangManifestUrl, ct);
            using var doc = JsonDocument.Parse(json);
            var versions = new List<string>();
            if (doc.RootElement.TryGetProperty("versions", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var type = item.TryGetProperty("type", out var t) ? t.GetString() : "";
                    var id = item.TryGetProperty("id", out var i) ? i.GetString() : "";
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    bool include = false;
                    if (type == "release" && showReleases) include = true;
                    else if (type == "snapshot" && showSnapshots) include = true;
                    else if (type == "old_beta" && showBetas) include = true;
                    else if (type == "old_alpha" && showAlphas) include = true;

                    if (include) versions.Add(id);
                }
            }
            if (versions.Count > 0) return versions;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to fetch Mojang manifest for server versions", ex);
        }

        // Fallback default popular versions if offline
        return new List<string> { "1.21.4", "1.21.1", "1.20.4", "1.20.1", "1.19.4", "1.18.2", "1.16.5", "1.12.2" };
    }

    public async Task<string> CreateServerAsync(
        string serverName,
        string version,
        string software,
        int ramMb,
        int port,
        bool agreeEula,
        bool onlineMode,
        IProgress<(string Status, double Percent)>? progress = null,
        CancellationToken ct = default)
    {
        // Safe conversion if caller accidentally passed GB <= 64
        if (ramMb <= 64 && ramMb > 0) ramMb *= 1024;
        if (ramMb < 512) ramMb = 4096;

        var safeName = string.Join("_", serverName.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "MinecraftServer";

        var targetDir = Path.Combine(DefaultServersDirectory, safeName);
        Directory.CreateDirectory(targetDir);

        progress?.Report(("Resolving server download...", 10));

        var serverJarPath = Path.Combine(targetDir, "server.jar");

        switch (software.ToLowerInvariant())
        {
            case "paper":
                await DownloadPaperServerAsync(version, serverJarPath, progress, ct);
                break;
            case "purpur":
                await DownloadPurpurServerAsync(version, serverJarPath, progress, ct);
                break;
            case "spigot":
                await DownloadSpigotServerAsync(version, serverJarPath, progress, ct);
                break;
            case "fabric":
                await DownloadFabricServerAsync(version, serverJarPath, progress, ct);
                break;
            case "forge":
                await DownloadForgeServerAsync(version, serverJarPath, progress, ct);
                break;
            case "neoforge":
                await DownloadNeoForgeServerAsync(version, serverJarPath, progress, ct);
                break;
            case "vanilla":
            default:
                await DownloadVanillaServerAsync(version, serverJarPath, progress, ct);
                break;
        }

        progress?.Report(("Configuring server files...", 90));

        // 1. EULA
        var eulaPath = Path.Combine(targetDir, "eula.txt");
        await File.WriteAllTextAsync(eulaPath,
            "# By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).\n" +
            $"# Created by Zenith Launcher on {DateTime.UtcNow:u}\n" +
            $"eula={(agreeEula ? "true" : "false")}\n", ct);

        // 2. server.properties
        var propsPath = Path.Combine(targetDir, "server.properties");
        if (!File.Exists(propsPath))
        {
            var props =
                $"# Minecraft server properties\n" +
                $"# Created by Zenith Launcher\n" +
                $"server-port={port}\n" +
                $"motd={serverName} (Zenith Launcher)\n" +
                $"online-mode={(onlineMode ? "true" : "false")}\n" +
                $"gamemode=survival\n" +
                $"difficulty=normal\n" +
                $"max-players=20\n" +
                $"view-distance=10\n" +
                $"simulation-distance=10\n" +
                $"white-list=false\n" +
                $"enable-command-block=false\n" +
                $"spawn-protection=16\n";
            await File.WriteAllTextAsync(propsPath, props, ct);
        }

        // 3. run.bat (Windows batch file) - Uses strictly MB notation and graceful errorlevel check
        var minRamMb = Math.Max(512, ramMb / 2);
        var runBatPath = Path.Combine(targetDir, "run.bat");
        var resolvedJava = JavaVersionHelper.FindOrResolveServerJava(targetDir, version, out _);
        var javaCmd = string.IsNullOrWhiteSpace(resolvedJava) ? "java" : $"\"{resolvedJava}\"";
        var runBat =
            "@echo off\r\n" +
            $"title Minecraft Server - {serverName}\r\n" +
            $"echo Starting Minecraft Server ({version} - {software})...\r\n" +
            $"{javaCmd} -Xms{minRamMb}M -Xmx{ramMb}M -jar server.jar nogui\r\n" +
            "if %ERRORLEVEL% NEQ 0 pause\r\n";
        await File.WriteAllTextAsync(runBatPath, runBat, ct);

        // 4. run.sh (Linux / macOS shell script)
        var runShPath = Path.Combine(targetDir, "run.sh");
        var runSh =
            "#!/bin/bash\n" +
            $"echo \"Starting Minecraft Server ({version} - {software})...\"\n" +
            $"java -Xms{minRamMb}M -Xmx{ramMb}M -jar server.jar nogui\n";
        await File.WriteAllTextAsync(runShPath, runSh, ct);

        progress?.Report(("Server ready!", 100));
        return targetDir;
    }

    private async Task DownloadVanillaServerAsync(string version, string targetJar, IProgress<(string Status, double Percent)>? progress, CancellationToken ct)
    {
        progress?.Report(($"Fetching Vanilla {version} manifest...", 20));
        var manifestJson = await Http.GetStringAsync(MojangManifestUrl, ct);
        using var manifestDoc = JsonDocument.Parse(manifestJson);

        string? versionUrl = null;
        if (manifestDoc.RootElement.TryGetProperty("versions", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id) && id.GetString() == version)
                {
                    if (item.TryGetProperty("url", out var u))
                        versionUrl = u.GetString();
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(versionUrl))
            throw new InvalidOperationException($"Could not find Vanilla version manifest for {version}.");

        progress?.Report(($"Resolving Vanilla {version} server jar...", 35));
        var versionDetailsJson = await Http.GetStringAsync(versionUrl, ct);
        using var versionDoc = JsonDocument.Parse(versionDetailsJson);

        string? downloadUrl = null;
        if (versionDoc.RootElement.TryGetProperty("downloads", out var downloads) &&
            downloads.TryGetProperty("server", out var serverObj) &&
            serverObj.TryGetProperty("url", out var dlUrl))
        {
            downloadUrl = dlUrl.GetString();
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new InvalidOperationException($"Mojang does not provide an official server download for version {version}.");

        await DownloadFileWithProgressAsync(downloadUrl, targetJar, $"Downloading Vanilla {version} server.jar...", progress, 40, 85, ct);
    }

    private async Task DownloadPaperServerAsync(string version, string targetJar, IProgress<(string Status, double Percent)>? progress, CancellationToken ct)
    {
        progress?.Report(($"Checking Paper builds for {version}...", 20));
        try
        {
            var buildsUrl = $"https://api.papermc.io/v2/projects/paper/versions/{version}/builds";
            var buildsJson = await Http.GetStringAsync(buildsUrl, ct);
            using var doc = JsonDocument.Parse(buildsJson);

            if (doc.RootElement.TryGetProperty("builds", out var buildsArr) && buildsArr.ValueKind == JsonValueKind.Array)
            {
                var builds = buildsArr.EnumerateArray().ToList();
                if (builds.Count > 0)
                {
                    var latestBuild = builds[^1];
                    var buildNum = latestBuild.GetProperty("build").GetInt32();
                    var fileName = latestBuild.GetProperty("downloads").GetProperty("application").GetProperty("name").GetString()
                                   ?? $"paper-{version}-{buildNum}.jar";

                    var downloadUrl = $"https://api.papermc.io/v2/projects/paper/versions/{version}/builds/{buildNum}/downloads/{fileName}";
                    await DownloadFileWithProgressAsync(downloadUrl, targetJar, $"Downloading Paper {version} (build #{buildNum})...", progress, 30, 85, ct);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Paper not available for {version} ({ex.Message}), falling back to Vanilla server.");
        }

        // Fallback to Vanilla if Paper doesn't support this version (e.g. snapshots)
        await DownloadVanillaServerAsync(version, targetJar, progress, ct);
    }

    private async Task DownloadFabricServerAsync(string version, string targetJar, IProgress<(string Status, double Percent)>? progress, CancellationToken ct)
    {
        progress?.Report(($"Checking Fabric loader for {version}...", 20));
        try
        {
            var loadersUrl = "https://meta.fabricmc.net/v2/versions/loader";
            var loadersJson = await Http.GetStringAsync(loadersUrl, ct);
            using var doc = JsonDocument.Parse(loadersJson);
            var latestLoader = doc.RootElement.EnumerateArray().FirstOrDefault().GetProperty("version").GetString() ?? "0.16.10";

            var installerUrl = "https://meta.fabricmc.net/v2/versions/installer";
            var installerJson = await Http.GetStringAsync(installerUrl, ct);
            using var instDoc = JsonDocument.Parse(installerJson);
            var latestInstaller = instDoc.RootElement.EnumerateArray().FirstOrDefault().GetProperty("version").GetString() ?? "1.0.1";

            var serverDownloadUrl = $"https://meta.fabricmc.net/v2/versions/loader/{version}/{latestLoader}/{latestInstaller}/server/jar";
            await DownloadFileWithProgressAsync(serverDownloadUrl, targetJar, $"Downloading Fabric {version} server...", progress, 30, 85, ct);
            return;
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Fabric server fetch failed for {version} ({ex.Message}), falling back to Vanilla.");
        }

        await DownloadVanillaServerAsync(version, targetJar, progress, ct);
    }

    private async Task DownloadPurpurServerAsync(string version, string targetJar, IProgress<(string Status, double Percent)>? progress, CancellationToken ct)
    {
        progress?.Report(($"Checking Purpur {version} server...", 20));
        try
        {
            var purpurUrl = $"https://api.purpurmc.org/v2/purpur/{version}/latest/download";
            await DownloadFileWithProgressAsync(purpurUrl, targetJar, $"Downloading Purpur {version}...", progress, 25, 85, ct);
            return;
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Purpur download failed for {version} ({ex.Message}), falling back to Paper.");
        }

        await DownloadPaperServerAsync(version, targetJar, progress, ct);
    }

    private async Task DownloadSpigotServerAsync(string version, string targetJar, IProgress<(string Status, double Percent)>? progress, CancellationToken ct)
    {
        progress?.Report(($"Checking Spigot {version} server...", 20));
        try
        {
            var spigotUrl = $"https://download.getbukkit.org/spigot/spigot-{version}.jar";
            await DownloadFileWithProgressAsync(spigotUrl, targetJar, $"Downloading Spigot {version}...", progress, 25, 85, ct);
            return;
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Direct Spigot download failed for {version} ({ex.Message}), falling back to Paper.");
        }

        await DownloadPaperServerAsync(version, targetJar, progress, ct);
    }

    private async Task DownloadForgeServerAsync(string version, string targetJar, IProgress<(string Status, double Percent)>? progress, CancellationToken ct)
    {
        progress?.Report(($"Resolving Forge server for {version}...", 20));
        try
        {
            var modLoaderService = new ModLoaderService();
            var builds = await modLoaderService.GetLoaderBuildsAsync(version, "Forge");
            var forgeVer = builds.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b) && b != "Latest (Auto)");
            if (!string.IsNullOrEmpty(forgeVer))
            {
                var url = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{version}-{forgeVer}/forge-{version}-{forgeVer}-installer.jar";
                await DownloadFileWithProgressAsync(url, targetJar, $"Downloading Forge {version}-{forgeVer}...", progress, 25, 85, ct);
                await RunServerInstallerAsync(Path.GetDirectoryName(targetJar)!, targetJar, progress, ct);
                return;
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Forge fetch failed for {version} ({ex.Message}), falling back to Vanilla.");
        }

        await DownloadVanillaServerAsync(version, targetJar, progress, ct);
    }

    private async Task DownloadNeoForgeServerAsync(string version, string targetJar, IProgress<(string Status, double Percent)>? progress, CancellationToken ct)
    {
        progress?.Report(($"Resolving NeoForge server for {version}...", 20));
        try
        {
            var modLoaderService = new ModLoaderService();
            var builds = await modLoaderService.GetLoaderBuildsAsync(version, "NeoForge");
            var neoVer = builds.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b) && b != "Latest (Auto)");
            if (!string.IsNullOrEmpty(neoVer))
            {
                var url = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{neoVer}/neoforge-{neoVer}-installer.jar";
                await DownloadFileWithProgressAsync(url, targetJar, $"Downloading NeoForge {neoVer}...", progress, 25, 85, ct);
                await RunServerInstallerAsync(Path.GetDirectoryName(targetJar)!, targetJar, progress, ct);
                return;
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"NeoForge fetch failed for {version} ({ex.Message}), falling back to Vanilla.");
        }

        await DownloadVanillaServerAsync(version, targetJar, progress, ct);
    }

    private static async Task RunServerInstallerAsync(string serverDir, string installerJar, IProgress<(string Status, double Percent)>? progress, CancellationToken ct)
    {
        progress?.Report(("Running server installer...", 80));
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = $"-jar \"{installerJar}\" --installServer",
                WorkingDirectory = serverDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct);
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Server installer execution notice: {ex.Message}");
        }
    }

    public static void DeleteServer(string serverDir)
    {
        if (string.IsNullOrWhiteSpace(serverDir) || !Directory.Exists(serverDir)) return;
        try
        {
            Directory.Delete(serverDir, true);
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to delete server directory {serverDir}", ex);
            throw;
        }
    }

    private static async Task DownloadFileWithProgressAsync(
        string url,
        string destinationPath,
        string statusPrefix,
        IProgress<(string Status, double Percent)>? progress,
        double minPercent,
        double maxPercent,
        CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[16384];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;

            if (totalBytes > 0 && progress != null)
            {
                var ratio = (double)totalRead / totalBytes;
                var currentPercent = minPercent + ratio * (maxPercent - minPercent);
                var mbRead = (double)totalRead / (1024 * 1024);
                var mbTotal = (double)totalBytes / (1024 * 1024);
                progress.Report(($"{statusPrefix} ({mbRead:F1} / {mbTotal:F1} MB)", currentPercent));
            }
        }
    }

    public static List<string> GetExistingServers()
    {
        var list = new List<string>();
        try
        {
            if (Directory.Exists(DefaultServersDirectory))
            {
                foreach (var dir in Directory.GetDirectories(DefaultServersDirectory))
                {
                    list.Add(Path.GetFileName(dir));
                }
            }
        }
        catch { }
        return list;
    }

    public static Dictionary<string, string> LoadProperties(string serverDir)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var file = Path.Combine(serverDir, "server.properties");
        if (!File.Exists(file)) return props;

        foreach (var line in File.ReadAllLines(file))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#") || !trimmed.Contains('=')) continue;
            var idx = trimmed.IndexOf('=');
            var key = trimmed[..idx].Trim();
            var val = trimmed[(idx + 1)..].Trim();
            props[key] = val;
        }
        return props;
    }

    public static void SaveProperties(string serverDir, IDictionary<string, string> properties)
    {
        var file = Path.Combine(serverDir, "server.properties");
        var existingLines = File.Exists(file) ? File.ReadAllLines(file).ToList() : new List<string>();
        var writtenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var newLines = new List<string>();
        foreach (var line in existingLines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#") || !trimmed.Contains('='))
            {
                newLines.Add(line);
                continue;
            }

            var idx = trimmed.IndexOf('=');
            var key = trimmed[..idx].Trim();
            if (properties.TryGetValue(key, out var val))
            {
                newLines.Add($"{key}={val}");
                writtenKeys.Add(key);
            }
            else
            {
                newLines.Add(line);
                writtenKeys.Add(key);
            }
        }

        foreach (var kvp in properties)
        {
            if (!writtenKeys.Contains(kvp.Key))
            {
                newLines.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        File.WriteAllLines(file, newLines);
    }

    public static List<ServerPluginItem> GetInstalledPluginsOrMods(string serverDir)
    {
        var list = new List<ServerPluginItem>();
        try
        {
            var pluginsDir = Path.Combine(serverDir, "plugins");
            if (Directory.Exists(pluginsDir))
            {
                foreach (var f in Directory.GetFiles(pluginsDir, "*.*")
                    .Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                                p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(ReadPluginOrModItem(f, true));
                }
            }

            var modsDir = Path.Combine(serverDir, "mods");
            if (Directory.Exists(modsDir))
            {
                foreach (var f in Directory.GetFiles(modsDir, "*.*")
                    .Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                                p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(ReadPluginOrModItem(f, false));
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Failed to read installed plugins/mods: {ex.Message}");
        }
        return list;
    }

    private static ServerPluginItem ReadPluginOrModItem(string filePath, bool isPlugin)
    {
        var fi = new FileInfo(filePath);
        var isEnabled = !filePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var cleanFileName = isEnabled ? fi.Name : fi.Name[..^".disabled".Length];
        var fallbackName = Path.GetFileNameWithoutExtension(cleanFileName);

        string name = fallbackName;
        string version = "";
        string description = isPlugin ? "Bukkit / Spigot / Paper Plugin" : "Fabric / Forge Mod";

        try
        {
            using var zip = ZipFile.OpenRead(filePath);

            // 1. Spigot / Paper plugin.yml or paper-plugin.yml
            var pluginYml = zip.GetEntry("plugin.yml") ?? zip.GetEntry("paper-plugin.yml");
            if (pluginYml != null)
            {
                using var stream = pluginYml.Open();
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed[5..].Trim(' ', '"', '\'');
                        if (!string.IsNullOrWhiteSpace(val)) name = val;
                    }
                    else if (trimmed.StartsWith("version:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed[8..].Trim(' ', '"', '\'');
                        if (!string.IsNullOrWhiteSpace(val)) version = val;
                    }
                    else if (trimmed.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed[12..].Trim(' ', '"', '\'');
                        if (!string.IsNullOrWhiteSpace(val)) description = val;
                    }
                }
            }
            else
            {
                // 2. Fabric mod: fabric.mod.json
                var fabricEntry = zip.GetEntry("fabric.mod.json");
                if (fabricEntry != null)
                {
                    using var stream = fabricEntry.Open();
                    using var doc = JsonDocument.Parse(stream);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString())) name = n.GetString()!;
                    if (root.TryGetProperty("version", out var v) && !string.IsNullOrWhiteSpace(v.GetString())) version = v.GetString()!;
                    if (root.TryGetProperty("description", out var d) && !string.IsNullOrWhiteSpace(d.GetString())) description = d.GetString()!;
                }
                else
                {
                    // 3. Forge / NeoForge: META-INF/mods.toml
                    var modsToml = zip.GetEntry("META-INF/mods.toml");
                    if (modsToml != null)
                    {
                        using var stream = modsToml.Open();
                        using var reader = new StreamReader(stream);
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            var trimmed = line.Trim();
                            if (trimmed.StartsWith("displayName", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
                            {
                                var val = trimmed[(trimmed.IndexOf('=') + 1)..].Trim(' ', '"', '\'');
                                if (!string.IsNullOrWhiteSpace(val)) name = val;
                            }
                            else if (trimmed.StartsWith("version", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
                            {
                                var val = trimmed[(trimmed.IndexOf('=') + 1)..].Trim(' ', '"', '\'');
                                if (!string.IsNullOrWhiteSpace(val)) version = val;
                            }
                            else if (trimmed.StartsWith("description", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
                            {
                                var val = trimmed[(trimmed.IndexOf('=') + 1)..].Trim(' ', '"', '\'');
                                if (!string.IsNullOrWhiteSpace(val)) description = val;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return new ServerPluginItem
        {
            FileName = fi.Name,
            Name = name,
            Version = version,
            Description = description,
            FileSizeDisplay = FormatBytes(fi.Length),
            FullPath = fi.FullName,
            IsPlugin = isPlugin,
            IsEnabled = isEnabled
        };
    }

    public static bool TogglePluginItem(ServerPluginItem item) => TogglePluginItem(item, out _, out _);

    public static bool TogglePluginItem(ServerPluginItem item, out string? newPath, out string? error)
    {
        newPath = null;
        error = null;
        try
        {
            if (!File.Exists(item.FullPath))
            {
                error = "File does not exist.";
                return false;
            }

            if (item.IsEnabled)
            {
                newPath = item.FullPath + ".disabled";
            }
            else
            {
                newPath = item.FullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? item.FullPath[..^".disabled".Length]
                    : item.FullPath;
            }

            File.Move(item.FullPath, newPath, true);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    public static void ResetWorld(string serverDir, string worldName = "world")
    {
        if (string.IsNullOrWhiteSpace(worldName)) worldName = "world";
        var targets = new[]
        {
            Path.Combine(serverDir, worldName),
            Path.Combine(serverDir, $"{worldName}_nether"),
            Path.Combine(serverDir, $"{worldName}_the_end")
        };

        foreach (var t in targets)
        {
            if (Directory.Exists(t))
            {
                try { Directory.Delete(t, true); } catch (Exception ex) { LauncherLog.Warn($"Could not delete world dir {t}: {ex.Message}"); }
            }
        }
    }

    public static List<ServerPlayerEntry> LoadPlayerList(string serverDir, string fileName)
    {
        var list = new List<ServerPlayerEntry>();
        var filePath = Path.Combine(serverDir, fileName);
        if (!File.Exists(filePath)) return list;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var entry = new ServerPlayerEntry();
                    if (el.TryGetProperty("name", out var n)) entry.Name = n.GetString() ?? "";
                    if (el.TryGetProperty("uuid", out var u)) entry.Uuid = u.GetString() ?? "";
                    if (el.TryGetProperty("level", out var l)) entry.Level = l.GetInt32();
                    if (el.TryGetProperty("reason", out var r)) entry.Reason = r.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(entry.Name)) list.Add(entry);
                }
            }
        }
        catch { }
        return list;
    }

    public static void SavePlayerList(string serverDir, string fileName, IEnumerable<ServerPlayerEntry> entries)
    {
        var filePath = Path.Combine(serverDir, fileName);
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var rawList = entries.Select(e => new Dictionary<string, object>
            {
                ["uuid"] = string.IsNullOrWhiteSpace(e.Uuid) ? Guid.NewGuid().ToString() : e.Uuid,
                ["name"] = e.Name,
                ["level"] = e.Level,
                ["bypassesPlayerLimit"] = e.BypassesPlayerLimit,
                ["reason"] = e.Reason
            }).ToList();
            File.WriteAllText(filePath, JsonSerializer.Serialize(rawList, options));
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to save player list {fileName}", ex);
        }
    }

    public static void GetServerMetadata(string serverDir, out string software, out string ram)
    {
        GetServerMetadata(serverDir, out software, out ram, out _, out _, out _);
    }

    public static void GetServerMetadata(string serverDir, out string software, out string ramDisplay, out int ramMb, out string javaPath, out string jvmArgs)
    {
        software = "Vanilla";
        ramMb = 4096;
        ramDisplay = "4096 MB";
        javaPath = "";
        jvmArgs = "";

        try
        {
            var runBat = Path.Combine(serverDir, "run.bat");
            if (File.Exists(runBat))
            {
                var content = File.ReadAllText(runBat);

                // Auto-sanitize on read if bare pause is still present
                if (System.Text.RegularExpressions.Regex.IsMatch(content, @"(?m)^\s*pause\s*$"))
                {
                    content = System.Text.RegularExpressions.Regex.Replace(content, @"(?m)^\s*pause\s*$", "if %ERRORLEVEL% NEQ 0 pause");
                    try { File.WriteAllText(runBat, content); } catch { }
                }

                // RAM in MB or GB
                var mRamMb = System.Text.RegularExpressions.Regex.Match(content, @"-Xmx(\d+)[mM]");
                if (mRamMb.Success && int.TryParse(mRamMb.Groups[1].Value, out var mb))
                {
                    ramMb = mb;
                    ramDisplay = $"{mb} MB";
                }
                else
                {
                    var mRam = System.Text.RegularExpressions.Regex.Match(content, @"-Xmx(\d+)[gG]");
                    if (mRam.Success && int.TryParse(mRam.Groups[1].Value, out var gb))
                    {
                        ramMb = gb * 1024;
                        ramDisplay = $"{ramMb} MB";
                    }
                }

                // Java path
                var mJava = System.Text.RegularExpressions.Regex.Match(content, @"(?:^|\r?\n)(?:""([^""]+)""|([^\s\r\n]+java(?:\.exe)?))\s+");
                if (mJava.Success)
                {
                    javaPath = !string.IsNullOrEmpty(mJava.Groups[1].Value) ? mJava.Groups[1].Value : mJava.Groups[2].Value;
                }

                // JVM args: flags between -Xmx and -jar
                var jarIdx = content.IndexOf("-jar", StringComparison.OrdinalIgnoreCase);
                var xmxMatch = System.Text.RegularExpressions.Regex.Match(content, @"-Xmx\d+[gGmM]");
                if (jarIdx > 0 && xmxMatch.Success && xmxMatch.Index < jarIdx)
                {
                    var start = xmxMatch.Index + xmxMatch.Length;
                    var between = content.Substring(start, jarIdx - start).Trim();
                    if (!string.IsNullOrWhiteSpace(between))
                    {
                        jvmArgs = between;
                    }
                }

                var mSoft = System.Text.RegularExpressions.Regex.Match(content, @"Starting Minecraft Server \(([^)]+)\)");
                if (mSoft.Success)
                {
                    software = mSoft.Groups[1].Value;
                    return;
                }
            }

            if (File.Exists(Path.Combine(serverDir, "purpur.yml"))) software = "Purpur";
            else if (File.Exists(Path.Combine(serverDir, "paper-global.yml")) || File.Exists(Path.Combine(serverDir, "paper.yml"))) software = "Paper";
            else if (File.Exists(Path.Combine(serverDir, "spigot.yml"))) software = "Spigot";
            else if (File.Exists(Path.Combine(serverDir, "fabric-server-launch.jar"))) software = "Fabric";
        }
        catch { }
    }

    public static bool RenameServer(string oldName, string newName, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(newName))
        {
            error = "Server name cannot be empty.";
            return false;
        }

        var cleanNewName = string.Join("_", newName.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrWhiteSpace(cleanNewName))
        {
            error = "Invalid characters in server name.";
            return false;
        }

        var serversDir = DefaultServersDirectory;
        var oldDir = Path.Combine(serversDir, oldName);
        var newDir = Path.Combine(serversDir, cleanNewName);

        if (!Directory.Exists(oldDir))
        {
            error = "Source server directory does not exist.";
            return false;
        }

        if (Directory.Exists(newDir) && !oldDir.Equals(newDir, StringComparison.OrdinalIgnoreCase))
        {
            error = "A server with this name already exists.";
            return false;
        }

        try
        {
            if (!oldDir.Equals(newDir, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(oldDir, newDir);
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static List<ServerNetworkEndpoint> GetLocalNetworkEndpoints(int port = 25565)
    {
        var result = new List<ServerNetworkEndpoint>();
        result.Add(new ServerNetworkEndpoint($"Localhost ({port})", $"localhost:{port}", "Local"));

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            foreach (var ni in interfaces)
            {
                var ipProps = ni.GetIPProperties();
                var unicastIpv4 = ipProps.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork &&
                                !System.Net.IPAddress.IsLoopback(u.Address))
                    .Select(u => u.Address.ToString())
                    .ToList();

                foreach (var ip in unicastIpv4)
                {
                    string typeName;
                    var desc = (ni.Description + " " + ni.Name).ToLowerInvariant();

                    if (desc.Contains("tailscale") || ip.StartsWith("100."))
                        typeName = "Tailscale";
                    else if (desc.Contains("radmin") || ip.StartsWith("26."))
                        typeName = "Radmin VPN";
                    else if (desc.Contains("hamachi"))
                        typeName = "Hamachi";
                    else if (desc.Contains("playit"))
                        typeName = "Playit.gg";
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                        typeName = "Wi-Fi (LAN)";
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                        typeName = "Ethernet (LAN)";
                    else
                        typeName = "LAN";

                    var label = $"{typeName} ({ip}:{port})";
                    result.Add(new ServerNetworkEndpoint(label, $"{ip}:{port}", typeName));
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to scan network interfaces", ex);
        }

        return result;
    }

    public static void SanitizeAndConfigureRunBat(string serverDir, string? javaExePath = null, int? ramMb = null, string? customJvmArgs = null)
    {
        try
        {
            var batPath = Path.Combine(serverDir, "run.bat");
            if (!File.Exists(batPath)) return;
            var text = File.ReadAllText(batPath);

            // 1. Stopping hang fix: ensure bare pause is replaced by errorlevel condition
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"(?m)^\s*pause\s*$",
                "if %ERRORLEVEL% NEQ 0 pause");

            // 2. Update Java executable path if specified
            if (!string.IsNullOrWhiteSpace(javaExePath))
            {
                text = System.Text.RegularExpressions.Regex.Replace(
                    text,
                    @"(^|\r?\n)(?:""[^""]+""|java)(\s+)",
                    $"$1\"{javaExePath}\"$2",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
            }

            // 3. Update RAM in MB if specified
            if (ramMb.HasValue && ramMb.Value > 0)
            {
                var mb = ramMb.Value;
                var minMb = Math.Max(512, mb / 2);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"-Xms\d+[gGmM]", $"-Xms{minMb}M");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"-Xmx\d+[gGmM]", $"-Xmx{mb}M");
            }

            // 4. Update custom JVM arguments if specified
            if (customJvmArgs != null)
            {
                var jarIndex = text.IndexOf("-jar", StringComparison.OrdinalIgnoreCase);
                if (jarIndex > 0)
                {
                    var xmxMatch = System.Text.RegularExpressions.Regex.Match(text, @"-Xmx\d+[gGmM]");
                    if (xmxMatch.Success && xmxMatch.Index < jarIndex)
                    {
                        var before = text[..(xmxMatch.Index + xmxMatch.Length)];
                        var after = text[jarIndex..];
                        var jvmPart = string.IsNullOrWhiteSpace(customJvmArgs) ? " " : $" {customJvmArgs.Trim()} ";
                        text = before + jvmPart + after;
                    }
                }
            }

            File.WriteAllText(batPath, text);
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Failed to configure run.bat: {ex.Message}");
        }
    }

    public static void EnsureRunBatJava(string serverDir, string javaExePath)
    {
        SanitizeAndConfigureRunBat(serverDir, javaExePath: javaExePath);
    }
}
