using System;
using System.IO;

namespace CustomMcLauncher.Services;

/// <summary>
/// Shared helpers for showing which Java build is used for a given Minecraft version
/// in "Recommended" mode. Mirrors the logic previously embedded in MainWindowViewModel.
/// </summary>
public static class JavaVersionHelper
{
    public static string RecommendedDisplay(string version)
    {
        if (string.IsNullOrEmpty(version))
            return L10n.T("java_display_auto");

        if (version.StartsWith("1.20.5", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.20.6", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.21", StringComparison.OrdinalIgnoreCase))
            return "(Java 21)";

        if (version.StartsWith("1.7", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.8", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.12", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.16", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.17", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.18", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.19", StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith("1.20", StringComparison.OrdinalIgnoreCase))
            return "(Java 17)";

        return "(Java 25)";
    }

    public static string CustomDisplay(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return L10n.T("java_display_notset");
        if (File.Exists(path))
            return Path.GetFileName(path);
        return L10n.T("java_display_notfound");
    }

    /// <summary>
    /// Locates the system Java the way a plain "System" launch would:
    /// JAVA_HOME first, then a PATH scan. Returns null when nothing is found.
    /// Used by the settings UI to always show a concrete javaw.exe path.
    /// </summary>
    public static string? FindSystemJavaPath()
    {
        foreach (var exe in new[] { "javaw.exe", "java.exe" })
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrWhiteSpace(javaHome))
            {
                var p = Path.Combine(javaHome, "bin", exe);
                if (File.Exists(p)) return p;
            }

            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var rawDir in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var dir = rawDir.Trim().Trim('"');
                    if (dir.Length == 0) continue;
                    var p = Path.Combine(dir, exe);
                    if (File.Exists(p)) return p;
                }
                catch { /* ignore malformed PATH entries */ }
            }
        }
        return null;
    }
}