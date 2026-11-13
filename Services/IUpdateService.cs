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
            var channel = UpdateChannelService.GetCurrentChannel();
            if (channel == UpdateChannel.Beta)
            {
                return await CheckBetaUpdatesAsync(ct).ConfigureAwait(false);
            }

            return await CheckStableUpdatesAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LauncherLog.Error("Update check failed.", ex);
            return null;
        }
    }

    private async Task<UpdateCheckResult?> CheckStableUpdatesAsync(CancellationToken ct)
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
        var isNewer = CompareVersions(tagName!, current) > 0;

        string? assetUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
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
        }

        var htmlUrl = root.TryGetProperty("html_url", out var htmlEl) ? htmlEl.GetString() : null;

        return new UpdateCheckResult(
            Version: tagName,
            DownloadUrl: assetUrl ?? htmlUrl ?? $"https://github.com/{Owner}/{Repo}/releases/latest",
            ReleaseNotes: root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            IsNewerThanCurrent: isNewer);
    }

    private async Task<UpdateCheckResult?> CheckBetaUpdatesAsync(CancellationToken ct)
    {
        using var response = await SharedHttp
            .GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases", ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        // Find the latest release with prerelease == true and draft == false
        JsonElement? latestPreRelease = null;
        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            var isDraft = rel.TryGetProperty("draft", out var draftProp) && draftProp.GetBoolean();
            if (isDraft) continue;

            var isPre = rel.TryGetProperty("prerelease", out var preProp) && preProp.GetBoolean();
            if (isPre)
            {
                latestPreRelease = rel;
                break;
            }
        }

        if (latestPreRelease == null)
            return null;

        var release = latestPreRelease.Value;
        var tagName = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
        if (string.IsNullOrWhiteSpace(tagName))
            return null;

        var current = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        var isNewer = CompareVersions(tagName!, current) > 0;

        string? assetUrl = SelectBetaAssetUrl(release);
        var htmlUrl = release.TryGetProperty("html_url", out var htmlEl) ? htmlEl.GetString() : null;

        return new UpdateCheckResult(
            Version: tagName,
            DownloadUrl: assetUrl ?? htmlUrl ?? $"https://github.com/{Owner}/{Repo}/releases",
            ReleaseNotes: release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            IsNewerThanCurrent: isNewer);
    }

    /// <summary>
    /// Selects the best asset for beta updates, preferring portable .exe or .zip archives.
    /// Priority:
    /// 1. File containing "portable" and ending with .exe or .zip
    /// 2. File ending with .zip
    /// 3. Standalone .exe (not containing "setup")
    /// 4. Any .exe
    /// </summary>
    private static string? SelectBetaAssetUrl(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        string? portableZipOrExe = null;
        string? anyZip = null;
        string? standaloneExe = null;
        string? anyExe = null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameEl) ||
                !asset.TryGetProperty("browser_download_url", out var urlEl))
                continue;

            var name = nameEl.GetString() ?? "";
            var url = urlEl.GetString();
            if (string.IsNullOrWhiteSpace(url)) continue;

            var isExe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            var isZip = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            var isPortable = name.Contains("portable", StringComparison.OrdinalIgnoreCase);
            var isSetup = name.Contains("setup", StringComparison.OrdinalIgnoreCase);

            if (isPortable && (isExe || isZip) && portableZipOrExe == null)
            {
                portableZipOrExe = url;
            }

            if (isZip && anyZip == null)
            {
                anyZip = url;
            }

            if (isExe && !isSetup && standaloneExe == null)
            {
                standaloneExe = url;
            }

            if (isExe && anyExe == null)
            {
                anyExe = url;
            }
        }

        return portableZipOrExe ?? anyZip ?? standaloneExe ?? anyExe;
    }

    public async Task<bool> DownloadAndApplyAsync(UpdateCheckResult update, CancellationToken ct = default, IProgress<DownloadProgress>? progress = null)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"ZenithLauncher_Setup_{update.Version}.exe");

            // Use a dedicated HttpClient with no timeout for large downloads
            using var dlHttp = new HttpClient();
            dlHttp.DefaultRequestHeaders.UserAgent.ParseAdd("ZenithLauncher");

            using (var response = await dlHttp.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1L;
                await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
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

            LauncherLog.Info($"Update downloaded to {tempPath}. Starting silent install...");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
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

    /// <summary>
    /// Semantic version comparison supporting pre-release tags (e.g. 1.0.10-beta.1 > 1.0.9).
    /// </summary>
    public static int CompareVersions(string left, string right)
    {
        static (int[] numbers, string prerelease) Parse(string raw)
        {
            var str = (raw ?? "").Trim();
            var start = 0;
            while (start < str.Length && !char.IsDigit(str[start])) start++;
            if (start > 0) str = str.Substring(start);

            var dashIdx = str.IndexOf('-');
            var core = dashIdx >= 0 ? str[..dashIdx] : str;
            var pre = dashIdx >= 0 ? str[(dashIdx + 1)..] : "";

            var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var nums = new int[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                int.TryParse(parts[i], out nums[i]);
            }
            return (nums, pre);
        }

        var (lNums, lPre) = Parse(left);
        var (rNums, rPre) = Parse(right);

        var maxLen = Math.Max(lNums.Length, rNums.Length);
        for (var i = 0; i < maxLen; i++)
        {
            var l = i < lNums.Length ? lNums[i] : 0;
            var r = i < rNums.Length ? rNums[i] : 0;
            if (l != r) return l.CompareTo(r);
        }

        // Core numbers are equal. By SemVer 2.0:
        // A version without prerelease is higher than one with prerelease (e.g. 1.0.10 > 1.0.10-beta)
        if (string.IsNullOrEmpty(lPre) && !string.IsNullOrEmpty(rPre)) return 1;
        if (!string.IsNullOrEmpty(lPre) && string.IsNullOrEmpty(rPre)) return -1;

        if (!string.IsNullOrEmpty(lPre) && !string.IsNullOrEmpty(rPre))
        {
            var lPreParts = lPre.Split('.');
            var rPreParts = rPre.Split('.');
            var preMax = Math.Max(lPreParts.Length, rPreParts.Length);
            for (var i = 0; i < preMax; i++)
            {
                var lp = i < lPreParts.Length ? lPreParts[i] : "";
                var rp = i < rPreParts.Length ? rPreParts[i] : "";
                var lIsNum = int.TryParse(lp, out var ln);
                var rIsNum = int.TryParse(rp, out var rn);
                if (lIsNum && rIsNum)
                {
                    if (ln != rn) return ln.CompareTo(rn);
                }
                else
                {
                    var cmp = string.Compare(lp, rp, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp;
                }
            }
        }

        return 0;
    }
}
