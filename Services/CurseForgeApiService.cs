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

    /// <summary>
    /// Base API URL. Can point directly to CurseForge ("https://api.curseforge.com/v1")
    /// or to a reverse proxy / Cloudflare Worker (e.g. "https://cf-curseforge-proxy.elijahsydorenko.workers.dev/v1")
    /// which automatically attaches the secret API key server-side.
    /// </summary>
    public static string ApiBaseUrl { get; set; } = "https://cf-curseforge-proxy.elijahsydorenko.workers.dev/v1";

    /// <summary>
    /// Default embedded API key for CurseForge if caller doesn't provide one.
    /// </summary>
    public static string DefaultApiKey { get; set; } = "";

    private static HttpRequestMessage CreateRequest(HttpMethod method, string pathAndQuery, string? callerApiKey)
    {
        var baseUri = ApiBaseUrl.TrimEnd('/');
        var url = $"{baseUri}/{pathAndQuery.TrimStart('/')}";
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Accept", "application/json");

        var key = !string.IsNullOrWhiteSpace(callerApiKey) ? callerApiKey.Trim() : DefaultApiKey?.Trim();
        if (!string.IsNullOrWhiteSpace(key))
        {
            request.Headers.Add("x-api-key", key);
        }

        return request;
    }

    private static bool IsDirectCurseForgeApi()
        => ApiBaseUrl.Contains("api.curseforge.com", StringComparison.OrdinalIgnoreCase);

    public static async Task<CurseForgeSearchResult?> SearchMapsAsync(
        string query,
        string? gameVersion,
        int offset,
        int pageSize,
        string? apiKey = null,
        CancellationToken token = default)
    {
        var effectiveKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey.Trim() : DefaultApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(effectiveKey) && IsDirectCurseForgeApi())
        {
            // If calling CurseForge API directly without any key, avoid throwing hard crashes
            return new CurseForgeSearchResult(new List<ModrinthProjectItemViewModel>(), 0, offset, pageSize);
        }

        var path = $"mods/search?gameId={MinecraftGameId}&classId={CustomMapsClassId}&index={offset}&pageSize={pageSize}&sortField=2&sortOrder=desc";

        if (!string.IsNullOrWhiteSpace(query))
            path += $"&searchFilter={Uri.EscapeDataString(query.Trim())}";

        if (!string.IsNullOrWhiteSpace(gameVersion) && !gameVersion.Equals("All versions", StringComparison.OrdinalIgnoreCase))
            path += $"&gameVersion={Uri.EscapeDataString(gameVersion.Trim())}";

        using var request = CreateRequest(HttpMethod.Get, path, apiKey);
        using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return new CurseForgeSearchResult(new List<ModrinthProjectItemViewModel>(), 0, offset, pageSize);
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
        string? apiKey = null,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return null;

        var path = $"mods/{projectId}/files?pageSize=10";
        if (!string.IsNullOrWhiteSpace(gameVersion) && !gameVersion.Equals("All versions", StringComparison.OrdinalIgnoreCase))
            path += $"&gameVersion={Uri.EscapeDataString(gameVersion.Trim())}";

        using var request = CreateRequest(HttpMethod.Get, path, apiKey);
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
        string? apiKey = null,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return null;

        var path = $"mods/{projectId}";
        using var request = CreateRequest(HttpMethod.Get, path, apiKey);

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
            var descPath = $"mods/{projectId}/description";
            using var descReq = CreateRequest(HttpMethod.Get, descPath, apiKey);
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
