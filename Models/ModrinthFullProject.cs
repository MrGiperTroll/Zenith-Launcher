using System;
using System.Collections.Generic;

namespace CustomMcLauncher.Models;

/// <summary>Full Modrinth project body returned by GET /v2/project/{id}.</summary>
public class ModrinthFullProject
{
    public string Id { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Body { get; init; } = "";
    public string ProjectType { get; init; } = "";
    public string Author { get; init; } = "";
    public string AuthorAvatarUrl { get; init; } = "";
    public long Downloads { get; init; }
    public long Follows { get; init; }
    public string IconUrl { get; init; } = "";
    public DateTime DateModified { get; init; }
    public DateTime DateCreated { get; init; }

    // Links
    public string SourceUrl { get; init; } = "";
    public string IssuesUrl { get; init; } = "";
    public string WikiUrl { get; init; } = "";
    public string DiscordUrl { get; init; } = "";

    // License
    public string LicenseId { get; init; } = "";
    public string LicenseName { get; init; } = "";

    // Compatibility
    public string[] GameVersions { get; init; } = Array.Empty<string>();
    public string[] Loaders { get; init; } = Array.Empty<string>();
    public string[] Categories { get; init; } = Array.Empty<string>();

    // Client/server side
    public string ClientSide { get; init; } = "";
    public string ServerSide { get; init; } = "";

    // Moderators
    public List<ModrinthTeamMember> TeamMembers { get; init; } = new();
    public List<ModrinthProjectVersion> Versions { get; init; } = new();
}

public class ModrinthTeamMember
{
    public string Username { get; init; } = "";
    public string AvatarUrl { get; init; } = "";
    public string Role { get; init; } = "";
}

/// <summary>Single version of a Modrinth project returned by GET /v2/project/{id}/version.</summary>
public class ModrinthProjectVersion
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string VersionNumber { get; init; } = "";
    public string Changelog { get; init; } = "";
    public string VersionType { get; init; } = "release"; // release, beta, alpha
    public DateTime DatePublished { get; init; }
    public string[] GameVersions { get; init; } = Array.Empty<string>();
    public string[] Loaders { get; init; } = Array.Empty<string>();
    public List<ModrinthVersionFile> Files { get; init; } = new();
    public string PrimaryFileName =>
        Files.Count > 0 ? Files[0].Filename : "";
    public long PrimaryFileSize =>
        Files.Count > 0 ? Files[0].FileSize : 0;
    public string FileSizeLabel
    {
        get
        {
            var s = PrimaryFileSize;
            return s >= 1_048_576 ? $"{s / 1_048_576.0:0.#} MB"
                 : s >= 1024 ? $"{s / 1024.0:0.#} KB"
                 : $"{s} B";
        }
    }
    public string VersionTypeDisplay =>
        VersionType.ToLowerInvariant() switch
        {
            "release" => "Release",
            "beta" => "Beta",
            "alpha" => "Alpha",
            _ => VersionType
        };
    public string GameVersionsLabel => string.Join(", ", GameVersions);
    public string LoadersLabel => string.Join(", ", Loaders);
    public string DateLabel =>
        DatePublished == default ? "" : DatePublished.ToString("dd.MM.yyyy");
}

public class ModrinthVersionFile
{
    public string Url { get; init; } = "";
    public string Filename { get; init; } = "";
    public long FileSize { get; init; } = 0;
    public bool IsPrimary { get; init; }
}
