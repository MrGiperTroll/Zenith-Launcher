using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

/// <summary>
/// Multi-source download helper. Tries each candidate mirror in order and returns the first
/// successful (non-empty) response, so a dead, slow or geo-blocked host never breaks the launcher.
/// </summary>
public static class WebFallback
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const int StringAttemptTimeoutMs = 6000;
    private const int BytesAttemptTimeoutMs = 30000;

    public static async Task<string?> GetStringFirstAsync(params string[] urls)
        => await GetStringFirstAsync((IEnumerable<string>)urls).ConfigureAwait(false);

    public static async Task<string?> GetStringFirstAsync(IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url)) continue;
            try
            {
                using var cts = new CancellationTokenSource(StringAttemptTimeoutMs);
                var res = await Http.GetStringAsync(url, cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(res)) return res;
            }
            catch (OperationCanceledException) { LauncherLog.Info($"Mirror {url} timed out - trying next"); }
            catch (Exception ex) { LauncherLog.Error($"Mirror {url} failed - trying next", ex); }
        }
        return null;
    }

    public static async Task<byte[]?> GetBytesFirstAsync(params string[] urls)
        => await GetBytesFirstAsync((IEnumerable<string>)urls).ConfigureAwait(false);

    public static async Task<byte[]?> GetBytesFirstAsync(IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url)) continue;
            try
            {
                using var cts = new CancellationTokenSource(BytesAttemptTimeoutMs);
                var res = await Http.GetByteArrayAsync(url, cts.Token).ConfigureAwait(false);
                if (res.Length > 0) return res;
            }
            catch (OperationCanceledException) { LauncherLog.Info($"Download from {url} timed out - trying next"); }
            catch (Exception ex) { LauncherLog.Error($"Download from {url} failed - trying next", ex); }
        }
        return null;
    }
}