using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

public record ServerSoftwareOption(string Id, string DisplayName);

public class ServerCreatorService
{
    private const string MojangManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static string DefaultServersDirectory => Path.Combine(ZenithPaths.AppDataDir, "servers");

    public static IReadOnlyList<ServerSoftwareOption> AvailableSoftware { get; } = new List<ServerSoftwareOption>
    {
        new("paper", "Paper (High Performance & Plugins)"),
        new("vanilla", "Vanilla (Official Mojang Server)"),
        new("fabric", "Fabric (Mods & Lightweight)")
    };

    public async Task<List<string>> GetPopularReleaseVersionsAsync(CancellationToken ct = default)
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
                    if (type == "release" && !string.IsNullOrWhiteSpace(id))
                    {
                        versions.Add(id);
                    }
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
        int ramGb,
        int port,
        bool agreeEula,
        bool onlineMode,
        IProgress<(string Status, double Percent)>? progress = null,
        CancellationToken ct = default)
    {
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
            case "fabric":
                await DownloadFabricServerAsync(version, serverJarPath, progress, ct);
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

        // 3. run.bat (Windows batch file)
        var minRam = Math.Max(1, ramGb / 2);
        var maxRam = Math.Max(1, ramGb);
        var runBatPath = Path.Combine(targetDir, "run.bat");
        var runBat =
            "@echo off\r\n" +
            $"title Minecraft Server - {serverName}\r\n" +
            $"echo Starting Minecraft Server ({version} - {software})...\r\n" +
            $"java -Xms{minRam}G -Xmx{maxRam}G -jar server.jar nogui\r\n" +
            "pause\r\n";
        await File.WriteAllTextAsync(runBatPath, runBat, ct);

        // 4. run.sh (Linux / macOS shell script)
        var runShPath = Path.Combine(targetDir, "run.sh");
        var runSh =
            "#!/bin/bash\n" +
            $"echo \"Starting Minecraft Server ({version} - {software})...\"\n" +
            $"java -Xms{minRam}G -Xmx{maxRam}G -jar server.jar nogui\n";
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
}
