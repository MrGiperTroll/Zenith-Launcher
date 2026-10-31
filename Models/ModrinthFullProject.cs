using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CustomMcLauncher.ViewModels;

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

    // Moderators & Gallery
    public List<ModrinthTeamMember> TeamMembers { get; init; } = new();
    public List<ModrinthProjectVersion> Versions { get; init; } = new();
    public List<ModrinthGalleryItem> Gallery { get; init; } = new();

    public string? FeaturedImageUrl =>
        Gallery.FirstOrDefault(g => g.Featured)?.Url ?? Gallery.FirstOrDefault()?.Url;

    public string? HeaderImageUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Body))
            {
                var match = Regex.Match(Body, @"!\[.*?\]\((https?://\S+?\.(?:png|jpg|jpeg|gif|webp|svg))\)");
                if (match.Success) return match.Groups[1].Value;

                var htmlMatch = Regex.Match(Body, @"<img\s+[^>]*?src=[""'](https?://\S+?\.(?:png|jpg|jpeg|gif|webp|svg))[""']", RegexOptions.IgnoreCase);
                if (htmlMatch.Success) return htmlMatch.Groups[1].Value;
            }
            return FeaturedImageUrl;
        }
    }
}

public partial class ModrinthGalleryItem : ObservableObject
{
    public string Url { get; init; } = "";
    public bool Featured { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public DateTime Created { get; init; }
    public string CreatedDisplay => Created == default ? "" : Created.ToString("MMMM dd, yyyy", System.Globalization.CultureInfo.InvariantCulture);

    [ObservableProperty]
    private Bitmap? _image;
    public bool HasImage => Image != null;

    public void LoadImage()
    {
        if (string.IsNullOrWhiteSpace(Url) || Image != null) return;
        System.Threading.Tasks.Task.Run(async () =>
        {
            var bytes = await Services.ModrinthApiService.GetIconAsync(Url);
            if (bytes is { Length: > 0 })
            {
                using var ms = new System.IO.MemoryStream(bytes);
                var bmp = Bitmap.DecodeToWidth(ms, 600);
                Dispatcher.UIThread.Post(() =>
                {
                    Image = bmp;
                    OnPropertyChanged(nameof(HasImage));
                });
            }
        });
    }
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
    public string CleanChangelog =>
        string.IsNullOrWhiteSpace(Changelog)
            ? "No changelog provided for this version."
            : Common.Utils.MarkdownSanitizer.Clean(Changelog);
    public bool HasChangelog => !string.IsNullOrWhiteSpace(Changelog);

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
