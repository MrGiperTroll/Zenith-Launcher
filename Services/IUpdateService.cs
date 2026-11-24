using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZenithLauncher.Services;

/// <summary>
/// Result of a successful update check.
/// </summary>
public sealed record UpdateCheckResult(
    string Version,
    string DownloadUrl,
    string ReleaseNotes,
    bool IsNewerThanCurrent);

/// <summary>Download progress snapshot (percent is 0-100).</summary>
public sealed record DownloadProgress(
    double Percent,
    long DownloadedBytes,
    long TotalBytes);

/// <summary>
/// Update pipeline contract. The download/apply stage is intentionally
/// reserved: the launcher ships without an installer yet, so nothing calls
/// these members from the startup pipeline. Wire CheckForUpdatesAsync into
/// the UI once a distribution channel exists.
/// </summary>
public interface IUpdateService
{
    /// <summary>Latest available version, or null when up-to-date / disabled / offline.</summary>
    Task<UpdateCheckResult?> CheckForUpdatesAsync(CancellationToken ct = default);

    /// <summary>Reserved for the future auto-update flow (not activated).</summary>
    Task<bool> DownloadAndApplyAsync(UpdateCheckResult update, CancellationToken ct = default, IProgress<DownloadProgress>? progress = null);
}

/// <summary>
/// GitHub Releases implementation of <see cref="IUpdateService"/>.
/// Disabled while Owner/Repo are empty - fill them in to activate checks.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string Owner = "MrGiperTroll";
    private const string Repo = "Zenith-Launcher";

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    static GitHubUpdateService()
    {
        SharedHttp.DefaultRequestHeaders.UserAgent.ParseAdd("ZenithLauncher");
        SharedHttp.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateCheckResult?> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("ZenithLauncher");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            req.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

            using var response = await SharedHttp.SendAsync(req, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LauncherLog.Warn($"Update check returned HTTP {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            if (string.IsNullOrWhiteSpace(tagName))
                return null;

            var current = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
            var isNewer = CompareVersions(tagName!.TrimStart('v', 'V'), current) > 0;

            string? assetUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                // Priority 1: .exe asset with Setup or Installer in name
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameEl) &&
                        asset.TryGetProperty("browser_download_url", out var url))
                    {
                        var name = nameEl.GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("Installer", StringComparison.OrdinalIgnoreCase)))
                        {
                            assetUrl = url.GetString();
                            break;
                        }
                    }
                }

                // Priority 2: any .exe asset
                if (assetUrl == null)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameEl) &&
                            asset.TryGetProperty("browser_download_url", out var url))
                        {
                            var name = nameEl.GetString() ?? "";
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                assetUrl = url.GetString();
                                break;
                            }
                        }
                    }
                }
            }

            var fallbackUrl = $"https://github.com/{Owner}/{Repo}/releases/download/{tagName}/ZenithLauncher_Setup_{tagName}.exe";

            return new UpdateCheckResult(
                Version: tagName,
                DownloadUrl: assetUrl ?? fallbackUrl,
                ReleaseNotes: root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                IsNewerThanCurrent: isNewer);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LauncherLog.Error("Update check failed.", ex);
            return null;
        }
    }

    public async Task<bool> DownloadAndApplyAsync(UpdateCheckResult update, CancellationToken ct = default, IProgress<DownloadProgress>? progress = null)
    {
        try
        {
            var cleanVersion = update.Version.Trim().TrimStart('v', 'V');
            var tempFile = Path.Combine(Path.GetTempPath(), $"Zenith_Setup_{cleanVersion}_{Guid.NewGuid():N}.exe");

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };

            using var dlHttp = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            dlHttp.DefaultRequestHeaders.UserAgent.ParseAdd("ZenithLauncher");
            dlHttp.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

            using (var response = await dlHttp.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1L;
                await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                {
                    var buffer = new byte[81920];
                    long downloaded = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        downloaded += read;
                        if (progress != null && total > 0)
                            progress.Report(new DownloadProgress(100.0 * downloaded / total, downloaded, total));
                    }
                    if (progress != null)
                        progress.Report(new DownloadProgress(100, downloaded, total > 0 ? total : downloaded));
                }
            }

            if (!File.Exists(tempFile) || new FileInfo(tempFile).Length == 0)
            {
                LauncherLog.Error("Downloaded setup file is missing or empty.");
                return false;
            }

            LauncherLog.Info($"Update downloaded successfully to {tempFile}. Preparing silent installer runner...");

            var currentPid = Environment.ProcessId;
            var appDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var exeName = Path.GetFileName(Environment.ProcessPath ?? "Zenith Launcher.exe");
            var exePath = Path.Combine(appDir, exeName);
            if (!File.Exists(exePath))
            {
                exePath = Path.Combine(appDir, "Zenith Launcher.exe");
            }

            var helperScript = Path.Combine(Path.GetTempPath(), $"zenith_updater_{Guid.NewGuid():N}.cmd");
            var scriptContent = $@"@echo off
timeout /t 1 /nobreak >nul
:wait_proc
tasklist /fi ""PID eq {currentPid}"" 2>nul | find ""{currentPid}"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto wait_proc
)
start """" /wait ""{tempFile}"" /VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /DIR=""{appDir}""
timeout /t 1 /nobreak >nul
start """" ""{exePath}""
timeout /t 2 /nobreak >nul
del /f /q ""{tempFile}"" 2>nul
del /f /q ""%~f0"" 2>nul
";

            await File.WriteAllTextAsync(helperScript, scriptContent, ct).ConfigureAwait(false);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{helperScript}\"\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            System.Diagnostics.Process.Start(psi);

            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LauncherLog.Error("Update download/launch failed.", ex);
            return false;
        }
    }

    /// <summary>Semantic-ish comparison: "1.10" > "1.9".</summary>
    private static int CompareVersions(string left, string right)
    {
        var lParts = left.Split('.');
        var rParts = right.Split('.');
        for (var i = 0; i < Math.Max(lParts.Length, rParts.Length); i++)
        {
            int.TryParse(i < lParts.Length ? lParts[i] : "0", out var l);
            int.TryParse(i < rParts.Length ? rParts[i] : "0", out var r);
            if (l != r) return l.CompareTo(r);
        }
        return 0;
    }
}
