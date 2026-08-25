using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
/// Turns a crashed Minecraft process into a human-readable explanation.
/// Scans the instance log tail plus the newest crash-report / latest.log
/// files and applies ordered pattern rules (memory, Java, mods, corrupted
/// files, GPU/driver, permissions).
/// </summary>
public static class CrashAnalyzer
{
    private sealed record Rule(
        string[] Patterns,
        Func<string> Title,
        Func<string> Human,
        int Priority);

    // Ordered by diagnostic priority - the first rule with a match wins.
    private static readonly List<Rule> Rules = new()
    {
        new(
            new[] { "java.lang.OutOfMemoryError", "OutOfMemoryError", "Could not reserve enough space for object heap" },
            () => L10n.T("crash_mem_title"),
            () => L10n.T("crash_mem_human"),
            10),

        new(
            new[] { "UnsupportedClassVersionError", "has been compiled by a more recent version", "bad class file... class file has wrong version" },
            () => L10n.T("crash_java_title"),
            () => L10n.T("crash_java_human"),
            20),

        new(
            new[]
            {
                "Missing or unsupported mandatory dependencies",
                "Duplicate mods found",
                "Mod resolution",
                "Mixin apply failed",
                "MixinTransformationHandler",
                "missing mod",
                "Missing Mods",
                "Incompatible mod set",
                "Incompatible mods found",
                "which is missing",
                "NoSuchMethodError",
                "NoClassDefFoundError",
                "MixinApplyError",
                "InjectionError",
                "Failed to create mod instance",
                "ModLoadingException",
            },
            () => L10n.T("crash_mods_title"),
            () => L10n.T("crash_mods_human"),
            30),

        new(
            new[]
            {
                "ZipException",
                "Invalid or corrupt jarfile",
                "checksum validation failed",
                "File checksums did not validate",
                "Tried to read a chunk that was not in the region file",
            },
            () => L10n.T("crash_corrupt_title"),
            () => L10n.T("crash_corrupt_human"),
            40),

        new(
            new[]
            {
                "EXCEPTION_ACCESS_VIOLATION",
                "-1073741819",
                "0xC0000005",
                "hs_err_pid",
                "Failed to check OpenGL",
                "Pixel format not accelerated",
                "GLX error",
                "glfwInit",
                "Failed to create window",
                "nvlddmkm",
                "dxgi_error_device_removed",
                "Device removed",
                "Device lost",
            },
            () => L10n.T("crash_gpu_title"),
            () => L10n.T("crash_gpu_human"),
            50),

        new(
            new[]
            {
                "Access is denied",
                "AccessDeniedException",
                "UnauthorizedAccessException",
            },
            () => L10n.T("crash_perm_title"),
            () => L10n.T("crash_perm_human"),
            60),

        new(
            new[]
            {
                "No space left on device",
                "There is not enough space on the disk",
                "disk full",
            },
            () => L10n.T("crash_disk_title"),
            () => L10n.T("crash_disk_human"),
            70),
    };

    public static CrashAnalysis Analyze(int exitCode, string instanceLogPath, string instanceBasePath)
    {
        var combined = new StringBuilder();
        combined.Append(ReadTail(instanceLogPath, 256 * 1024));

        try
        {
            var crashDir = Path.Combine(instanceBasePath, "crash-reports");
            if (Directory.Exists(crashDir))
            {
                var newest = Directory.GetFiles(crashDir, "*.txt")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (newest != null)
                {
                    combined.Append("\n=== crash-report: ").Append(newest.Name).Append(" ===\n");
                    combined.Append(ReadTail(newest.FullName, 128 * 1024));
                }
            }
            var latest = Path.Combine(instanceBasePath, "logs", "latest.log");
            if (File.Exists(latest))
                combined.Append("\n=== latest.log ===\n").Append(ReadTail(latest, 128 * 1024));
        }
        catch { /* best-effort enrichment only */ }

        var text = combined.ToString();
        var lines = text.Split('\n');

        // Minecraft logs are full of XML/log4j noise like
        // <log4j:Message><![CDATA[real message]]></log4j:Message>.
        // Clean every line BEFORE matching and before showing it to the user.
        var cleaned = new List<string>(lines.Length);
        foreach (var l in lines)
            cleaned.Add(CleanLine(l));

        Rule? winner = null;
        var evidence = new List<string>();

        foreach (var line in cleaned)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            foreach (var rule in Rules)
            {
                if (rule.Patterns.Any(p => line.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    if (winner == null || rule.Priority < winner.Priority)
                    {
                        winner = rule;
                        evidence.Clear();
                    }
                    if (winner == rule && evidence.Count < 12 && !evidence.Contains(line, StringComparer.Ordinal))
                        evidence.Add(line);
                    break;
                }
            }
        }

        // No known signature: surface the last error-looking lines instead.
        if (winner == null)
        {
            evidence.AddRange(cleaned
                .Where(l => !string.IsNullOrWhiteSpace(l) && (
                            l.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                            l.Contains("ERROR", StringComparison.Ordinal) ||
                            l.Contains("[STDERR]", StringComparison.Ordinal)))
                .TakeLast(8));
        }

        var humanSummary = winner?.Human() ??
            string.Format(L10n.T("crash_generic_human"), exitCode);

        // Mod-resolution crashes: try to pull out the exact mod names so the
        // user sees WHICH mod is missing / conflicting instead of a wall of log.
        IReadOnlyList<string>? requirements = null;
        if (winner != null && winner.Priority == 30)
        {
            // Fabric Loader prints a precise tree ("Mod 'X' requires version Y
            // of 'Z', which is missing!") - turn it into a readable list first.
            requirements = ParseFabricRequirements(cleaned).ToList();

            if (requirements.Count > 0)
            {
                humanSummary += "\n" + L10n.T("crash_req_header") + "\n" +
                                string.Join("\n", requirements.Take(8));
            }
            else
            {
                var mods = ExtractModNames(cleaned).Take(6).ToList();
                if (mods.Count > 0)
                {
                    humanSummary += "\n" + string.Format(L10n.T("crash_mods_names"), string.Join(", ", mods));
                    evidence.Insert(0, "[mods] " + string.Join(", ", mods));
                }
            }
        }

        return new CrashAnalysis(
            Title: winner?.Title() ?? L10n.T("crash_generic_title"),
            HumanSummary: humanSummary,
            LikelyCause: winner?.Title() ?? L10n.T("crash_generic_cause"),
            Evidence: evidence,
            LogPath: instanceLogPath,
            ExitCode: exitCode,
            Requirements: requirements);
    }

    // ------------------------------------------------------------ line cleaning

    private static readonly Regex CdataRe =
        new(@"<!\[CDATA\[(.*?)\]\]>", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex TagRe =
        new(@"</?[A-Za-z][^<>]{0,200}>", RegexOptions.Compiled);

    private static readonly Regex MultiSpaceRe =
        new(@"[ \t]{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Strips log4j/XML markup (&lt;log4j:Message&gt;&lt;![CDATA[...]]&gt; etc.)
    /// from a raw log line so humans only see the actual message text.
    /// </summary>
    public static string CleanLine(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var s = raw.TrimEnd('\r');
        if (!s.Contains('<')) return s;

        s = CdataRe.Replace(s, static m => m.Groups[1].Value);
        s = TagRe.Replace(s, " ");
        s = s.Replace("&amp;", "&")
              .Replace("&lt;", "<")
              .Replace("&gt;", ">")
              .Replace("&quot;", "\"")
              .Replace("&apos;", "'");
        s = MultiSpaceRe.Replace(s, " ");
        return s.Trim();
    }

    // ------------------------------------------------------------ mod extraction

    private static readonly Regex RequiresRe =
        new(@"requires\s+\{?([A-Za-z][A-Za-z0-9_\-\.\$]{1,80})\}?", RegexOptions.Compiled);

    private static readonly Regex MissingListRe =
        new(@"Missing mods?(?:\s+list)?\s*:?\s*(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ModIdRe =
        new(@"(?:^|[\s\-•])Mod\s+([a-z][a-z0-9_\-\.\$]{1,80})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> ModNameStoplist = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "file", "java", "version", "any", "one", "of", "to", "be", "is",
        "minecraft", "mod", "mods", "found", "missing", "requires", "supported",
        "resolution", "versions", "dependency", "dependencies", "incompatible",
        "conflicting", "conflict", "duplicate", "duplicates", "mandatory",
        "unsupported", "provide", "provided"
    };

    /// <summary>Pulls candidate mod ids/names out of cleaned crash-log lines.</summary>
    internal static IEnumerable<string> ExtractModNames(IEnumerable<string> cleanedLines)
    {
        foreach (var line in cleanedLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var m = MissingListRe.Match(line);
            if (m.Success)
            {
                foreach (var token in m.Groups[1].Value.Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var name = token.Trim('[', ']', '(', ')', '{', '}', '"', '\'');
                    if (IsPlausibleModName(name)) yield return name;
                }
            }

            foreach (Match r in RequiresRe.Matches(line))
            {
                var name = r.Groups[1].Value;
                if (IsPlausibleModName(name)) yield return name;
            }

            foreach (Match r in ModIdRe.Matches(line))
            {
                var name = r.Groups[1].Value;
                if (IsPlausibleModName(name)) yield return name;
            }
        }
    }

    // ------------------------------------------------------------ fabric deps

    // " - Mod 'sodium' (sodium-0.5.8.jar) requires version [0.4.0,) of 'fabric-api', which is missing!"
    private static readonly Regex FabricReqVersionRe =
        new(@"mod\s+'(?<src>[^']+)'.{0,160}?requires\s+version\s+(?<ver>\[[^\]]*\]|\([^)]*\)|[^\s,]+)\s+of\s+'(?<dep>[^']+)'",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // " - Mod 'cloth-config' requires any version of 'fabric-api', which is missing!"
    private static readonly Regex FabricReqAnyRe =
        new(@"mod\s+'(?<src>[^']+)'.{0,160}?requires\s+(?:any|some)\s+version\s+of\s+'(?<dep>[^']+)'",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // " - Mod 'a' conflicts with 'b'" / "... is incompatible with ..."
    private static readonly Regex FabricConflictRe =
        new(@"mod\s+'(?<a>[^']+)'.{0,160}?(?:conflicts?\s+with|is\s+incompatible\s+with)\s+'(?<b>[^']+)'",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Extracts a human-readable list of broken mod requirements from Fabric
    /// Loader's "Incompatible mods found!" tree. Each returned line is already
    /// localized and names the offending mod plus what exactly is missing.
    /// </summary>
    internal static IEnumerable<string> ParseFabricRequirements(IEnumerable<string> cleanedLines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in cleanedLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            foreach (Match m in FabricReqAnyRe.Matches(line))
            {
                var text = FormatRequirement(m.Groups["src"].Value, m.Groups["dep"].Value,
                    L10n.T("crash_ver_any"), seen);
                if (text.Length > 0) yield return text;
            }

            foreach (Match m in FabricReqVersionRe.Matches(line))
            {
                var text = FormatRequirement(m.Groups["src"].Value, m.Groups["dep"].Value,
                    m.Groups["ver"].Value.Trim(), seen);
                if (text.Length > 0) yield return text;
            }

            foreach (Match m in FabricConflictRe.Matches(line))
            {
                var text = string.Format(L10n.T("crash_conflict_item"),
                    m.Groups["a"].Value, m.Groups["b"].Value);
                if (seen.Add(text)) yield return text;
            }
        }
    }

    private static string FormatRequirement(string src, string dep, string version, HashSet<string> seen)
    {
        var text = string.Format(L10n.T("crash_req_item"), src, dep, version);
        return seen.Add(text) ? text : "";
    }

    private static bool IsPlausibleModName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length < 3 || name.Length > 80) return false;
        if (ModNameStoplist.Contains(name)) return false;
        if (name.Contains(':')) return false;          // "java.lang..." style
        return char.IsAsciiLetter(name[0]);
    }

    private static string ReadTail(string path, int maxBytes)
    {
        try
        {
            if (!File.Exists(path)) return string.Empty;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length > maxBytes)
                fs.Seek(-maxBytes, SeekOrigin.End);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }
        catch { return string.Empty; }
    }
}
