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

    public static int GetJavaMajor(string exePath)
    {
        try
        {
            if (File.Exists(exePath))
            {
                var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                if (vi.ProductMajorPart > 0) return vi.ProductMajorPart;
                if (vi.FileMajorPart > 0) return vi.FileMajorPart;
            }
        }
        catch { }
        return 17;
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

    /// <summary>
    /// Resolves concrete javaw.exe path matching a specific Java major version.
    /// Checks local .zenith/runtime, JAVA_HOME, Program Files JDKs, and fallback system Java.
    /// </summary>
    public static string? FindJavaForVersion(int major)
    {
        // 1. Check <AppDataDir>/runtime/
        try
        {
            var javaDir = Path.Combine(ZenithPaths.AppDataDir, "runtime");
            if (Directory.Exists(javaDir))
            {
                var patterns = new[] { $"jdk-{major}*", $"jdk{major}*", $"*jdk*{major}*" };
                foreach (var pattern in patterns)
                {
                    foreach (var dir in Directory.GetDirectories(javaDir, pattern, SearchOption.TopDirectoryOnly))
                    {
                        var javaw = Path.Combine(dir, "bin", "javaw.exe");
                        if (File.Exists(javaw)) return javaw;
                        var javaExe = Path.Combine(dir, "bin", "java.exe");
                        if (File.Exists(javaExe)) return javaExe;
                    }
                }
            }
        }
        catch { }

        // 2. Check JAVA_HOME if version matches
        try
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                var jh = Path.Combine(javaHome, "bin", "javaw.exe");
                if (File.Exists(jh) && GetJavaMajor(jh) == major) return jh;
                jh = Path.Combine(javaHome, "bin", "java.exe");
                if (File.Exists(jh) && GetJavaMajor(jh) == major) return jh;
            }
        }
        catch { }

        // 3. Check installed JDKs in Program Files
        try
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var candidates = new[]
            {
                Path.Combine(programFiles, "Eclipse Adoptium", $"jdk-{major}", "bin", "javaw.exe"),
                Path.Combine(programFiles, "Eclipse Adoptium", $"jdk-{major}", "bin", "java.exe"),
                Path.Combine(programFiles, "Java", $"jdk-{major}", "bin", "javaw.exe"),
                Path.Combine(programFiles, "Java", $"jdk-{major}", "bin", "java.exe"),
                Path.Combine(programFiles, "Amazon Corretto", $"jdk{major}.0", "bin", "javaw.exe"),
                Path.Combine(programFiles, "Zulu", $"zulu-{major}", "bin", "javaw.exe"),
                Path.Combine(programFilesX86, "Java", $"jdk-{major}", "bin", "javaw.exe")
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }
        }
        catch { }

        // 4. Fall back to system java path
        var sys = FindSystemJavaPath();
        if (!string.IsNullOrEmpty(sys))
        {
            return sys;
        }

        return null;
    }
}