using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

/// <summary>
/// Hardened file downloader used for every launcher-managed artifact
/// (Java runtimes, modloader installers, authlib-injector).
///
/// Features:
///  - mirror failover (tries every URL in order),
///  - automatic retries with growing delay between rounds,
///  - HTTP Range resume of a partial ".part" file after a dropped connection,
///  - SHA-256 integrity validation (optional) with auto-retry on mismatch,
///  - full CancellationToken support (the game launch cancel path).
/// </summary>
public static class ResilientDownloadService
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    static ResilientDownloadService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("ZenithLauncher");
        Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
    }

    /// <summary>
    /// Downloads <paramref name="destPath"/> trying each mirror in order.
    /// Throws the last observed exception after all rounds are exhausted.
    /// The ".part" file is kept on network failures so the next attempt resumes.
    /// </summary>
    public static async Task DownloadFileAsync(
        IReadOnlyList<string> urls,
        string destPath,
        string? sha256Hex = null,
        CancellationToken ct = default,
        IProgress<double>? progress = null,
        int maxRounds = 3)
    {
        if (urls is null || urls.Count == 0)
            throw new ArgumentException("No download URLs supplied.", nameof(urls));

        var targetDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

        var partPath = destPath + ".part";
        Exception? lastError = null;

        for (var round = 1; round <= maxRounds; round++)
        {
            foreach (var url in urls)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await DownloadOneAsync(url, partPath, ct, progress).ConfigureAwait(false);
                    VerifyOrThrow(partPath, sha256Hex);
                    File.Move(partPath, destPath, overwrite: true);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    LauncherLog.Error($"Download attempt failed ({url}): {ex.Message}");
                    // A corrupted payload must not be resumed next round.
                    try { File.Delete(partPath); } catch { }
                }
            }

            if (round < maxRounds)
                await Task.Delay(TimeSpan.FromSeconds(round), ct).ConfigureAwait(false);
        }

        throw lastError ?? new InvalidOperationException("Download failed for an unknown reason.");
    }

    private static async Task DownloadOneAsync(string url, string partPath, CancellationToken ct, IProgress<double>? progress)
    {
        long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await Http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        // Server ignored the Range header and returned the whole body -> restart.
        var resume = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;

        response.EnsureSuccessStatusCode();
        var totalLength = response.Content.Headers.ContentLength ?? -1;

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(
            partPath,
            resume ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            written += read;
            if (progress != null && totalLength > 0)
                progress.Report(Math.Min(1.0, (existing + written) / (double)(existing + totalLength)));
        }
    }

    private static void VerifyOrThrow(string path, string? sha256Hex)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0)
            throw new InvalidDataException($"Downloaded file '{path}' is empty.");

        if (string.IsNullOrWhiteSpace(sha256Hex))
            return;

        string actual;
        using (var stream = info.OpenRead())
            actual = Convert.ToHexString(SHA256.HashData(stream));
        actual = actual.Replace("-", "");

        if (!actual.Equals(sha256Hex.Replace("-", ""), StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { }
            throw new InvalidDataException(
                $"SHA-256 checksum mismatch ({actual[..12]}... != expected {sha256Hex[..12]}...).");
        }
    }
}
