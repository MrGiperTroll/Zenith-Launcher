using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CustomMcLauncher.Services;

/// <summary>Result of a crash analysis pass (localized for display).</summary>
public sealed record CrashAnalysis(
    string Title,
    string HumanSummary,
    string LikelyCause,
    IReadOnlyList<string> Evidence,
    string LogPath,
    int ExitCode,
    IReadOnlyList<string>? Requirements = null);

/// <summary>
/// Log-based crash classifier. Reads the game log tail (and the newest
/// crash-report, if any) and maps distinctive lines to a cause, reusing the
/// localized crash_* strings the report window already displays.
/// Benign lines (debug crashes, WARN/INFO noise, thread dumps) never count
/// as evidence on their own.
/// </summary>
public static class CrashAnalyzer
{
    private const int MaxLogBytes = 300 * 1024;
    private const int MaxEvidence = 6;
    private const int MaxEvidenceLine = 220;

    public static CrashAnalysis Analyze(int exitCode, string instanceLogPath, string instanceBasePath)
    {
        var evidence = new List<string>();
        var requirements = new List<string>();
        var logText = ReadLogTail(instanceLogPath, instanceBasePath);

        string title = L10n.T("crash_generic_title");
        string human = L10n.T("crash_generic_human");
        string cause = L10n.T("crash_generic_cause");

        if (!string.IsNullOrEmpty(logText))
        {
            // 1. Manually triggered debug crash (Shift+F3+C) is not a problem.
            if (ContainsAny(logText, "Manually triggered debug crash"))
            {
                cause = "Manual debug crash (Shift+F3+C)";
                evidence.AddRange(PickLines(logText, "Manually triggered debug crash", 2));
                return Build(title, human, cause, evidence, requirements, instanceLogPath, exitCode);
            }

            // 2. Out of memory.
            var oom = PickLines(logText, "OutOfMemoryError|Out of memory|Failed to allocate|Memory allocation failed", 3);
            if (oom.Count > 0)
            {
                return Build(L10n.T("crash_mem_title"), L10n.T("crash_mem_human"),
                    "Java heap exhausted", oom, requirements, instanceLogPath, exitCode);
            }

            // 3. Wrong Java (class file version -> required major).
            var cfv = Regex.Match(logText, @"class file version\s+(\d+)", RegexOptions.IgnoreCase);
            if (!cfv.Success)
                cfv = Regex.Match(logText, @"UnsupportedClassVersionError[^\d]*(\d+)", RegexOptions.IgnoreCase);
            if (cfv.Success && int.TryParse(cfv.Groups[1].Value, out var classMajor))
            {
                var needJava = classMajor >= 69 ? 25 : classMajor >= 65 ? 21 : classMajor >= 61 ? 17 : classMajor >= 55 ? 11 : 8;
                requirements.Add($"Requires Java {needJava} (class file {classMajor})");
                return Build(L10n.T("crash_java_title"), L10n.T("crash_java_human"),
                    $"Game needs Java {needJava}",
                    PickLines(logText, "UnsupportedClassVersionError|class file version|has been compiled by a more recent version", 3),
                    requirements, instanceLogPath, exitCode);
            }

            // 4. Missing mod dependencies.
            var depLines = PickLines(logText, "Missing.*dependenc|requires .* of |needs .* to be|but it is missing", 4);
            var modNames = ExtractModJars(logText);
            if (depLines.Count > 0)
            {
                foreach (var req in ParseRequirements(logText).Take(5))
                    requirements.Add(req);
                if (modNames.Count > 0)
                    evidence.Add(string.Format(L10n.T("crash_mods_names"), string.Join(", ", modNames.Take(5))));
                evidence.AddRange(depLines);
                return Build(L10n.T("crash_mods_title"), L10n.T("crash_mods_human"),
                    "Missing or incompatible mod dependency", evidence, requirements, instanceLogPath, exitCode);
            }

            // 5. Mod conflict / broken mod code.
            var conflict = PickLines(logText, "NoClassDefFoundError|NoSuchMethodError|Mixin apply failed|MixinPrepareError|Duplicate Questo|is already registered|mod .* (crashed|failed)|Caused by:.*(fabric|forge|mixin)", 4);
            if (conflict.Count > 0)
            {
                if (modNames.Count > 0)
                    evidence.Add(string.Format(L10n.T("crash_mods_names"), string.Join(", ", modNames.Take(5))));
                evidence.AddRange(conflict);
                return Build(L10n.T("crash_mods_title"), L10n.T("crash_mods_human"),
                    "Incompatible or broken mod", evidence, requirements, instanceLogPath, exitCode);
            }

            // 6. Graphics / driver.
            var gpu = PickLines(logText, @"EXCEPTION_ACCESS_VIOLATION|ig[0-9][a-z0-9_]*\.dll|atio6axx|nvoglv|Pixel format not accelerated|GLFW error 6554|Failed to create.*context|OpenGL", 3);
            if (gpu.Count > 0)
            {
                return Build(L10n.T("crash_gpu_title"), L10n.T("crash_gpu_human"),
                    "Graphics driver / OpenGL failure", gpu, requirements, instanceLogPath, exitCode);
            }

            // 7. Disk full / access denied.
            if (ContainsAny(logText, "No space left on device|Disk quota exceeded|There is not enough space on the disk"))
            {
                return Build(L10n.T("crash_disk_title"), L10n.T("crash_disk_human"),
                    "Drive ran out of free space",
                    PickLines(logText, "No space left|Disk quota|not enough space", 2),
                    requirements, instanceLogPath, exitCode);
            }
            var perm = PickLines(logText, "AccessDeniedException|Access to the path .* denied|Permission denied|Failed to (create|write|save).* (directory|file)", 2);
            if (perm.Count > 0)
            {
                return Build(L10n.T("crash_perm_title"), L10n.T("crash_perm_human"),
                    "Cannot write game files", perm, requirements, instanceLogPath, exitCode);
            }

            // 8. Corrupted jars / locked world.
            var corrupt = PickLines(logText, "Invalid or corrupt jarfile|ZipException|zip file is empty|Session lock|already locked|Level .*(corrupt|damaged)|Failed to read level", 3);
            if (corrupt.Count > 0)
            {
                return Build(L10n.T("crash_corrupt_title"), L10n.T("crash_corrupt_human"),
                    "Damaged game or world files", corrupt, requirements, instanceLogPath, exitCode);
            }

            // 9. Crash-report description / first error as a last hint.
            var desc = PickLines(logText, "Description: ", 1);
            if (desc.Count > 0) evidence.AddRange(desc);
            var caused = PickLines(logText, "Caused by: ", 2);
            evidence.AddRange(caused);
        }

        // 10. Exit-code fallback (137 is routinely the OOM-killer).
        if (exitCode == 137 || exitCode == -1073741819)
        {
            cause = "Process killed (exit 137 — often the system OOM-killer)";
            return Build(L10n.T("crash_mem_title"), L10n.T("crash_mem_human"), cause,
                evidence, requirements, instanceLogPath, exitCode);
        }

        return Build(title, human, cause, evidence, requirements, instanceLogPath, exitCode);
    }

    private static CrashAnalysis Build(string title, string human, string cause,
        List<string> evidence, List<string> requirements, string logPath, int exitCode)
    {
        var ev = evidence.Take(MaxEvidence).ToList();
        ev.Insert(0, string.Format(L10n.T("crash_exit_code"), exitCode));
        return new CrashAnalysis(title, human, cause, ev, logPath, exitCode,
            requirements.Count > 0 ? requirements : null);
    }

    private static bool ContainsAny(string text, string alternation) =>
        Regex.IsMatch(text, alternation, RegexOptions.IgnoreCase);

    private static List<string> PickLines(string text, string alternation, int max)
    {
        var out_ = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            if (out_.Count >= max) break;
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || IsNoise(line)) continue;
            if (Regex.IsMatch(line, alternation, RegexOptions.IgnoreCase))
                out_.Add(line.Length > MaxEvidenceLine ? line[..MaxEvidenceLine] + "…" : line);
        }
        return out_;
    }

    /// <summary>WARN/INFO chatter and thread-dump frames are bystanders, not causes.</summary>
    private static bool IsNoise(string line)
    {
        if (line.StartsWith('"') && (line.Contains(" Id=") || line.Contains(" prio="))) return true;
        var head = line.Length > 160 ? line[..160].ToLowerInvariant() : line.ToLowerInvariant();
        return head.Contains("/warn]") || head.Contains("/info]") || head.Contains("/debug]")
            || head.Contains("[warn]") || head.Contains("[info]") || head.Contains("[debug]")
            || head.StartsWith("[authlib-injector] [info]") || head.StartsWith("picked up ");
    }

    private static List<string> ExtractModJars(string text)
    {
        var seen = new List<string>();
        foreach (Match m in Regex.Matches(text, @"[/\\]mods[/\\]([^/\\:;""']+\.jar)", RegexOptions.IgnoreCase))
        {
            var name = m.Groups[1].Value;
            if (!seen.Contains(name, StringComparer.OrdinalIgnoreCase))
                seen.Add(name);
            if (seen.Count >= 8) break;
        }
        return seen;
    }

    private static IEnumerable<string> ParseRequirements(string text)
    {
        // Forge: Mod 'x' (...) requires version ... of 'y'
        foreach (Match m in Regex.Matches(text, @"Mod '([^']+)'[^'\n]*requires [^'\n]*?of '([^']+)'", RegexOptions.IgnoreCase))
            yield return string.Format(L10n.T("crash_req_item"), m.Groups[1].Value, m.Groups[2].Value, L10n.T("crash_ver_any"));
        // Fabric: Mod "x" requires "y"
        foreach (Match m in Regex.Matches(text, @"Mod ""([^""]+)""[^""\n]*requires [""']([^""']+)[""']", RegexOptions.IgnoreCase))
            yield return string.Format(L10n.T("crash_req_item"), m.Groups[1].Value, m.Groups[2].Value, L10n.T("crash_ver_any"));
    }

    private static string ReadLogTail(string instanceLogPath, string instanceBasePath)
    {
        try
        {
            // Newest crash-report first: it already isolates the failure.
            var crashDir = string.IsNullOrWhiteSpace(instanceBasePath)
                ? null
                : Path.Combine(instanceBasePath, "crash-reports");
            if (!string.IsNullOrWhiteSpace(crashDir) && Directory.Exists(crashDir))
            {
                var newest = Directory.GetFiles(crashDir, "*.txt")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (newest != null && newest.Length > 0 && newest.Length < MaxLogBytes * 2)
                {
                    var crashText = File.ReadAllText(newest.FullName);
                    if (!string.IsNullOrWhiteSpace(crashText))
                        return Tail(crashText);
                }
            }
            if (!string.IsNullOrWhiteSpace(instanceLogPath) && File.Exists(instanceLogPath))
            {
                using var fs = new FileStream(instanceLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length > MaxLogBytes)
                    fs.Seek(-MaxLogBytes, SeekOrigin.End);
                using var sr = new StreamReader(fs);
                return sr.ReadToEnd();
            }
        }
        catch { }
        return "";
    }

    private static string Tail(string text)
    {
        if (text.Length <= MaxLogBytes) return text;
        return text.Substring(text.Length - MaxLogBytes);
    }
}
