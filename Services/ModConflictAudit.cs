using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CustomMcLauncher.Services;

public enum ModConflictKind
{
    /// <summary>Same mod id in two or more files (different versions or copies).</summary>
    DuplicateId,
    /// <summary>foo.jar and foo.jar.disabled both present.</summary>
    EnabledAndDisabled,
}

public sealed record ModConflict(
    ModConflictKind Kind,
    string ModId,
    IReadOnlyList<string> Files,
    string Hint);

/// <summary>
/// Scans a mods/plugins folder for duplicate mod ids: the most common reason
/// a Forge/Fabric instance refuses to start, normally hunted by hand.
/// Jars whose id cannot be read are grouped by file stem instead.
/// </summary>
public static class ModConflictAudit
{
    public static IReadOnlyList<ModConflict> AnalyzeDirectory(string dir)
    {
        var out_ = new List<ModConflict>();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return out_;

        string[] files;
        try
        {
            files = Directory.GetFiles(dir, "*.jar*")
                .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch { return out_; }

        // foo.jar + foo.jar.disabled pair.
        var byBase = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            var stem = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? name[..^".disabled".Length]
                : name;
            if (!byBase.TryGetValue(stem, out var list))
                byBase[stem] = list = new List<string>();
            list.Add(f);
        }
        var pairedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in byBase)
        {
            var hasJar = kv.Value.Any(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                && !f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase));
            var hasDisabled = kv.Value.Any(f => f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase));
            if (hasJar && hasDisabled)
            {
                pairedStems.Add(kv.Key);
                out_.Add(new ModConflict(ModConflictKind.EnabledAndDisabled, kv.Key,
                    kv.Value.Select(Path.GetFileName).Where(n => n != null).Select(n => n!).ToList(),
                    "Both copies load-state fight: delete the stale one."));
            }
        }

        // Same mod id in several files.
        var byId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var unknown = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            if (ModMetaReader.TryGetModId(f, out var id, out _))
            {
                if (!byId.TryGetValue(id, out var list))
                    byId[id] = list = new List<string>();
                list.Add(f);
            }
            else
            {
                var stem = StripVersionSuffix(Path.GetFileNameWithoutExtension(
                    f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase) ? f[..^".disabled".Length] : f));
                if (!unknown.TryGetValue(stem, out var list))
                    unknown[stem] = list = new List<string>();
                list.Add(f);
            }
        }
        foreach (var kv in byId.Where(kv => kv.Value.Count > 1))
        {
            // An enabled+disabled pair of the same file is already reported
            // above; don't repeat it as a duplicate id.
            var stems = kv.Value.Select(f =>
            {
                var n = Path.GetFileName(f);
                return n.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? n[..^".disabled".Length] : n;
            }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (stems.Count == 1 && pairedStems.Contains(stems[0]))
                continue;
            out_.Add(new ModConflict(ModConflictKind.DuplicateId, kv.Key,
                kv.Value.Select(Path.GetFileName).Where(n => n != null).Select(n => n!).ToList(),
                "Same mod installed twice: keep one version."));
        }
        foreach (var kv in unknown.Where(kv => kv.Value.Count > 1))
        {
            // Only flag when the files are actually identical copies/snapshots.
            var sizes = kv.Value.Select(f => { try { return new FileInfo(f).Length; } catch { return -1L; } }).ToList();
            if (sizes.Distinct().Count() == 1)
            {
                out_.Add(new ModConflict(ModConflictKind.DuplicateId, kv.Key + " (unreadable id)",
                    kv.Value.Select(Path.GetFileName).Where(n => n != null).Select(n => n!).ToList(),
                    "Identical files under different names: keep one."));
            }
        }
        return out_;
    }

    /// <summary>Strips trailing version bits: foo-1.20.1-0.15.0, foo_v2, foo (1).</summary>
    private static string StripVersionSuffix(string stem)
    {
        var s = stem.Trim();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[\s._-]*\(?\d+\)?$", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[\s._-]+v?\d+(\.\d+)+.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[\s._-]+(v\d+|build\.?\d+|release|final)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return s.Trim().ToLowerInvariant();
    }
}
