using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Services;

public sealed class ModrinthSearchPage
{
    public List<ModrinthProject> Hits { get; }
    public int Offset { get; }
    public int Limit { get; }
    public int TotalHits { get; }

    public ModrinthSearchPage(List<ModrinthProject> hits, int offset, int limit, int totalHits)
    {
        Hits = hits;
        Offset = offset;
        Limit = limit;
        TotalHits = totalHits;
    }
}

public sealed class ModrinthDependency
{
    public string ProjectId { get; init; } = "";
    public string VersionId { get; init; } = "";
    public string DependencyType { get; init; } = "";
}

    public sealed class ModrinthVersionDownload
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string VersionNumber { get; init; } = "";
    public string FileName { get; init; } = "";
    public string FileUrl { get; init; } = "";
    public long FileSize { get; init; }
    public string[] GameVersions { get; init; } = Array.Empty<string>();
    public string[] Loaders { get; init; } = Array.Empty<string>();
    public List<ModrinthDependency> Dependencies { get; init; } = new();
}

/// <summary>Minimal Modrinth API v2 client: search with exact game-version/loader facets, version lookup and downloads.</summary>
public static class ModrinthApiService
{
    private const string ApiBase = "https://api.modrinth.com/v2";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(45),
        DefaultRequestHeaders =
        {
            { "User-Agent", "ZenithLauncher/1.0 (ZenithMGT)" },
            { "Accept", "application/json" }
        }
    };

    public static async Task<ModrinthSearchPage?> SearchAsync(
        string query, string gameVersion, IReadOnlyList<string> loaderFacets, int offset, int limit = 20)
        => await SearchAsync(query, gameVersion, loaderFacets, null, "relevance", offset, limit);

    public static async Task<ModrinthSearchPage?> SearchAsync(
        string query, string gameVersion, IReadOnlyList<string> loaderFacets, string? projectType, string sortIndex, int offset, int limit = 20)
        => await SearchAsync(query, gameVersion, loaderFacets, projectType, sortIndex, Array.Empty<string>(), offset, limit);

    public static async Task<ModrinthSearchPage?> SearchAsync(
        string query, string gameVersion, IReadOnlyList<string> loaderFacets, string? projectType, string sortIndex, IReadOnlyList<string> categoryTags, int offset, int limit = 20)
    {
        var facets = new List<string[]>();
        if (!string.IsNullOrWhiteSpace(gameVersion))
            facets.Add(new[] { $"versions:{gameVersion}" });
        foreach (var l in loaderFacets)
            facets.Add(new[] { $"categories:{l}" });
        if (!string.IsNullOrWhiteSpace(projectType))
            facets.Add(new[] { $"project_type:{projectType}" });
        foreach (var t in categoryTags)
            if (!string.IsNullOrWhiteSpace(t))
                facets.Add(new[] { $"categories:{t.Trim().ToLowerInvariant()}" });

        var url = $"{ApiBase}/search?limit={limit}&offset={offset}&index={Uri.EscapeDataString(sortIndex)}";
        if (!string.IsNullOrWhiteSpace(query))
            url += "&query=" + Uri.EscapeDataString(query.Trim());
        url += "&facets=" + Uri.EscapeDataString(JsonSerializer.Serialize(facets));

        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var hits = new List<ModrinthProject>();
        if (root.TryGetProperty("hits", out var h) && h.ValueKind == JsonValueKind.Array)
        {
            foreach (var hit in h.EnumerateArray())
            {
                var cats = GetStringArray(hit, "categories");
                var loaders = cats.Where(c => c is "fabric" or "forge" or "neoforged" or "quilt" or "iris" or "optifine").ToArray();
                var otherCats = cats.Where(c => c is not "fabric" and not "forge" and not "neoforged" and not "quilt" and not "iris" and not "optifine").ToArray();
                var updatedStr = GetString(hit, "date_modified");
                DateTime.TryParse(updatedStr, out var updated);
                hits.Add(new ModrinthProject
                {
                    ProjectId = GetString(hit, "project_id") ?? "",
                    Slug = GetString(hit, "slug") ?? "",
                    Title = GetString(hit, "title") ?? GetString(hit, "slug") ?? "",
                    Author = GetString(hit, "author") ?? "",
                    Description = GetString(hit, "description") ?? "",
                    Downloads = GetLong(hit, "downloads"),
                    IconUrl = GetString(hit, "icon_url") ?? "",
                    GameVersions = GetStringArray(hit, "versions"),
                    Categories = otherCats,
                    Loaders = loaders,
                    Updated = updated,
                    ProjectType = GetString(hit, "project_type") ?? "mod"
                });
            }
        }

        var total = GetInt(root, "total_hits");
        return new ModrinthSearchPage(hits, offset, limit, total);
    }

    /// <summary>Finds the newest version of a project that matches the exact game version and loader(s).</summary>
    public static async Task<ModrinthVersionDownload?> GetLatestVersionAsync(
        string projectId, string gameVersion, IReadOnlyList<string> loaderFacets)
    {
        var url = $"{ApiBase}/project/{Uri.EscapeDataString(projectId)}/version";
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameVersion))
            queryParts.Add($"game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion }))}");
        if (loaderFacets.Count > 0)
            queryParts.Add($"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(loaderFacets))}");
        if (queryParts.Count > 0)
            url += "?" + string.Join("&", queryParts);

        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        foreach (var v in doc.RootElement.EnumerateArray())
        {
            if (!v.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) continue;

            JsonElement chosen = default;
            var found = false;
            foreach (var f in files.EnumerateArray().Take(8))
            {
                if (f.TryGetProperty("primary", out var pr) && pr.ValueKind == JsonValueKind.True)
                {
                    chosen = f;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                foreach (var f in files.EnumerateArray().Take(8))
                {
                    chosen = f;
                    found = true;
                    break;
                }
            }
            if (!found) continue;

            var fileName = GetString(chosen, "filename");
            var fileUrl = GetString(chosen, "url");
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(fileUrl)) continue;

            var deps = new List<ModrinthDependency>();
            if (v.TryGetProperty("dependencies", out var depsEl) && depsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in depsEl.EnumerateArray())
                {
                    var type = GetString(d, "dependency_type") ?? "";
                    var pid = GetString(d, "project_id") ?? "";
                    var vid = GetString(d, "version_id") ?? "";
                    if (!string.IsNullOrWhiteSpace(pid))
                        deps.Add(new ModrinthDependency { ProjectId = pid, VersionId = vid, DependencyType = type });
                }
            }

            var gameVers = GetStringArray(v, "game_versions");
            var loaders = GetStringArray(v, "loaders");

            return new ModrinthVersionDownload
            {
                Id = GetString(v, "id") ?? "",
                Name = GetString(v, "name") ?? "",
                VersionNumber = GetString(v, "version_number") ?? "",
                FileName = fileName,
                FileUrl = fileUrl,
                FileSize = GetLong(chosen, "size"),
                GameVersions = gameVers,
                Loaders = loaders,
                Dependencies = deps
            };
        }

        return null;
    }

    public static async Task<byte[]?> DownloadAsync(string url)
    {
        try { return await Http.GetByteArrayAsync(url); }
        catch { return null; }
    }

    public static async Task<byte[]?> GetIconAsync(string url)
    {
        try { return await Http.GetByteArrayAsync(url); }
        catch { return null; }
    }

    public static async Task<ModrinthFullProject?> GetFullProjectAsync(string projectId)
    {
        try
        {
            var json = await Http.GetStringAsync($"{ApiBase}/project/{Uri.EscapeDataString(projectId)}");
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            // Get team members for creators section
            var team = new List<ModrinthTeamMember>();
            try
            {
                var teamJson = await Http.GetStringAsync($"{ApiBase}/project/{Uri.EscapeDataString(projectId)}/members");
                using var teamDoc = JsonDocument.Parse(teamJson);
                foreach (var m in teamDoc.RootElement.EnumerateArray())
                {
                    var username = GetString(m, "username") ?? "";
                    var role = GetString(m, "role") ?? "";
                    // Get avatar from user object if available
                    var avatar = "";
                    if (m.TryGetProperty("avatar_url", out var av)) avatar = av.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(username))
                        team.Add(new ModrinthTeamMember { Username = username, Role = role, AvatarUrl = avatar });
                }
            }
            catch { }

            // License
            var licenseId = "";
            var licenseName = "";
            if (r.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.Object)
            {
                licenseId = GetString(lic, "id") ?? "";
                licenseName = GetString(lic, "name") ?? "";
            }

            return new ModrinthFullProject
            {
                Id = GetString(r, "id") ?? "",
                Slug = GetString(r, "slug") ?? "",
                Title = GetString(r, "title") ?? "",
                Description = GetString(r, "description") ?? "",
                Body = GetString(r, "body") ?? "",
                ProjectType = GetString(r, "project_type") ?? "",
                Downloads = GetLong(r, "downloads"),
                Follows = GetLong(r, "follows"),
                IconUrl = GetString(r, "icon_url") ?? "",
                SourceUrl = GetString(r, "source_url") ?? "",
                IssuesUrl = GetString(r, "issues_url") ?? "",
                WikiUrl = GetString(r, "wiki_url") ?? "",
                DiscordUrl = GetString(r, "discord_url") ?? "",
                LicenseId = licenseId,
                LicenseName = licenseName,
                GameVersions = GetStringArray(r, "game_versions"),
                Loaders = GetStringArray(r, "loaders"),
                Categories = GetStringArray(r, "categories"),
                ClientSide = GetString(r, "client_side") ?? "",
                ServerSide = GetString(r, "server_side") ?? "",
                DateModified = DateTime.TryParse(GetString(r, "date_modified"), out var dm) ? dm : default,
                DateCreated = DateTime.TryParse(GetString(r, "date_created"), out var dc) ? dc : default,
                TeamMembers = team,
            };
        }
        catch { return null; }
    }

    public static async Task<List<ModrinthProjectVersion>> GetProjectVersionsAsync(
        string projectId, string[]? gameVersions = null, string[]? loaders = null)
    {
        try
        {
            var url = $"{ApiBase}/project/{Uri.EscapeDataString(projectId)}/version";
            var parts = new List<string>();
            if (gameVersions is { Length: > 0 })
                parts.Add($"game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(gameVersions))}");
            if (loaders is { Length: > 0 })
                parts.Add($"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(loaders))}");
            if (parts.Count > 0) url += "?" + string.Join("&", parts);

            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var versions = new List<ModrinthProjectVersion>();

            foreach (var v in doc.RootElement.EnumerateArray())
            {
                var files = new List<ModrinthVersionFile>();
                if (v.TryGetProperty("files", out var filesArr))
                {
                    foreach (var f in filesArr.EnumerateArray())
                    {
                        files.Add(new ModrinthVersionFile
                        {
                            Url = GetString(f, "url") ?? "",
                            Filename = GetString(f, "filename") ?? "",
                            FileSize = GetLong(f, "size"),
                            IsPrimary = f.TryGetProperty("primary", out var pr) && pr.ValueKind == JsonValueKind.True,
                        });
                    }
                }

                versions.Add(new ModrinthProjectVersion
                {
                    Id = GetString(v, "id") ?? "",
                    Name = GetString(v, "name") ?? "",
                    VersionNumber = GetString(v, "version_number") ?? "",
                    Changelog = GetString(v, "changelog") ?? "",
                    VersionType = GetString(v, "version_type") ?? "release",
                    DatePublished = DateTime.TryParse(GetString(v, "date_published"), out var dp) ? dp : default,
                    GameVersions = GetStringArray(v, "game_versions"),
                    Loaders = GetStringArray(v, "loaders"),
                    Files = files,
                });
            }
            return versions;
        }
        catch { return new List<ModrinthProjectVersion>(); }
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int GetInt(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static long GetLong(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private static string[] GetStringArray(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? "");
        return list.ToArray();
    }
}