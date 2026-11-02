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
    IReadOnlyList<string>? Requirements = null,
    IReadOnlyList<string>? SolutionSteps = null,
    IReadOnlyList<string>? FaultyMods = null,
    string? InstanceBasePath = null);

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
        var solutionSteps = new List<string>();

        // Resolve actual instance crash report or latest.log
        string resolvedLogPath = instanceLogPath;
        if (!string.IsNullOrWhiteSpace(instanceBasePath))
        {
            var crashReportsDir = Path.Combine(instanceBasePath, "crash-reports");
            if (Directory.Exists(crashReportsDir))
            {
                var newestCrash = Directory.GetFiles(crashReportsDir, "*.txt")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (newestCrash != null && File.Exists(newestCrash.FullName))
                    resolvedLogPath = newestCrash.FullName;
            }
            if (resolvedLogPath == instanceLogPath)
            {
                var instanceLatestLog = Path.Combine(instanceBasePath, "logs", "latest.log");
                if (File.Exists(instanceLatestLog))
                    resolvedLogPath = instanceLatestLog;
            }
        }

        var logText = ReadLogTail(resolvedLogPath, instanceBasePath);

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
                solutionSteps.Add("No action required. The crash was manually triggered via keyboard shortcut Shift+F3+C.");
                return Build(title, human, cause, evidence, requirements, solutionSteps, resolvedLogPath, exitCode);
            }

            // 2. Out of memory.
            var oom = PickLines(logText, "OutOfMemoryError|Out of memory|Failed to allocate|Memory allocation failed", 3);
            if (oom.Count > 0)
            {
                solutionSteps.Add("1. Open Profile Settings and navigate to Memory (RAM).");
                solutionSteps.Add("2. Increase Allocated RAM (recommended: 4096 MB - 6144 MB for modded Minecraft).");
                solutionSteps.Add("3. Close memory-heavy background applications before launching.");
                return Build(L10n.T("crash_mem_title"), L10n.T("crash_mem_human"),
                    "Java heap exhausted", oom, requirements, solutionSteps, resolvedLogPath, exitCode);
            }

            // 3. Wrong Java (class file version -> required major).
            var cfv = Regex.Match(logText, @"class file version\s+(\d+)", RegexOptions.IgnoreCase);
            if (!cfv.Success)
                cfv = Regex.Match(logText, @"UnsupportedClassVersionError[^\d]*(\d+)", RegexOptions.IgnoreCase);
            if (cfv.Success && int.TryParse(cfv.Groups[1].Value, out var classMajor))
            {
                var needJava = classMajor >= 69 ? 25 : classMajor >= 65 ? 21 : classMajor >= 61 ? 17 : classMajor >= 55 ? 11 : 8;
                requirements.Add($"Requires Java {needJava} (class file {classMajor})");
                solutionSteps.Add($"1. Open Profile Settings -> Java Runtime.");
                solutionSteps.Add($"2. Set Java Mode to 'Recommended' or select Java {needJava}.");
                solutionSteps.Add($"3. If Java {needJava} is not available, install it via Settings -> Java Runtimes.");
                return Build(L10n.T("crash_java_title"), L10n.T("crash_java_human"),
                    $"Game needs Java {needJava}",
                    PickLines(logText, "UnsupportedClassVersionError|class file version|has been compiled by a more recent version", 3),
                    requirements, solutionSteps, resolvedLogPath, exitCode);
            }

            // 4. Missing mod dependencies or incompatibilities (Fabric, Forge, NeoForge).
            var depLines = PickLines(logText, @"Incompatible mod set found|FormattedException|Missing.*dependenc|requires.*which is missing|requires .* of |needs .* to be|but it is missing|IncompatibleModsException|DuplicateModsFoundException|ModResolutionException|ModLoadingException|Some of your mods are incompatible|A potential solution has been determined|requires version.*of mod", 6);
            
            var reqMatch = Regex.Match(logText, @"Mod\s+'([^']+)'\s*(?:\([^)]+\))?[^'
]*?requires\s+(?:version\s+([^
,]+?)\s+or\s+later\s+of\s+|version\s+([^
,]+?)\s+of\s+)?([a-zA-Z0-9_\-]+)(?:,\s*which\s+is\s+missing)?", RegexOptions.IgnoreCase);
            var installMatch = Regex.Match(logText, @"Install\s+([a-zA-Z0-9_\-]+)(?:,\s*version\s+([^
,]+))?", RegexOptions.IgnoreCase);

            string? missingMod = null;
            string? requiringMod = null;
            string? requiredVer = null;

            if (installMatch.Success)
            {
                missingMod = installMatch.Groups[1].Value.Trim();
                if (installMatch.Groups[2].Success)
                    requiredVer = installMatch.Groups[2].Value.Trim();
            }

            if (reqMatch.Success)
            {
                requiringMod = reqMatch.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(missingMod) && reqMatch.Groups[4].Success)
                    missingMod = reqMatch.Groups[4].Value.Trim();
                if (string.IsNullOrWhiteSpace(requiredVer))
                    requiredVer = reqMatch.Groups[2].Success ? reqMatch.Groups[2].Value.Trim() : (reqMatch.Groups[3].Success ? reqMatch.Groups[3].Value.Trim() : null);
            }

            var modNames = ExtractModJars(logText, instanceBasePath);
            if (!string.IsNullOrWhiteSpace(missingMod) && !modNames.Contains(missingMod, StringComparer.OrdinalIgnoreCase))
                modNames.Insert(0, missingMod);
            if (!string.IsNullOrWhiteSpace(requiringMod) && !modNames.Contains(requiringMod, StringComparer.OrdinalIgnoreCase))
                modNames.Add(requiringMod);

            if (depLines.Count > 0 || !string.IsNullOrWhiteSpace(missingMod))
            {
                string causeTitle;
                string humanDesc;

                if (!string.IsNullOrWhiteSpace(missingMod))
                {
                    causeTitle = !string.IsNullOrWhiteSpace(requiringMod)
                        ? $"Missing mod '{missingMod}' (required by {requiringMod})"
                        : $"Missing mod '{missingMod}'";
                    humanDesc = !string.IsNullOrWhiteSpace(requiringMod)
                        ? $"Mod '{requiringMod}' requires '{missingMod}'{(string.IsNullOrWhiteSpace(requiredVer) ? "" : $" (version {requiredVer})")}, which is missing. Click 'Download Compatible' to install '{missingMod}' or 'Disable Mod' to disable '{requiringMod}'."
                        : $"The game requires '{missingMod}', which is not installed. Click 'Download Compatible' to install it.";
                }
                else
                {
                    causeTitle = "Missing or incompatible mod dependency";
                    humanDesc = L10n.T("crash_mods_human");
                }

                foreach (var req in ParseRequirements(logText).Take(5))
                    requirements.Add(req);
                if (requirements.Count == 0 && !string.IsNullOrWhiteSpace(missingMod))
                    requirements.Add($"Install {missingMod}{(string.IsNullOrWhiteSpace(requiredVer) ? "" : $" {requiredVer}")}");

                if (modNames.Count > 0)
                    evidence.Add(string.Format(L10n.T("crash_mods_names"), string.Join(", ", modNames.Take(5))));
                evidence.AddRange(depLines);

                if (!string.IsNullOrWhiteSpace(missingMod))
                    solutionSteps.Add($"1. Click 'Download Compatible' to search and install '{missingMod}'.");
                if (!string.IsNullOrWhiteSpace(requiringMod))
                    solutionSteps.Add($"2. Or click 'Disable Mod' to disable '{requiringMod}'.");
                solutionSteps.Add("3. Ensure all installed mods match your mod loader (Fabric vs Forge vs NeoForge).");

                return Build(L10n.T("crash_mods_title"), humanDesc, causeTitle,
                    evidence, requirements, solutionSteps, resolvedLogPath, exitCode, modNames, instanceBasePath);
            }

            // 5. Mod conflict / broken mod code (checked before graphics to avoid false positives on Exit Code: 1).
            var conflict = PickLines(logText, @"NoClassDefFoundError|NoSuchMethodError|Mixin apply failed|MixinPrepareError|MixinTransformerError|Duplicate Questo|is already registered|mod .* (crashed|failed)|Caused by:.*(fabric|forge|neoforge|mixin)", 4);
            if (conflict.Count > 0 || (modNames.Count > 0 && exitCode == 1))
            {
                if (modNames.Count > 0)
                    evidence.Add(string.Format(L10n.T("crash_mods_names"), string.Join(", ", modNames.Take(5))));
                if (conflict.Count > 0)
                    evidence.AddRange(conflict);
                solutionSteps.Add("1. Review the faulting mod or mixin in the Details section below.");
                solutionSteps.Add("2. Click 'Disable Mod' below to disable the conflicting file.");
                solutionSteps.Add("3. Click 'Download Compatible' to find a newer version for your Minecraft version.");
                return Build(L10n.T("crash_mods_title"), L10n.T("crash_mods_human"),
                    "Incompatible or broken mod", evidence, requirements, solutionSteps, resolvedLogPath, exitCode, modNames, instanceBasePath);
            }

            // 6. Graphics / driver (strictly verify driver dll or GLFW context failure, not generic OpenGL mentioned in mod stack).
            var gpu = PickLines(logText, @"EXCEPTION_ACCESS_VIOLATION.*(ig[0-9]|atio6axx|nvoglv|nvd3dum)|(ig[0-9][a-z0-9_]*|atio6axx|nvoglv64)\.dll(?=.*(crash|exception|error|violation))|Pixel format not accelerated|GLFW error 6554: (WGL|GLX|EGL|GLFW)|Failed to create a valid OpenGL context|org\.lwjgl\.opengl\.OpenGLException", 3)
                .Where(line => !line.Contains("NVIDIA Compatible OpenGL ICD", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (gpu.Count > 0)
            {
                solutionSteps.Add("1. Update your graphics card drivers (NVIDIA, AMD, or Intel).");
                solutionSteps.Add("2. For laptops with dual GPUs, force the High-Performance discrete GPU.");
                solutionSteps.Add("3. Disable active Shader Packs or incompatible graphics mods.");
                return Build(L10n.T("crash_gpu_title"), L10n.T("crash_gpu_human"),
                    "Graphics driver / OpenGL failure", gpu, requirements, solutionSteps, resolvedLogPath, exitCode);
            }

            // 7. Disk full / access denied.
            if (ContainsAny(logText, "No space left on device|Disk quota exceeded|There is not enough space on the disk"))
            {
                solutionSteps.Add("1. Free up disk space on the drive where Zenith and instances are stored.");
                solutionSteps.Add("2. Empty the Recycle Bin and delete old temporary files.");
                return Build(L10n.T("crash_disk_title"), L10n.T("crash_disk_human"),
                    "Drive ran out of free space",
                    PickLines(logText, "No space left|Disk quota|not enough space", 2),
                    requirements, solutionSteps, resolvedLogPath, exitCode);
            }
            var perm = PickLines(logText, "AccessDeniedException|Access to the path .* denied|Permission denied|Failed to (create|write|save).* (directory|file)", 2);
            if (perm.Count > 0)
            {
                solutionSteps.Add("1. Ensure the game folder is not set to Read-Only or locked by another app.");
                solutionSteps.Add("2. Add the Zenith Launcher directory to your antivirus exclusions.");
                return Build(L10n.T("crash_perm_title"), L10n.T("crash_perm_human"),
                    "Cannot write game files", perm, requirements, solutionSteps, resolvedLogPath, exitCode);
            }

            // 8. Corrupted jars / locked world.
            var corrupt = PickLines(logText, "Invalid or corrupt jarfile|ZipException|zip file is empty|Session lock|already locked|Level .*(corrupt|damaged)|Failed to read level", 3);
            if (corrupt.Count > 0)
            {
                solutionSteps.Add("1. Close any hanging javaw.exe processes in Task Manager.");
                solutionSteps.Add("2. If a world is locked, delete 'session.lock' in the world save folder.");
                solutionSteps.Add("3. Redownload corrupt mod jar files or restore world from backup.");
                return Build(L10n.T("crash_corrupt_title"), L10n.T("crash_corrupt_human"),
                    "Damaged game or world files", corrupt, requirements, solutionSteps, resolvedLogPath, exitCode);
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
            solutionSteps.Add("1. Increase RAM allocation in Profile Settings -> Memory.");
            solutionSteps.Add("2. Close background applications to free up system memory.");
            return Build(L10n.T("crash_mem_title"), L10n.T("crash_mem_human"), cause,
                evidence, requirements, solutionSteps, resolvedLogPath, exitCode);
        }

        solutionSteps.Add("1. Click 'Open Log' below to inspect the full game log.");
        solutionSteps.Add("2. Disable recently added mods or test with a clean profile.");
        return Build(title, human, cause, evidence, requirements, solutionSteps, resolvedLogPath, exitCode);
    }

    private static CrashAnalysis Build(string title, string human, string cause,
        List<string> evidence, List<string> requirements, List<string> solutionSteps, string logPath, int exitCode,
        List<string>? faultyMods = null, string? instanceBasePath = null)
    {
        var ev = evidence.Take(MaxEvidence).ToList();
        ev.Insert(0, string.Format(L10n.T("crash_exit_code"), exitCode));
        return new CrashAnalysis(title, human, cause, ev, logPath, exitCode,
            requirements.Count > 0 ? requirements : null,
            solutionSteps.Count > 0 ? solutionSteps : null,
            faultyMods != null && faultyMods.Count > 0 ? faultyMods : null,
            instanceBasePath);
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

    private static List<string> ExtractModJars(string text, string instanceBasePath = "")
    {
        var seen = new List<string>();

        // Missing dependencies in Fabric (e.g. requires yet_another_config_lib 3.2.0 or later)
        foreach (Match m in Regex.Matches(text, @"requires\s+([a-zA-Z0-9_\-]+)(?:\s+[0-9.]|\s+or later|\s*, which is missing)", RegexOptions.IgnoreCase))
        {
            var req = m.Groups[1].Value.Trim();
            if (req.Length > 2 && !seen.Contains(req, StringComparer.OrdinalIgnoreCase) && !req.Equals("minecraft", StringComparison.OrdinalIgnoreCase))
                seen.Add(req);
        }

        foreach (Match m in Regex.Matches(text, @"[/\\]mods[/\\]([^/\\:;""'\s]+\.jar)", RegexOptions.IgnoreCase))
        {
            var name = m.Groups[1].Value;
            if (!seen.Contains(name, StringComparer.OrdinalIgnoreCase))
                seen.Add(name);
            if (seen.Count >= 8) break;
        }

        foreach (Match m in Regex.Matches(text, @"(?:Mod|mod)\s+['""]([^'""]+)['""]", RegexOptions.IgnoreCase))
        {
            var id = m.Groups[1].Value.Trim();
            if (id.Length > 2 && !id.Equals("minecraft", StringComparison.OrdinalIgnoreCase) && !seen.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(instanceBasePath))
                {
                    var modsDir = Path.Combine(instanceBasePath, "mods");
                    if (Directory.Exists(modsDir))
                    {
                        var matchingJar = Directory.GetFiles(modsDir, "*.jar")
                            .Select(Path.GetFileName)
                            .FirstOrDefault(f => f != null && f.Contains(id, StringComparison.OrdinalIgnoreCase));
                        if (matchingJar != null && !seen.Contains(matchingJar, StringComparer.OrdinalIgnoreCase))
                        {
                            seen.Add(matchingJar);
                            continue;
                        }
                    }
                }
                seen.Add(id);
            }
            if (seen.Count >= 8) break;
        }
        return seen;
    }

    private static IEnumerable<string> ParseRequirements(string text)
    {
        // Forge: Mod 'x' (...) requires version ... of 'y'
        foreach (Match m in Regex.Matches(text, @"Mod '([^']+)'[^'\n]*requires [^'\n]*?of '([^']+)'", RegexOptions.IgnoreCase))
            yield return string.Format(L10n.T("crash_req_item"), m.Groups[1].Value, m.Groups[2].Value, L10n.T("crash_ver_any"));
        // Fabric: Mod "x" requires "y" or Mod 'x' requires y ... which is missing
        foreach (Match m in Regex.Matches(text, @"Mod\s+['""]([^'""]+)['""][^'\n]*?requires\s+['""]?([a-zA-Z0-9_\-]+)['""]?", RegexOptions.IgnoreCase))
            yield return string.Format(L10n.T("crash_req_item"), m.Groups[1].Value, m.Groups[2].Value, L10n.T("crash_ver_any"));
        // Fabric bullet: - mod_name x.y.z or later
        foreach (Match m in Regex.Matches(text, @"^\s*-\s+([a-zA-Z0-9_\-]+)\s+([0-9.]+.*?)\s*$", RegexOptions.Multiline))
        {
            var dep = m.Groups[1].Value;
            if (!dep.Equals("replace", StringComparison.OrdinalIgnoreCase) && !dep.Equals("a", StringComparison.OrdinalIgnoreCase) && !dep.Equals("any", StringComparison.OrdinalIgnoreCase))
                yield return $"{dep} ({m.Groups[2].Value.Trim()})";
        }
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
