using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZenithLauncher.Services;

/// <summary>
/// Non-blocking, size-bounded file logger. Writes asynchronously in batches to .zenith/launcher.log
/// with automatic rotation (max 512 KB active + .old) so UI threads never block on disk I/O.
/// </summary>
public static class LauncherLog
{
    private const long MaxLogBytes = 512 * 1024;
    private static readonly ConcurrentQueue<string> Pending = new();
    private static readonly AutoResetEvent Signal = new(false);
    private static readonly object FileGate = new();
    private static string? _path;
    private static string? _pathAppDataDir;
    private static int _workerStarted;

    private static string LogFilePath
    {
        get
        {
            var appDataDir = ZenithPaths.AppDataDir;
            if (_path is null || !string.Equals(_pathAppDataDir, appDataDir, StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.CreateDirectory(appDataDir); } catch { }
                _pathAppDataDir = appDataDir;
                _path = System.IO.Path.Combine(appDataDir, "launcher.log");
            }
            return _path;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
          .Append("] ").Append(level).Append("  ").Append(message);
        if (ex != null)
        {
            sb.Append("  ->  ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            sb.Append('\n').Append(ex.StackTrace);
        }
        sb.Append('\n');

        Pending.Enqueue(sb.ToString());
        EnsureWorker();
        Signal.Set();
    }

    public static void Flush()
    {
        DrainBatch();
    }

    private static void EnsureWorker()
    {
        if (Interlocked.Exchange(ref _workerStarted, 1) != 0)
            return;

        AppDomain.CurrentDomain.ProcessExit += (_, _) => DrainBatch();
        Task.Factory.StartNew(WorkerLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static void WorkerLoop()
    {
        while (true)
        {
            Signal.WaitOne(1000);
            DrainBatch();
        }
    }

    private static void DrainBatch()
    {
        if (Pending.IsEmpty) return;

        var batch = new StringBuilder();
        while (Pending.TryDequeue(out var entry))
        {
            batch.Append(entry);
            if (batch.Length >= 65536) break;
        }
        if (batch.Length == 0) return;

        try
        {
            lock (FileGate)
            {
                var path = LogFilePath;
                RotateIfNeeded(path);
                File.AppendAllText(path, batch.ToString());
            }
        }
        catch
        {
            // Logging must never crash the launcher itself.
        }

        if (!Pending.IsEmpty)
            Signal.Set();
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length >= MaxLogBytes)
            {
                var oldPath = path + ".old";
                File.Move(path, oldPath, overwrite: true);
            }
        }
        catch { }
    }
}