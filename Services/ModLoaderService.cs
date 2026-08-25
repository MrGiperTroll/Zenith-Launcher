using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

/// <summary>
/// Loader metadata service with multi-mirror failover.
/// Every loader is queried from several independent hosts (official endpoints,
/// BMCLAPI, MCBBS and other public mirrors). If the primary host is unreachable,
/// returns an error or resets the connection, the next mirror is used instantly —
/// so the loader list never silently disappears from "Create Profile".
/// </summary>
public class ModLoaderService
{
    // ---- Mirror hosts (keep at least 3-5 independent sources per loader) ----

    // Fabric meta is mirrored verbatim by BMCLAPI and MCBBS under /fabric-meta.
    private static readonly string[] FabricMetaHosts =
    {
        "https://meta.fabricmc.net",
        "https://bmclapi2.bangbang93.com/fabric-meta"
    };

    // Quilt meta has no widely deployed BMCL anagolue, but v3/v2 endpoints and
    // mirror attempts give us several candidates; the first one that responds wins.
    private static readonly string[] QuiltMetaHosts =
    {
        "https://meta.quiltmc.org",
        "https://bmclapi2.bangbang93.com/quilt-meta"
    };

    // Forge maven metadata (JSON) hosted on multiple maven mirrors.
    // The last entry is the official maven's XML metadata — the most reliable
    // source of all and the final fallback when every JSON mirror fails.
    private static readonly string[] ForgeMetadataHosts =
    {
        "https://files.minecraftforge.net/net/minecraftforge/forge/maven-metadata.json",
        "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/maven-metadata.json",
        "https://mirror.sjtu.edu.cn/bmclapi/net/minecraftforge/forge/maven-metadata.json",
        "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml"
    };

    private static readonly string[] ForgePerGameHosts =
    {
        "https://bmclapi2.bangbang93.com/forge/minecraft/{0}"
    };

    // Official promotions endpoint: authoritative "-latest"/"-recommended" per game
    // version. Independent failover when the large metadata payloads fail.
    private static readonly string[] ForgePromotionsHosts =
    {
        "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json",
        "https://bmclapi2.bangbang93.com/forge/promotions_slim.json"
    };

    // NeoForge version list (official JSON API, plus mirror XML/first-class mirrors).
    private static readonly string[] NeoForgeListHosts =
    {
        "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge",
        "https://maven.neoforged.net/api/maven/versions/net/neoforged/neoforge"
    };

    // NeoForge purged the 1.20.1 era (20.1.x) from every reachable mirror, but 1.20.1
    // is the most heavily modded Minecraft version. Keep the community-standard last
    // 1.20.1 build pinned so NeoForge stays selectable and installable there.
    private static readonly string[] NeoForgeLegacyGameVersions = { "1.20.1" };
    private static readonly string[] NeoForgeLegacyBuilds = { "20.1.63" };

    private static readonly string[] OptiFineVersionListHosts =
    {
        "https://bmclapi2.bangbang93.com/optifine/versionList",
        "https://bmclapi.bangbang93.com/optifine/versionList"
    };

    private static readonly string[] OptiFinePerGameHosts =
    {
        "https://bmclapi2.bangbang93.com/optifine/{0}",
        "https://bmclapi.bangbang93.com/optifine/{0}"
    };

    // ---- Cached databases (with TTL so new versions appear and dead caches heal) ----

    private readonly List<(HashSet<string> Set, DateTime FetchedAt)> _fabricVersions = new();
    private readonly List<(HashSet<string> Set, DateTime FetchedAt)> _quiltVersions = new();
    private readonly List<(HashSet<string> Set, DateTime FetchedAt)> _forgeVersions = new();
    private readonly List<(HashSet<string> Set, DateTime FetchedAt)> _neoForgeVersions = new();
    private readonly List<(HashSet<string> Set, DateTime FetchedAt)> _optiFineVersions = new();

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private const int MinAcceptableEntries = 3;

    /// <summary>Raised for each loader-database fetch attempt so the UI can surface which source failed.</summary>
    public static event Action<string>? Diagnostic;

    private static void Log(string message) => Diagnostic?.Invoke(message);

    private static bool IsCacheFresh(List<(HashSet<string> Set, DateTime FetchedAt)> cache)
        => cache.Count > 0 && DateTime.UtcNow - cache[0].FetchedAt < CacheTtl;

    /// <summary>
    /// Fetches all loader databases in parallel using multi-mirror failover.
    /// Each loader independently ends up with the best available mirror.
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        if (IsCacheFresh(_fabricVersions) &&
            IsCacheFresh(_quiltVersions) &&
            IsCacheFresh(_forgeVersions) &&
            IsCacheFresh(_neoForgeVersions) &&
            IsCacheFresh(_optiFineVersions))
            return;

        var fabricTask = FetchDatabaseJsonAsync(FabricMetaHosts, v => $"{v}/v2/versions/game", ParseFabricGameVersions);
        var quiltTask = FetchDatabaseAsync(QuiltMetaHosts, ParseQuiltGameVersions);
        var forgeTask = FetchForgeDatabaseAsync();
        var neoForgeTask = FetchNeoForgeDatabaseAsync();
        var optiFineTask = FetchOptiFineDatabaseAsync();

        await Task.WhenAll(fabricTask, quiltTask, forgeTask, neoForgeTask, optiFineTask);

        if (fabricTask.Result.Count > 0) _fabricVersions.Insert(0, (fabricTask.Result, DateTime.UtcNow));
        if (quiltTask.Result.Count > 0) _quiltVersions.Insert(0, (quiltTask.Result, DateTime.UtcNow));
        if (forgeTask.Result.Count > 0) _forgeVersions.Insert(0, (forgeTask.Result, DateTime.UtcNow));
        if (neoForgeTask.Result.Count > 0) _neoForgeVersions.Insert(0, (neoForgeTask.Result, DateTime.UtcNow));
        if (optiFineTask.Result.Count > 0) _optiFineVersions.Insert(0, (optiFineTask.Result, DateTime.UtcNow));
    }

    /// <summary>All loader types shown in the Create Profile dropdown, in display order.
    /// Static by design: a loader is never hidden just because its version database is still
    /// loading or returned empty — version-fetch errors are surfaced in the UI instead.</summary>
    public static readonly string[] AllLoaders =
    {
        "Vanilla", "Fabric", "Quilt", "OptiFine", "Forge", "NeoForge"
    };

    public List<string> GetSupportedLoaders(string gameVersion)
    {
        // All loaders are always selectable - even before a game version is picked.
        // Per-version support is enforced later by the builds fetch and the live
        // game-version filter, never by hiding entries from this dropdown.
        return AllLoaders.ToList();
    }

    /// <summary>
    /// True for locally installed modloader profile folders (fabric-loader-*,
    /// quilt-loader-*, *-forge-*, neoforge-*, OptiFine_*). These are launcher
    /// plumbing, never game versions, so they must not appear in any picker.
    /// </summary>
    public static bool IsLoaderProfileId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.IndexOf("-loader-", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (id.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase)) return true;
        if (id.StartsWith("quilt-", StringComparison.OrdinalIgnoreCase)) return true;
        if (id.StartsWith("neoforge", StringComparison.OrdinalIgnoreCase)) return true;
        if (id.Contains("-forge-", StringComparison.OrdinalIgnoreCase)) return true;
        if (id.StartsWith("forge-", StringComparison.OrdinalIgnoreCase)) return true;
        return id.IndexOf("optifine", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Game versions currently known to support the given loader, straight from the
    /// cached version database. Null for Vanilla/unknown - meaning "no filtering".
    /// Used by the Create Profile screen to hide unsupported game versions live.
    /// </summary>
    public HashSet<string>? GetCachedSupportedVersions(string loaderType)
    {
        List<(HashSet<string> Set, DateTime FetchedAt)>? cache = loaderType switch
        {
            "Fabric" => _fabricVersions,
            "Quilt" => _quiltVersions,
            "Forge" => _forgeVersions,
            "NeoForge" => _neoForgeVersions,
            "OptiFine" => _optiFineVersions,
            _ => null
        };
        return cache is { Count: > 0 } ? cache[0].Set : null;
    }

    private static (int Major, int Minor, int Patch) ParseGameVersion(string version)
    {
        var dash = version.IndexOf('-');
        if (dash > 0) version = version[..dash];

        var parts = version.Split('.');
        var major = 0; var minor = 0; var patch = 0;
        if (parts.Length >= 1) int.TryParse(parts[0], out major);
        if (parts.Length >= 2) int.TryParse(parts[1], out minor);
        if (parts.Length >= 3) int.TryParse(parts[2], out patch);
        return (major, minor, patch);
    }

    private static bool AtLeast((int Major, int Minor, int Patch) v, int major, int minor, int patch)
        => v.Major > major ||
           (v.Major == major && (v.Minor > minor || (v.Minor == minor && v.Patch >= patch)));

    private static bool AtMost((int Major, int Minor, int Patch) v, int major, int minor, int patch)
        => v.Major < major ||
           (v.Major == major && (v.Minor < minor || (v.Minor == minor && v.Patch <= patch)));

    private static bool DbHas(List<(HashSet<string> Set, DateTime FetchedAt)> cache, string version)
    {
        foreach (var (set, _) in cache)
            if (set.Contains(version)) return true;
        return false;
    }

    public async Task<List<string>> GetLoaderBuildsAsync(string gameVersion, string loaderType)
    {
        switch (loaderType)
        {
            case "Fabric":
                return await FetchLoaderBuildsAsync(FabricMetaHosts, '/', "v2/versions/loader", ParseFabricLoaderBuilds, gameVersion).ConfigureAwait(false);
            case "Quilt":
                return await FetchLoaderBuildsAsync(QuiltMetaHosts, '/', null, ParseQuiltLoaderBuilds, gameVersion).ConfigureAwait(false);
            case "Forge":
                return await FetchForgeBuildsAsync(gameVersion).ConfigureAwait(false);
            case "NeoForge":
                return await FetchNeoForgeBuildsAsync(gameVersion).ConfigureAwait(false);
            case "OptiFine":
                return await FetchOptiFineBuildsAsync(gameVersion).ConfigureAwait(false);
            default:
                // Unknown/blank loader: never return an empty list — the UI treats
                // that as a hard failure. "Latest (Auto)" keeps the flow usable.
                return FinalizeBuilds(new List<string>());
        }
    }

    // ================= Helpers =================

    /// <summary>Tries each mirror until one returns parseable, non-empty data.</summary>
    private static async Task<List<string>?> TryFetchAsync(IEnumerable<string> urls, Func<string, List<string>?> parse)
    {
        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url)) continue;
            var res = await WebFallback.GetStringFirstAsync(url).ConfigureAwait(false);
            Log($"LoaderDB GET {url} → {(res is null ? "FAIL (unreachable / HTTP error)" : $"OK ({res.Length} chars)")}");
            if (res is null) continue;
            try
            {
                var parsed = parse(res);
                if (parsed is { Count: > 0 } && IsAcceptable(parsed))
                {
                    Log($"LoaderDB parsed {parsed.Count} entries from {url}");
                    return parsed;
                }
                if (parsed is { Count: > 0 })
                    Log($"LoaderDB {url} returned {parsed.Count} entries (< {MinAcceptableEntries}) — skipping mirror");
            }
            catch
            {
                Log($"LoaderDB {url} returned a malformed payload — skipping mirror");
            }
        }
        return null;
    }

    private static bool IsAcceptable(List<string> parsed) => parsed.Count >= MinAcceptableEntries;

    private static List<string>? ParseJsonArrayValues(string json, Func<JsonElement, string?> pick)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<string>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            try
            {
                if (pick(el) is { } s && !string.IsNullOrWhiteSpace(s))
                    list.Add(s);
            }
            catch { }
        }
        return list.Count > 0 ? list : null;
    }

    private static List<string>? ParseFabricGameVersions(string json)
        => ParseJsonArrayValues(json, el => el.TryGetProperty("version", out var v) ? v.GetString() : null);

    private static List<string>? ParseFabricLoaderBuilds(string json)
        => ParseJsonArrayValues(json, el =>
            el.TryGetProperty("loader", out var l) && l.TryGetProperty("version", out var v) ? v.GetString() : null);

    private static List<string>? ParseQuiltGameVersions(string json)
        => ParseJsonArrayValues(json, el => el.TryGetProperty("version", out var v) ? v.GetString() : null);

    private static List<string>? ParseQuiltLoaderBuilds(string json)
        => ParseJsonArrayValues(json, el =>
            el.TryGetProperty("loader", out var l) && l.TryGetProperty("version", out var v) ? v.GetString() : null);

    /// <summary>Reads a Maven version list from either the NeoForge API JSON, a plain JSON array, or an XML maven-metadata.</summary>
    public static List<string>? ParseMavenVersionList(string jsonOrXml)
    {
        var trimmed = jsonOrXml.TrimStart();
        if (trimmed.StartsWith("<"))
        {
            var versions = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(trimmed, "<version>([^<]+)</version>"))
                if (m.Groups[1].Value is { Length: > 0 } v)
                    versions.Add(v);
            return versions.Count > 0 ? versions : null;
        }

        using var doc = JsonDocument.Parse(jsonOrXml);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            var arr = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                try
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        if (el.GetString() is { Length: > 0 } sv) arr.Add(sv);
                    }
                    else
                    {
                        foreach (var key in new[] { "version", "rawVersion", "name" })
                            if (el.TryGetProperty(key, out var pv) && pv.GetString() is { Length: > 0 } named)
                            {
                                arr.Add(named);
                                break;
                            }
                    }
                }
                catch { }
            }
            return arr.Count > 0 ? arr : null;
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("versions", out var versionsArr))
            return null;
        var list = new List<string>();
        foreach (var el in versionsArr.EnumerateArray())
        {
            try
            {
                if (el.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                    list.Add(s);
            }
            catch { }
        }
        return list.Count > 0 ? list : null;
    }

    // ================= Loader-specific fetchers =================

    private async Task<HashSet<string>> FetchDatabaseJsonAsync(
        string[] hosts, Func<string, string> toUrl, Func<string, List<string>?> parse)
    {
        var urls = hosts.Select(h => toUrl(h)).ToArray();
        var list = await TryFetchAsync(urls, parse).ConfigureAwait(false);
        return new HashSet<string>(list ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<string>> FetchLoaderBuildsAsync(
        string[] hosts, char sep, string? fixedPrefix, Func<string, List<string>?> parse, string gameVersion)
    {
        var urls = new List<string>();
        foreach (var h in hosts)
        {
            if (fixedPrefix != null)
            {
                urls.Add($"{h}{sep}{fixedPrefix}/{gameVersion}");
            }
            else
            {
                urls.Add($"{h}/v3/versions/loader/{gameVersion}");
                urls.Add($"{h}/v2/versions/loader/{gameVersion}");
            }
        }
        var list = await TryFetchAsync(urls, parse).ConfigureAwait(false);
        return FinalizeBuilds(list ?? new List<string>());
    }

    private async Task<HashSet<string>> FetchDatabaseAsync(string[] hosts, Func<string, List<string>?> parse)
    {
        // Quilt hosts: v3 first, then v2 (same endpoint family).
        var urls = new List<string>();
        foreach (var h in hosts)
        {
            urls.Add($"{h}/v3/versions/game");
            urls.Add($"{h}/v2/versions/game");
        }
        var list = await TryFetchAsync(urls, parse).ConfigureAwait(false);
        return new HashSet<string>(list ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<HashSet<string>> FetchForgeDatabaseAsync()
    {
        var list = await TryFetchAsync(ForgeMetadataHosts, ParseForgeAllVersions).ConfigureAwait(false);
        if (list == null)
        {
            // Last resort: per-game endpoints won't give the full DB, so we stay empty
            // rather than showing wrong data; the per-game fetch still covers builds.
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Extracts the Minecraft game versions from the Forge maven metadata or the BMCL array.</summary>
    private static List<string>? ParseForgeAllVersions(string json)
    {
        // XML maven-metadata.xml fallback: entries look like "1.20.1-47.2.0".
        var trimmedJson = json.TrimStart();
        if (trimmedJson.StartsWith("<"))
        {
            var raw = ParseMavenVersionList(json);
            if (raw == null) return null;
            var fromXml = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in raw)
            {
                var dash = s.IndexOf('-');
                if (dash > 0) fromXml.Add(s[..dash]);
            }
            return fromXml.Count > 0 ? fromXml.ToList() : null;
        }

        using var doc = JsonDocument.Parse(json);
        var set = new List<string>();

        // files.minecraftforge.net / BMCLAPI shape: { "1.20.1": ["1.20.1-47.2.0", ...], ... }
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            var gameKeyed = doc.RootElement.EnumerateObject()
                .Where(p => p.Name.Length > 0 && char.IsDigit(p.Name[0]) && p.Name.Contains('.') && p.Value.ValueKind == JsonValueKind.Array)
                .Select(p => p.Name)
                .ToList();
            if (gameKeyed.Count >= MinAcceptableEntries)
                return gameKeyed.Distinct().ToList();

            // Legacy maven.minecraftforge.net shape: { "versions": ["1.20.1-47.1.0", ...] }
            if (doc.RootElement.TryGetProperty("versions", out var versions))
            {
                foreach (var el in versions.EnumerateArray())
                {
                    try
                    {
                        var s = el.GetString();
                        if (string.IsNullOrWhiteSpace(s)) continue;
                        var dash = s.IndexOf('-');
                        set.Add(dash > 0 ? s[..dash] : s);
                    }
                    catch { }
                }
                return set.Count > 0 ? set.Distinct().ToList() : null;
            }
            return gameKeyed.Count > 0 ? gameKeyed.Distinct().ToList() : null;
        }

        // BMCL per-game array of objects/strings: [{ "version": "1.20.1-47.1.0", ... }, ...]
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            try
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    var dash = s.IndexOf('-');
                    set.Add(dash > 0 ? s[..dash] : s);
                }
                else if (el.TryGetProperty("version", out var v) && v.GetString() is { } sv)
                {
                    var dash = sv.IndexOf('-');
                    set.Add(dash > 0 ? sv[..dash] : sv);
                }
            }
            catch { }
        }
        return set.Count > 0 ? set.Distinct().ToList() : null;
    }

    private async Task<List<string>> FetchForgeBuildsAsync(string gameVersion)
    {
        // Primary: filter the full metadata; fallback: official promotions, then per-game mirrors.
        var buildList = await TryFetchAsync(ForgeMetadataHosts, json => ParseForgeBuilds(json, gameVersion)).ConfigureAwait(false);
        if (buildList is { Count: > 0 }) return FinalizeBuilds(buildList);

        // promotions_slim.json yields at most 2 entries, below MinAcceptableEntries -
        // fetched with a dedicated loop instead of TryFetchAsync's acceptance gate.
        foreach (var url in ForgePromotionsHosts)
        {
            var res = await WebFallback.GetStringFirstAsync(url).ConfigureAwait(false);
            Log($"Forge promotions GET {url} → {(res is null ? "FAIL (unreachable / HTTP error)" : $"OK ({res.Length} chars)")}");
            if (res == null) continue;
            var parsed = ParseForgePromotions(res, gameVersion);
            if (parsed is { Count: > 0 })
            {
                Log($"Forge promotions parsed {parsed.Count} entries from {url}");
                return FinalizeBuilds(parsed);
            }
        }

        var perGameUrls = ForgePerGameHosts.Select(h => string.Format(h, gameVersion)).ToArray();
        var mirrorList = await TryFetchAsync(perGameUrls, json => ParseForgeBuilds(json, gameVersion)).ConfigureAwait(false);
        if (mirrorList is { Count: > 0 }) return FinalizeBuilds(mirrorList);
        return FinalizeBuilds(new List<string>());
    }

    /// <summary>promotions_slim.json shape: { "promos": { "1.20.1-latest": "47.2.0", ... } }.</summary>
    private static List<string>? ParseForgePromotions(string json, string gameVersion)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("promos", out var promos) ||
            promos.ValueKind != JsonValueKind.Object)
            return null;
        var builds = new List<string>();
        foreach (var suffix in new[] { "latest", "recommended" })
        {
            if (promos.TryGetProperty($"{gameVersion}-{suffix}", out var v) &&
                v.GetString() is { Length: > 0 } b &&
                !builds.Contains(b, StringComparer.OrdinalIgnoreCase))
                builds.Add(b);
        }
        return builds.Count > 0 ? builds : null;
    }

    private static List<string>? ParseForgeBuilds(string json, string gameVersion)
    {
        var prefix = gameVersion + "-";

        // Official maven fallback: XML maven-metadata.xml — {prefix}{build} entries.
        var trimmedJson = json.TrimStart();
        if (trimmedJson.StartsWith("<"))
        {
            var raw = ParseMavenVersionList(json);
            if (raw == null) return null;
            var fromXml = new List<string>();
            foreach (var s in raw)
                if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    !s.Contains("installer") && !s.Contains("universal"))
                    fromXml.Add(s[prefix.Length..]);
            return fromXml.Count > 0 ? fromXml : null;
        }

        using var doc = JsonDocument.Parse(json);
        var builds = new List<string>();

        // files.minecraftforge.net / BMCLAPI shape: { gameVersion: ["1.20.1-47.2.0", ...], ... }
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            !string.IsNullOrEmpty(gameVersion) &&
            doc.RootElement.TryGetProperty(gameVersion, out var gameArr) &&
            gameArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in gameArr.EnumerateArray())
            {
                try
                {
                    var s = el.GetString();
                    if (s != null && s.StartsWith(prefix) && !s.Contains("installer") && !s.Contains("universal"))
                        builds.Add(s[prefix.Length..]);
                }
                catch { }
            }
            if (builds.Count > 0) return builds;
        }

        // Legacy maven.minecraftforge.net shape: { "versions": ["1.20.1-47.2.0", ...] }
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("versions", out var versions))
        {
            foreach (var el in versions.EnumerateArray())
            {
                try
                {
                    var s = el.GetString();
                    if (s != null && s.StartsWith(prefix) && !s.Contains("installer") && !s.Contains("universal"))
                        builds.Add(s[prefix.Length..]);
                }
                catch { }
            }
            if (builds.Count > 0) return builds;
        }

        // BMCL per-game array of objects/strings
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            try
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (s != null && s.StartsWith(prefix) && !s.Contains("installer") && !s.Contains("universal"))
                        builds.Add(s[prefix.Length..]);
                }
                else if (el.TryGetProperty("version", out var v) && v.GetString() is { } sv && sv.StartsWith(prefix) && !sv.Contains("installer") && !sv.Contains("universal"))
                {
                    builds.Add(sv[prefix.Length..]);
                }
            }
            catch { }
        }
        return builds.Count > 0 ? builds : null;
    }

    private async Task<HashSet<string>> FetchNeoForgeDatabaseAsync()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = await TryFetchAsync(NeoForgeListHosts, ParseMavenVersionList).ConfigureAwait(false);
        if (list != null)
        {
            foreach (var s in list)
            {
                var dash = s.IndexOf('-');
                var candidate = dash > 0 ? s[..dash] : s;
                var parts = candidate.Split('.');
                if (parts.Length >= 2 &&
                    int.TryParse(parts[0], out var major) && major > 0 &&
                    int.TryParse(parts[1], out var minor))
                {
                    // NeoForge X.0.x → MC 1.X; 20.x → MC 1.20.x; 26.x → MC 26.x
                    var mc = minor == 0 ? $"1.{major}" : $"1.{major}.{minor}";
                    if (major >= 20) set.Add(mc);
                }
            }
        }
        // Purged 1.20.1 era: keep NeoForge available for the most-used modding Minecraft version.
        foreach (var v in NeoForgeLegacyGameVersions) set.Add(v);
        return set;
    }

    private async Task<List<string>> FetchNeoForgeBuildsAsync(string gameVersion)
    {
        var builds = new List<string>();
        var target = ParseNeoForgeTarget(gameVersion);
        if (target != null)
        {
            var raw = await TryFetchAsync(NeoForgeListHosts, ParseMavenVersionList).ConfigureAwait(false);
            if (raw is { Count: > 0 })
            {
                // Exact major.minor match: MC 1.21 → 21.0.x, MC 1.21.4 → 21.4.x.
                // A loose prefix pulled 21.1.x into MC 1.21 and 21.11.x into MC 1.21.1.
                var prefix = $"{target.Value.Major}.{target.Value.Minor}.";
                foreach (var s in raw)
                    if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        builds.Add(s);
            }
        }
        // Purged era fallback (e.g. NeoForge 1.20.1 → 20.1.63).
        if (builds.Count == 0 && gameVersion.Equals("1.20.1", StringComparison.OrdinalIgnoreCase))
            builds.AddRange(NeoForgeLegacyBuilds);
        return FinalizeBuilds(builds);
    }

    /// <summary>MC "1.21" → NeoForge (21,0); "1.21.4" → (21,4). Null for non-1.x versions.</summary>
    private static (int Major, int Minor)? ParseNeoForgeTarget(string gameVersion)
    {
        var parts = gameVersion.Split('.');
        if (parts.Length < 2 || parts[0] != "1" || !int.TryParse(parts[1], out var major)) return null;
        var minor = parts.Length >= 3 && int.TryParse(parts[2], out var m) ? m : 0;
        return (major, minor);
    }

    private async Task<HashSet<string>> FetchOptiFineDatabaseAsync()
    {
        var list = await TryFetchAsync(OptiFineVersionListHosts, ParseOptiFineVersionList).ConfigureAwait(false);
        return new HashSet<string>(list ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
    }

    private static List<string>? ParseOptiFineVersionList(string json)
        => ParseJsonArrayValues(json, el =>
            el.TryGetProperty("mcversion", out var v) ? v.GetString() : null);

    private async Task<List<string>> FetchOptiFineBuildsAsync(string gameVersion)
    {
        var urls = OptiFinePerGameHosts.Select(h => string.Format(h, gameVersion)).ToArray();
        var list = await TryFetchAsync(urls, ParseOptiFineBuilds).ConfigureAwait(false);
        return FinalizeBuilds(list ?? new List<string>());
    }

    private static List<string>? ParseOptiFineBuilds(string json)
        => ParseJsonArrayValues(json, el =>
        {
            var type = el.TryGetProperty("type", out var t) ? t.GetString() : null;
            var patch = el.TryGetProperty("patch", out var p) ? p.GetString() : null;
            return type != null && patch != null ? $"{type}_{patch}" : null;
        });

    private static List<string> FinalizeBuilds(List<string> builds)
    {
        var cleaned = builds
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Newest first, numeric-aware so "47.3.10" outranks "47.3.9" - plain string
        // sorting placed 9 above 10 and hid the freshest builds from the UI.
        cleaned.Sort((a, b) => CompareBuildTokens(b, a));
        if (cleaned.Count == 0)
            cleaned.Add("Latest (Auto)");
        return cleaned;
    }

    private static int CompareBuildTokens(string x, string y)
    {
        var tx = TokenizeNumeric(x);
        var ty = TokenizeNumeric(y);
        for (var i = 0; i < Math.Max(tx.Count, ty.Count); i++)
        {
            var vx = i < tx.Count ? tx[i] : -1;
            var vy = i < ty.Count ? ty[i] : -1;
            if (vx != vy) return vx.CompareTo(vy);
        }
        return string.CompareOrdinal(x, y);
    }

    /// <summary>Splits a build id into numeric runs: "47.3.10" → [47,3,10], "HD_U_I7" → [7].</summary>
    private static List<long> TokenizeNumeric(string s)
    {
        var tokens = new List<long>();
        long current = 0;
        var inRun = false;
        foreach (var ch in s)
        {
            if (char.IsDigit(ch))
            {
                current = inRun ? Math.Min(current * 10 + (ch - '0'), 999_999_999L) : (ch - '0');
                inRun = true;
            }
            else if (inRun)
            {
                tokens.Add(current);
                inRun = false;
            }
        }
        if (inRun) tokens.Add(current);
        return tokens;
    }
}