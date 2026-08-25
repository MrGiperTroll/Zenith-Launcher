using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

/// <summary>
/// Result of a successful update check.
/// </summary>
public sealed record UpdateCheckResult(
    string Version,
    string DownloadUrl,
    string ReleaseNotes,
    bool IsNewerThanCurrent);

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
    Task<bool> DownloadAndApplyAsync(UpdateCheckResult update, CancellationToken ct = default);
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
            using var response = await SharedHttp
                .GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest", ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

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
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameEl) &&
                        asset.TryGetProperty("browser_download_url", out var url))
                    {
                        var name = nameEl.GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                        {
                            assetUrl = url.GetString();
                            break;
                        }
                    }
                }

            return new UpdateCheckResult(
                Version: tagName,
                DownloadUrl: assetUrl ?? $"https://github.com/{Owner}/{Repo}/releases/latest",
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

    public async Task<bool> DownloadAndApplyAsync(UpdateCheckResult update, CancellationToken ct = default)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"ZenithLauncher_Setup_{update.Version}.exe");
            using (var response = await SharedHttp.GetAsync(update.DownloadUrl, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            LauncherLog.Info($"Update downloaded to {tempPath}. Launching installer...");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true
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
