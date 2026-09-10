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

        var major = InferRequiredJavaMajor(version);
        return $"(Java {major})";
    }

    public static int InferRequiredJavaMajor(string gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion)) return 8;

        var chars = new System.Collections.Generic.List<char>();
        foreach (var c in gameVersion)
        {
            if (char.IsDigit(c) || c == '.') chars.Add(c);
            else if (chars.Count > 0) break;
        }
        var numeric = new string(chars.ToArray()).TrimEnd('.');

        var parts = numeric.Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var first))
            return 8;

        if (first >= 26) return 25;
        if (first != 1) return 8;

        if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
        {
            if (minor >= 21) return 21;
            if (minor == 20)
            {
                if (parts.Length >= 3 && int.TryParse(parts[2], out var patch) && patch >= 5)
                    return 21;
                return 17;
            }
            if (minor >= 17) return 17;
        }
        return 8;
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