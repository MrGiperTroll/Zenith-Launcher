using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

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
        if (string.IsNullOrWhiteSpace(gameVersion)) return 21;

        var trimmed = gameVersion.Trim();
        // Modern snapshots like 25w... or 26w... use Java 25
        if (trimmed.StartsWith("25w", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("26w", StringComparison.OrdinalIgnoreCase))
        {
            return 25;
        }

        var chars = new System.Collections.Generic.List<char>();
        foreach (var c in trimmed)
        {
            if (char.IsDigit(c) || c == '.') chars.Add(c);
            else if (chars.Count > 0) break;
        }
        var numeric = new string(chars.ToArray()).TrimEnd('.');

        var parts = numeric.Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var first))
            return 21;

        // New versioning scheme: 25.x, 26.x and higher -> Java 25
        if (first >= 25) return 25;

        if (first == 1)
        {
            if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
            {
                if (minor >= 22) return 25;
                if (minor >= 21) return 21;
                if (minor == 20)
                {
                    if (parts.Length >= 3 && int.TryParse(parts[2], out var patch) && patch >= 5)
                        return 21;
                    return 17;
                }
                if (minor >= 17) return 17;
            }
        }
        else if (first >= 2)
        {
            return 25;
        }

        return 8;
    }

    /// <summary>
    /// jvm.cfg is the first file the JVM reads; a home without it is a broken
    /// (e.g. half-extracted) runtime that dies before main(). Catches
    /// interrupted JRE downloads that otherwise look runnable.
    /// </summary>
    public static bool IsValidJavaHome(string? javaHomeDir)
    {
        if (string.IsNullOrWhiteSpace(javaHomeDir) || !Directory.Exists(javaHomeDir))
            return false;
        try
        {
            var hasExe = File.Exists(Path.Combine(javaHomeDir, "bin", "java.exe"))
                || File.Exists(Path.Combine(javaHomeDir, "bin", "javaw.exe"))
                || File.Exists(Path.Combine(javaHomeDir, "bin", "java"))
                || File.Exists(Path.Combine(javaHomeDir, "jre", "bin", "javaw.exe"));
            if (!hasExe) return false;

            return File.Exists(Path.Combine(javaHomeDir, "lib", "jvm.cfg"))
                || File.Exists(Path.Combine(javaHomeDir, "lib", "amd64", "jvm.cfg"))
                || File.Exists(Path.Combine(javaHomeDir, "lib", "i386", "jvm.cfg"))
                || File.Exists(Path.Combine(javaHomeDir, "lib", "rt.jar"))
                || File.Exists(Path.Combine(javaHomeDir, "jre", "lib", "jvm.cfg"))
                || File.Exists(Path.Combine(javaHomeDir, "jre", "lib", "amd64", "jvm.cfg"))
                || File.Exists(Path.Combine(javaHomeDir, "jre", "lib", "rt.jar"));
        }
        catch { return false; }
    }

    /// <summary>
    /// Reads bytecode class major version from a JAR file (e.g. server.jar).
    /// Returns 69 for Java 25, 65 for Java 21, 61 for Java 17, 52 for Java 8.
    /// </summary>
    public static int GetJarClassMajorVersion(string jarPath)
    {
        if (string.IsNullOrWhiteSpace(jarPath) || !File.Exists(jarPath)) return 0;
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            var candidateEntries = zip.Entries.Where(e => e.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase));
            var primary = candidateEntries.FirstOrDefault(e => e.FullName.Contains("Main.class"))
                          ?? candidateEntries.FirstOrDefault();

            if (primary != null)
            {
                using var stream = primary.Open();
                var header = new byte[8];
                int read = stream.Read(header, 0, 8);
                if (read == 8 && header[0] == 0xCA && header[1] == 0xFE && header[2] == 0xBA && header[3] == 0xBE)
                {
                    int major = (header[6] << 8) | header[7];
                    return major;
                }
            }
        }
        catch { }
        return 0;
    }

    /// <summary>
    /// Maps class file major version (e.g. 69, 65, 61, 52) to Java major version (25, 21, 17, 8).
    /// </summary>
    public static int ClassVersionToJavaMajor(int classMajor)
    {
        if (classMajor <= 0) return 21;
        if (classMajor >= 69) return 25;
        if (classMajor >= 45) return classMajor - 44;
        return 8;
    }

    /// <summary>
    /// Detects server java requirement by inspecting server.jar bytecode directly,
    /// falling back to version string inference.
    /// </summary>
    public static string? FindOrResolveServerJava(string serverDir, string? gameVersion, out int requiredJavaMajor)
    {
        requiredJavaMajor = 21;
        var serverJar = Path.Combine(serverDir, "server.jar");
        if (File.Exists(serverJar))
        {
            var classVer = GetJarClassMajorVersion(serverJar);
            if (classVer > 0)
            {
                requiredJavaMajor = ClassVersionToJavaMajor(classVer);
            }
        }

        if (requiredJavaMajor == 21 && !string.IsNullOrWhiteSpace(gameVersion))
        {
            requiredJavaMajor = InferRequiredJavaMajor(gameVersion);
        }

        return FindJavaForVersion(requiredJavaMajor, preferConsole: true);
    }

    public static string AdoptiumDownloadUrl(int major) =>
        $"https://adoptium.net/temurin/releases/?version={major}";

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
                // Java 8 is reported as 1.8.0_xxx where ProductMajorPart is 1 and ProductMinorPart is 8
                if (vi.ProductMajorPart == 1 && vi.ProductMinorPart == 8) return 8;
                if (vi.FileMajorPart == 1 && vi.FileMinorPart == 8) return 8;
                if (vi.ProductMajorPart > 0) return vi.ProductMajorPart;
                if (vi.FileMajorPart > 0) return vi.FileMajorPart;

                if (!string.IsNullOrWhiteSpace(vi.ProductVersion))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(vi.ProductVersion, @"^(?:1\.)?(\d+)");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var parsed))
                        return parsed;
                }
            }
        }
        catch { }
        return 0;
    }

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
    /// Resolves concrete javaw.exe / java.exe path matching a specific Java major version.
    /// Checks local .zenith/runtime, JAVA_HOME, Program Files JDKs (Adoptium, Oracle, Corretto, Zulu, etc.).
    /// </summary>
    public static string? FindJavaForVersion(int major, bool preferConsole = false)
    {
        var primaryExe = preferConsole ? "java.exe" : "javaw.exe";
        var secondaryExe = preferConsole ? "javaw.exe" : "java.exe";

        // 1. Check <AppDataDir>/runtime/
        try
        {
            var javaDir = Path.Combine(ZenithPaths.AppDataDir, "runtime");
            if (Directory.Exists(javaDir))
            {
                var patterns = major == 8
                    ? new[] { "jdk-8*", "jdk8u*", "*jdk*8*", "*jre*8*", "*8u*" }
                    : new[] { $"jdk-{major}*", $"jdk{major}*", $"*jdk*{major}*" };

                foreach (var pattern in patterns)
                {
                    foreach (var dir in Directory.GetDirectories(javaDir, pattern, SearchOption.TopDirectoryOnly))
                    {
                        if (!IsValidJavaHome(dir)) continue;
                        var p1 = Path.Combine(dir, "bin", primaryExe);
                        if (File.Exists(p1) && GetJavaMajor(p1) == major) return p1;
                        var p2 = Path.Combine(dir, "bin", secondaryExe);
                        if (File.Exists(p2) && GetJavaMajor(p2) == major) return p2;
                    }
                }

                // Fallback scan across all runtime subdirectories
                foreach (var dir in Directory.GetDirectories(javaDir, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!IsValidJavaHome(dir)) continue;
                    var p1 = Path.Combine(dir, "bin", primaryExe);
                    if (File.Exists(p1) && GetJavaMajor(p1) == major) return p1;
                    var p2 = Path.Combine(dir, "bin", secondaryExe);
                    if (File.Exists(p2) && GetJavaMajor(p2) == major) return p2;
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
                var jh1 = Path.Combine(javaHome, "bin", primaryExe);
                if (File.Exists(jh1) && GetJavaMajor(jh1) == major) return jh1;
                var jh2 = Path.Combine(javaHome, "bin", secondaryExe);
                if (File.Exists(jh2) && GetJavaMajor(jh2) == major) return jh2;
            }
        }
        catch { }

        // 3. Check installed JDKs in Program Files & User Profile (.jdks)
        try
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var vendorBases = new[]
            {
                Path.Combine(programFiles, "Eclipse Adoptium"),
                Path.Combine(programFiles, "Java"),
                Path.Combine(programFiles, "Amazon Corretto"),
                Path.Combine(programFiles, "Zulu"),
                Path.Combine(programFiles, "BellSoft"),
                Path.Combine(programFiles, "Microsoft"),
                Path.Combine(programFiles, "Semeru"),
                Path.Combine(programFilesX86, "Eclipse Adoptium"),
                Path.Combine(programFilesX86, "Java"),
                Path.Combine(userProfile, ".jdks")
            };

            var patterns = major == 8
                ? new[] { "jdk-8*", "jdk8u*", "*jdk*8*", "*jre*8*", "*8u*" }
                : new[] { $"jdk-{major}*", $"jdk{major}*", $"*jdk*{major}*" };

            foreach (var vBase in vendorBases)
            {
                if (!Directory.Exists(vBase)) continue;
                foreach (var pattern in patterns)
                {
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(vBase, pattern, SearchOption.TopDirectoryOnly))
                        {
                            var p1 = Path.Combine(dir, "bin", primaryExe);
                            if (File.Exists(p1) && GetJavaMajor(p1) == major) return p1;
                            var p2 = Path.Combine(dir, "bin", secondaryExe);
                            if (File.Exists(p2) && GetJavaMajor(p2) == major) return p2;
                        }
                    }
                    catch { }
                }

                // Fallback scan across all subdirectories of this vendor
                try
                {
                    foreach (var dir in Directory.GetDirectories(vBase, "*", SearchOption.TopDirectoryOnly))
                    {
                        var p1 = Path.Combine(dir, "bin", primaryExe);
                        if (File.Exists(p1) && GetJavaMajor(p1) == major) return p1;
                        var p2 = Path.Combine(dir, "bin", secondaryExe);
                        if (File.Exists(p2) && GetJavaMajor(p2) == major) return p2;
                    }
                }
                catch { }
            }
        }
        catch { }

        // 4. Check Windows Registry (Adoptium, Oracle, Corretto, Zulu)
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var regSubKeys = new[]
                {
                    $@"SOFTWARE\JavaSoft\JDK\{major}",
                    $@"SOFTWARE\JavaSoft\Java Development Kit\{major}",
                    $@"SOFTWARE\JavaSoft\JRE\{major}",
                    $@"SOFTWARE\JavaSoft\Java Runtime Environment\{major}",
                    $@"SOFTWARE\Eclipse Adoptium\JDK\{major}",
                    $@"SOFTWARE\Eclipse Adoptium\JRE\{major}",
                    $@"SOFTWARE\AdoptOpenJDK\JDK\{major}",
                    $@"SOFTWARE\Microsoft\JDK\{major}"
                };

                foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
                {
                    using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view);
                    foreach (var sub in regSubKeys)
                    {
                        using var key = baseKey.OpenSubKey(sub);
                        if (key != null)
                        {
                            var home = key.GetValue("JavaHome") as string ?? key.GetValue("Path") as string;
                            if (!string.IsNullOrWhiteSpace(home))
                            {
                                var p1 = Path.Combine(home, "bin", primaryExe);
                                if (File.Exists(p1) && GetJavaMajor(p1) == major) return p1;
                                var p2 = Path.Combine(home, "bin", secondaryExe);
                                if (File.Exists(p2) && GetJavaMajor(p2) == major) return p2;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // 5. Fall back to system java path only if major version matches
        var sys = FindSystemJavaPath();
        if (!string.IsNullOrEmpty(sys) && GetJavaMajor(sys) == major)
        {
            if (preferConsole && sys.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase))
            {
                var consoleSys = Path.Combine(Path.GetDirectoryName(sys)!, "java.exe");
                if (File.Exists(consoleSys)) return consoleSys;
            }
            return sys;
        }

        return null;
    }
}