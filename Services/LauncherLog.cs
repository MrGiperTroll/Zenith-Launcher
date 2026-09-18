using System;
using System.IO;
using System.Text;

namespace CustomMcLauncher.Services;

/// <summary>
/// Minimal thread-safe file logger. Writes to .zenith/launcher.log so runtime errors
/// are never swallowed silently — even on machines where the user can't find the log.
/// </summary>
public static class LauncherLog
{
    private static readonly object Gate = new();
    private static string? _path;

    private static string Path
    {
        get
        {
            if (_path is null)
            {
                try { Directory.CreateDirectory(ZenithPaths.AppDataDir); } catch { }
                _path = System.IO.Path.Combine(ZenithPaths.AppDataDir, "launcher.log");
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

        try
        {
            lock (Gate)
            {
                File.AppendAllText(Path, sb.Append('\n').ToString());
            }
        }
        catch
        {
            // Logging must never crash the launcher itself.
        }
    }
}