using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZenithLauncher.Services;

/// <summary>
/// Crash-safe atomic file writer using a unique temporary sibling file followed
/// by <see cref="File.Move(string, string, bool)"/> replacement.
/// </summary>
public static class AtomicFileWriter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string targetPath, string content, bool flushToDisk = false)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Target path cannot be empty.", nameof(targetPath));

        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmpPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(fs, Utf8NoBom);
                writer.Write(content);
                writer.Flush();
                if (flushToDisk)
                    fs.Flush(flushToDisk: true);
            }

            FileRetry.MoveFile(tmpPath, targetPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemp(tmpPath);
            throw;
        }
    }

    public static async Task WriteAllTextAsync(
        string targetPath,
        string content,
        bool flushToDisk = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Target path cannot be empty.", nameof(targetPath));

        cancellationToken.ThrowIfCancellationRequested();

        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmpPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var fs = new FileStream(
                tmpPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16384,
                useAsync: true))
            {
                await using var writer = new StreamWriter(fs, Utf8NoBom, bufferSize: 16384, leaveOpen: true);
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (flushToDisk)
                    fs.Flush(flushToDisk: true);
            }

            await FileRetry.RunAsync(() =>
            {
                File.Move(tmpPath, targetPath, overwrite: true);
                return Task.CompletedTask;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            TryDeleteTemp(tmpPath);
            throw;
        }
    }

    private static void TryDeleteTemp(string tmpPath)
    {
        try
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
        }
        catch
        {
            // Best-effort cleanup of temp artifact.
        }
    }
}
