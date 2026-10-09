using System;

namespace CustomMcLauncher.Services;

/// <summary>
/// Version-gated visibility rules for server.properties keys.
/// Minecraft renames/removes settings between releases, so the editor hides
/// keys that do not exist on the server's game version instead of writing
/// dead values. Unknown / unparsable versions show everything (safe default).
/// Reference history:
/// - simulation-distance: added in 1.18, absent before.
/// - require-resource-pack: added in 1.17, absent before.
/// - resource-pack-prompt: added in 1.20.2, absent before.
/// - entity-broadcast-range-percentage: removed in 1.21.2.
/// - level-type: removed in 1.19 (no longer edited by the launcher at all).
/// </summary>
public static class ServerPropertiesRules
{
    public const string SimulationDistance = "simulation-distance";
    public const string RequireResourcePack = "require-resource-pack";
    public const string ResourcePackPrompt = "resource-pack-prompt";
    public const string EntityBroadcastRange = "entity-broadcast-range-percentage";

    public static bool IsSupported(string key, string? mcVersion)
    {
        if (!TryParseRelease(mcVersion, out var major, out var minor, out _))
            return true; // snapshots / unknown -> show all

        return key switch
        {
            SimulationDistance => major > 1 || (major == 1 && minor >= 18),
            RequireResourcePack => major > 1 || (major == 1 && minor >= 17),
            ResourcePackPrompt => IsResourcePackPromptSupported(mcVersion),
            EntityBroadcastRange => major < 1 || (major == 1 && minor < 21),
            _ => true,
        };
    }

    /// <summary>
    /// resource-pack-prompt requires 1.20.2+, which needs the patch number.
    /// Kept separate so the common path stays simple.
    /// </summary>
    public static bool IsResourcePackPromptSupported(string? mcVersion)
    {
        if (!TryParseRelease(mcVersion, out var major, out var minor, out var patch))
            return true;
        if (major != 1) return major > 1;
        if (minor != 20) return minor > 20;
        return patch >= 2;
    }

    public static bool TryParseRelease(string? version, out int major, out int minor, out int patch)
    {
        major = 0; minor = 0; patch = 0;
        if (string.IsNullOrWhiteSpace(version))
            return false;
        var v = version.Trim();
        // Strip pre-release suffixes: "1.20.1-pre1", "1.19-rc2", "1.20.4_something"
        var cut = v.IndexOfAny(new[] { '-', '_', ' ', '+' });
        if (cut >= 0) v = v[..cut];
        var parts = v.Split('.');
        if (parts.Length < 2) return false; // snapshots like "24w33a" -> unknown
        if (!int.TryParse(parts[0], out major) || !int.TryParse(parts[1], out minor))
            return false;
        if (parts.Length >= 3 && !int.TryParse(parts[2], out patch))
            patch = 0;
        return major > 0;
    }

    /// <summary>
    /// Rewrites -Xms/-Xmx flags inside a JVM arguments string to match ramMb.
    /// If no flags are present they are prepended. Used to keep the launch
    /// arguments textbox in sync when the RAM slider moves.
    /// </summary>
    public static string WithRamFlags(string? jvmArgs, int ramMb)
    {
        if (ramMb <= 0) return jvmArgs ?? "";
        var minMb = Math.Max(512, ramMb / 2);
        var args = StripRamFlags(jvmArgs);
        var prefix = $"-Xms{minMb}M -Xmx{ramMb}M";
        return string.IsNullOrWhiteSpace(args) ? prefix : prefix + " " + args;
    }

    /// <summary>Removes -Xms/-Xmx flags so callers can re-apply canonical values.</summary>
    public static string StripRamFlags(string? jvmArgs)
    {
        if (string.IsNullOrWhiteSpace(jvmArgs)) return "";
        var args = System.Text.RegularExpressions.Regex.Replace(jvmArgs, @"-Xms\d+[gGmM]", " ");
        args = System.Text.RegularExpressions.Regex.Replace(args, @"-Xmx\d+[gGmM]", " ");
        return System.Text.RegularExpressions.Regex.Replace(args, @"\s+", " ").Trim();
    }
}
