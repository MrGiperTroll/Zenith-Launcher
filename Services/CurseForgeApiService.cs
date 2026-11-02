using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CustomMcLauncher.Models;
using CustomMcLauncher.ViewModels.Items;

namespace CustomMcLauncher.Services;

public static class CurseForgeApiService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    // Standard CurseForge Minecraft GameId
    public const int MinecraftGameId = 432;
    // CurseForge Worlds / Custom Maps classId
    public const int CustomMapsClassId = 17;

    public static async Task<CurseForgeSearchResult?> SearchMapsAsync(
        string query,
        string? gameVersion,
        int offset,
        int pageSize,
        string apiKey,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("CurseForge API key is required to browse Worlds & Maps from CurseForge. Set it in Settings or visit console.curseforge.com.");
        }

        var url = $"https://api.curseforge.com/v1/mods/search?gameId={MinecraftGameId}&classId={CustomMapsClassId}" +
                  $"&index={offset}&pageSize={pageSize}&sortField=2&sortOrder=desc";

        if (!string.IsNullOrWhiteSpace(query))
            url += $"&searchFilter={Uri.EscapeDataString(query.Trim())}";

        if (!string.IsNullOrWhiteSpace(gameVersion) && !gameVersion.Equals("All versions", StringComparison.OrdinalIgnoreCase))
            url += $"&gameVersion={Uri.EscapeDataString(gameVersion.Trim())}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey.Trim());
        request.Headers.Add("Accept", "application/json");

        using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Invalid or inactive CurseForge API key. Verify your key from console.curseforge.com.");
        }

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);

        var hits = new List<ModrinthProjectItemViewModel>();
        var root = doc.RootElement;

        var totalCount = 0;
        if (root.TryGetProperty("pagination", out var pag) && pag.TryGetProperty("totalCount", out var tc))
            totalCount = tc.GetInt32();

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idProp) ? idProp.GetInt64().ToString() : "";
                var slug = item.TryGetProperty("slug", out var slugProp) ? slugProp.GetString() ?? "" : "";
                var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                var summary = item.TryGetProperty("summary", out var sumProp) ? sumProp.GetString() ?? "" : "";
                var dl = item.TryGetProperty("downloadCount", out var dlProp) ? dlProp.GetInt64() : 0;

                var authorName = "";
                if (item.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array && authors.GetArrayLength() > 0)
                {
                    authorName = authors[0].TryGetProperty("name", out var aName) ? aName.GetString() ?? "" : "";
                }

                var iconUrl = "";
                if (item.TryGetProperty("logo", out var logo))
                {
                    iconUrl = logo.TryGetProperty("thumbnailUrl", out var thumb) ? thumb.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(iconUrl))
                        iconUrl = logo.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                }

                var cats = new List<string>();
                if (item.TryGetProperty("categories", out var catArr) && catArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in catArr.EnumerateArray())
                    {
                        if (c.TryGetProperty("name", out var cn) && cn.GetString() is { Length: > 0 } cns)
                            cats.Add(cns);
                    }
                }

                DateTime updated = default;
                if (item.TryGetProperty("dateModified", out var dateMod))
                    DateTime.TryParse(dateMod.GetString(), out updated);

                var versions = new List<string>();
                if (item.TryGetProperty("latestFilesIndexes", out var idxArr) && idxArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var idx in idxArr.EnumerateArray())
                    {
                        if (idx.TryGetProperty("gameVersion", out var gv) && gv.GetString() is { Length: > 0 } gvs)
                            versions.Add(gvs);
                    }
                }

                hits.Add(new ModrinthProjectItemViewModel
                {
                    ProjectId = id,
                    Slug = slug,
                    Title = name,
                    Author = authorName,
                    Description = summary,
                    Downloads = dl,
                    IconUrl = iconUrl,
                    GameVersions = versions.Distinct().ToArray(),
                    Categories = cats.ToArray(),
                    Updated = updated,
                    ProjectType = "world"
                });
            }
        }

        return new CurseForgeSearchResult(hits, totalCount, offset, pageSize);
    }

    public static async Task<CurseForgeFileInfo?> GetLatestMapFileAsync(
        string projectId,
        string? gameVersion,
        string apiKey,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(projectId)) return null;

        var url = $"https://api.curseforge.com/v1/mods/{projectId}/files?pageSize=10";
        if (!string.IsNullOrWhiteSpace(gameVersion) && !gameVersion.Equals("All versions", StringComparison.OrdinalIgnoreCase))
            url += $"&gameVersion={Uri.EscapeDataString(gameVersion.Trim())}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey.Trim());
        request.Headers.Add("Accept", "application/json");

        using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);

        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in data.EnumerateArray())
            {
                var fileId = f.TryGetProperty("id", out var fid) ? fid.GetInt64() : 0;
                var fileName = f.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
                var dlUrl = f.TryGetProperty("downloadUrl", out var du) ? du.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(dlUrl) && fileId > 0 && !string.IsNullOrWhiteSpace(fileName))
                {
                    dlUrl = $"https://edge.forgecdn.net/files/{fileId / 1000}/{fileId % 1000}/{Uri.EscapeDataString(fileName)}";
                }
                if (!string.IsNullOrWhiteSpace(dlUrl))
                {
                    return new CurseForgeFileInfo(fileId, fileName, dlUrl);
                }
            }
        }
        return null;
    }

    public static async Task<byte[]?> DownloadAsync(string url, CancellationToken token = default)
    {
        using var response = await Http.GetAsync(url, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
    }

    public static async Task<ModrinthFullProject?> GetFullProjectAsync(
        string projectId,
        string apiKey,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(projectId)) return null;

        var url = $"https://api.curseforge.com/v1/mods/{projectId}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey.Trim());
        request.Headers.Add("Accept", "application/json");

        using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("data", out var item)) return null;

        var id = item.TryGetProperty("id", out var idProp) ? idProp.GetInt64().ToString() : projectId;
        var slug = item.TryGetProperty("slug", out var slugProp) ? slugProp.GetString() ?? "" : "";
        var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
        var summary = item.TryGetProperty("summary", out var sumProp) ? sumProp.GetString() ?? "" : "";
        var dl = item.TryGetProperty("downloadCount", out var dlProp) ? dlProp.GetInt64() : 0;

        var authorName = "";
        if (item.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array && authors.GetArrayLength() > 0)
        {
            authorName = authors[0].TryGetProperty("name", out var aName) ? aName.GetString() ?? "" : "";
        }

        var iconUrl = "";
        if (item.TryGetProperty("logo", out var logo))
        {
            iconUrl = logo.TryGetProperty("thumbnailUrl", out var thumb) ? thumb.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(iconUrl))
                iconUrl = logo.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
        }

        var desc = summary;
        try
        {
            var descUrl = $"https://api.curseforge.com/v1/mods/{projectId}/description";
            using var descReq = new HttpRequestMessage(HttpMethod.Get, descUrl);
            descReq.Headers.Add("x-api-key", apiKey.Trim());
            using var descResp = await Http.SendAsync(descReq, token).ConfigureAwait(false);
            if (descResp.IsSuccessStatusCode)
            {
                using var dStream = await descResp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var dDoc = await JsonDocument.ParseAsync(dStream, cancellationToken: token).ConfigureAwait(false);
                if (dDoc.RootElement.TryGetProperty("data", out var dData) && dData.GetString() is { Length: > 0 } bodyHtml)
                {
                    desc = bodyHtml;
                }
            }
        }
        catch { }

        var versions = new List<string>();
        if (item.TryGetProperty("latestFilesIndexes", out var idxArr) && idxArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var idx in idxArr.EnumerateArray())
            {
                if (idx.TryGetProperty("gameVersion", out var gv) && gv.GetString() is { Length: > 0 } gvs)
                    versions.Add(gvs);
            }
        }

        return new ModrinthFullProject
        {
            Id = id,
            Slug = slug,
            Title = name,
            Description = summary,
            Body = desc,
            ProjectType = "world",
            Author = authorName,
            Downloads = dl,
            IconUrl = iconUrl,
            GameVersions = versions.Distinct().ToArray(),
            ClientSide = "client",
            ServerSide = "optional"
        };
    }
}

public record CurseForgeSearchResult(
    IReadOnlyList<ModrinthProjectItemViewModel> Hits,
    int TotalHits,
    int Offset,
    int Limit
);

public record CurseForgeFileInfo(long FileId, string FileName, string DownloadUrl);
