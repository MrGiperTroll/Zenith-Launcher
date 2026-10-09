using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

/// <summary>
/// Windows file-operation helpers. Files under a running server or a busy
/// indexer are transiently locked — a blind single attempt turns that into a
/// user-facing error, so mutating ops retry a few times before giving up.
/// </summary>
public static class FileRetry
{
    private const int Attempts = 4;
    private const int DelayMs = 150;

    public static void DeleteFile(string path)
    {
        Run(() => File.Delete(path));
    }

    public static void DeleteDirectory(string path, bool recursive)
    {
        Run(() => Directory.Delete(path, recursive));
    }

    public static void MoveFile(string source, string dest, bool overwrite = true)
    {
        Run(() => File.Move(source, dest, overwrite));
    }

    public static void MoveDirectory(string source, string dest)
    {
        Run(() => Directory.Move(source, dest));
    }

    public static async Task RunAsync(Func<Task> action, CancellationToken ct = default)
    {
        Exception? last = null;
        for (var i = 0; i < Attempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await action().ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsLocked(ex))
            {
                last = ex;
                await Task.Delay(DelayMs, ct).ConfigureAwait(false);
            }
        }
        if (last != null) throw last;
    }

    private static void Run(Action action)
    {
        Exception? last = null;
        for (var i = 0; i < Attempts; i++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (IsLocked(ex))
            {
                last = ex;
                Thread.Sleep(DelayMs);
            }
        }
        if (last != null) throw last;
    }

    private static bool IsLocked(Exception ex) =>
        ex is IOException || ex is UnauthorizedAccessException;
}
