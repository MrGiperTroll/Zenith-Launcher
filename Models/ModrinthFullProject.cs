using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace CustomMcLauncher.Models;

/// <summary>Full Modrinth project body returned by GET /v2/project/{id}.</summary>
public class ModrinthFullProject
{
    public static string FormatPlatformName(string? raw) => ModrinthProjectVersion.FormatPlatformName(raw);
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
    public string ClientSide { get; init; } = "client";
    public string ServerSide { get; init; } = "server";

    // Supported environments display
    public string ClientSideDisplay => ClientSide.ToLowerInvariant() switch
    {
        "required" => "Client-side (Required)",
        "optional" => "Client-side (Optional)",
        "unsupported" => "Client-side (Unsupported)",
        _ => "Client-side"
    };

    public string ServerSideDisplay => ServerSide.ToLowerInvariant() switch
    {
        "required" => "Server-side (Required)",
        "optional" => "Server-side (Optional)",
        "unsupported" => "Server-side (Unsupported)",
        _ => "Server-side"
    };

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

    public List<Common.Utils.MarkdownBlock> DescriptionBlocks => Common.Utils.MarkdownBlockParser.Parse(Body);
}

public partial class ModrinthGalleryItem : ObservableObject
{
    public string Url { get; init; } = "";
    public bool Featured { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";

    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title : (!string.IsNullOrWhiteSpace(Description) ? Description : "");
    public bool HasDisplayTitle => !string.IsNullOrWhiteSpace(DisplayTitle);
    public DateTime Created { get; init; }
    public string CreatedDisplay => Created == default ? "" : Created.ToString("MMMM dd, yyyy", System.Globalization.CultureInfo.InvariantCulture);

    [ObservableProperty]
    private Bitmap? _image;
    public bool HasImage => Image != null;

    private CancellationTokenSource? _animCts;

    public void LoadImage()
    {
        if (string.IsNullOrWhiteSpace(Url) || Image != null) return;
        Task.Run(async () =>
        {
            var bytes = await Services.ModrinthApiService.GetIconAsync(Url);
            if (bytes is { Length: > 0 })
            {
                // Try decoding animated GIF
                try
                {
                    using var stream = new MemoryStream(bytes);
                    using var codec = SKCodec.Create(stream);
                    if (codec != null && codec.FrameCount > 1)
                    {
                        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                        using var skBmp = new SKBitmap(info);
                        var frames = new List<(Bitmap bitmap, int durationMs)>();

                        int step = Math.Max(1, codec.FrameCount / 60);
                        for (int i = 0; i < codec.FrameCount; i += step)
                        {
                            var opts = new SKCodecOptions(i);
                            if (codec.GetPixels(info, skBmp.GetPixels(), opts) == SKCodecResult.Success)
                            {
                                using var img = SKImage.FromBitmap(skBmp);
                                using var data = img.Encode(SKEncodedImageFormat.Png, 85);
                                using var ms = new MemoryStream();
                                data.SaveTo(ms);
                                ms.Position = 0;
                                var bmp = new Bitmap(ms);
                                int dur = codec.FrameInfo[i].Duration;
                                if (dur <= 15) dur = 80;
                                frames.Add((bmp, dur));
                            }
                        }

                        if (frames.Count > 0)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                Image = frames[0].bitmap;
                                OnPropertyChanged(nameof(HasImage));
                            });

                            _animCts?.Cancel();
                            var cts = new CancellationTokenSource();
                            _animCts = cts;

                            _ = Task.Run(async () =>
                            {
                                int idx = 0;
                                while (!cts.Token.IsCancellationRequested && frames.Count > 1)
                                {
                                    try
                                    {
                                        await Task.Delay(frames[idx].durationMs, cts.Token);
                                        idx = (idx + 1) % frames.Count;
                                        var f = frames[idx].bitmap;
                                        Dispatcher.UIThread.Post(() => Image = f);
                                    }
                                    catch { break; }
                                }
                            }, cts.Token);
                            return;
                        }
                    }
                }
                catch { }

                // Fallback: standard static bitmap decode
                try
                {
                    using var ms = new MemoryStream(bytes);
                    var bmp = new Bitmap(ms);
                    Dispatcher.UIThread.Post(() =>
                    {
                        Image = bmp;
                        OnPropertyChanged(nameof(HasImage));
                    });
                }
                catch { }
            }
        });
    }

    public void StopAnimation()
    {
        _animCts?.Cancel();
    }
}

public partial class ModrinthTeamMember : ObservableObject
{
    public string Id { get; init; } = "";
    public string Username { get; init; } = "";
    public string AvatarUrl { get; init; } = "";
    public string Role { get; init; } = "";
    public bool IsOwner => string.Equals(Role, "Owner", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private Bitmap? _avatar;
    public bool HasAvatar => Avatar != null;

    public void LoadAvatar()
    {
        if (string.IsNullOrWhiteSpace(AvatarUrl) || Avatar != null) return;
        Task.Run(async () =>
        {
            var bytes = await Services.ModrinthApiService.GetIconAsync(AvatarUrl);
            if (bytes is { Length: > 0 })
            {
                using var ms = new MemoryStream(bytes);
                var bmp = Bitmap.DecodeToWidth(ms, 64);
                Dispatcher.UIThread.Post(() =>
                {
                    Avatar = bmp;
                    OnPropertyChanged(nameof(HasAvatar));
                });
            }
        });
    }
}

/// <summary>Single version of a Modrinth project returned by GET /v2/project/{id}/version.</summary>
public class ModrinthProjectVersion
{
    public string Id { get; init; } = "";
    public string ProjectId { get; init; } = "";
    public string MavenCoordinates => $"maven.modrinth:{(string.IsNullOrWhiteSpace(ProjectId) ? "project" : ProjectId)}:{Id}";
    public string GradleSnippet => 
$@"repositories {{
    exclusiveContent {{
        forRepository {{
            maven {{
                name = ""Modrinth""
                url = ""https://api.modrinth.com/maven""
            }}
        }}
        // forRepositories(fg.repository) // Uncomment when using ForgeGradle
        filter {{
            includeGroup ""maven.modrinth""
        }}
    }}
}}

// Standard Gradle dependency
dependencies {{
    implementation ""maven.modrinth:{(string.IsNullOrWhiteSpace(ProjectId) ? "project" : ProjectId)}:{Id}""
}}

// Legacy Loom dependency
dependencies {{
    modImplementation ""maven.modrinth:{(string.IsNullOrWhiteSpace(ProjectId) ? "project" : ProjectId)}:{Id}""
}}";
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
    public long Downloads { get; init; } = 0;
    public string AuthorId { get; init; } = "";
    public string AuthorName { get; set; } = "";

    public string DownloadsLabel
    {
        get
        {
            if (Downloads >= 1_000_000) return $"{Downloads / 1_000_000.0:0.##}M";
            if (Downloads >= 1_000) return $"{Downloads / 1000.0:0.#}K";
            return Downloads > 0 ? Downloads.ToString() : "—";
        }
    }
    public string[] GameVersions { get; init; } = Array.Empty<string>();
    public string[] Loaders { get; init; } = Array.Empty<string>();
    public List<ModrinthVersionFile> Files { get; init; } = new();
    public string PrimaryFileName =>
        Files.Count > 0 ? Files[0].Filename : "";
    public long PrimaryFileSize =>
        Files.Count > 0 ? Files[0].FileSize : 0;
    public static string FormatPlatformName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var trimmed = raw.Trim();
        if (trimmed.Equals("babric", StringComparison.OrdinalIgnoreCase)) return "Fabric";
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim();
        if (s.Equals("all platforms", StringComparison.OrdinalIgnoreCase)) return "All platforms";
        if (s.Equals("babric", StringComparison.OrdinalIgnoreCase)) return "Fabric";
        if (s.Equals("fabric", StringComparison.OrdinalIgnoreCase)) return "Fabric";
        if (s.Equals("forge", StringComparison.OrdinalIgnoreCase)) return "Forge";
        if (s.Equals("neoforge", StringComparison.OrdinalIgnoreCase)) return "NeoForge";
        if (s.Equals("quilt", StringComparison.OrdinalIgnoreCase)) return "Quilt";
        if (s.Equals("liteloader", StringComparison.OrdinalIgnoreCase)) return "LiteLoader";
        if (s.Equals("optifine", StringComparison.OrdinalIgnoreCase)) return "OptiFine";
        if (s.Equals("canvas", StringComparison.OrdinalIgnoreCase)) return "Canvas";
        if (s.Equals("iris", StringComparison.OrdinalIgnoreCase)) return "Iris";
        if (s.Length <= 1) return s.ToUpperInvariant();
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    public string PrimaryLoader => Loaders.Length > 0 ? FormatPlatformName(Loaders[0]) : "";
    public string PrimaryGameVersion => GameVersions.Length > 0 ? GameVersions[0] : "";

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

    public string VersionTypeInitial =>
        !string.IsNullOrEmpty(VersionType) ? VersionType[..1].ToUpperInvariant() : "R";

    public string GameVersionsLabel => string.Join(", ", GameVersions);
    public string LoadersLabel => string.Join(", ", Loaders);
    public string DateLabel =>
        DatePublished == default ? "" : DatePublished.ToString("dd.MM.yyyy");

    public string RelativeTime
    {
        get
        {
            if (DatePublished == default) return "";
            var span = DateTime.UtcNow - DatePublished.ToUniversalTime();
            if (span.TotalDays >= 365)
            {
                int yrs = Math.Max(1, (int)(span.TotalDays / 365));
                return $"{yrs} {(yrs == 1 ? "year" : "years")} ago";
            }
            if (span.TotalDays >= 30)
            {
                int mos = Math.Max(1, (int)(span.TotalDays / 30));
                return $"{mos} {(mos == 1 ? "month" : "months")} ago";
            }
            if (span.TotalDays >= 7)
            {
                int wks = Math.Max(1, (int)(span.TotalDays / 7));
                return $"{wks} {(wks == 1 ? "week" : "weeks")} ago";
            }
            if (span.TotalDays >= 1)
            {
                int days = Math.Max(1, (int)span.TotalDays);
                return $"{days} {(days == 1 ? "day" : "days")} ago";
            }
            return "today";
        }
    }
}

public class ModrinthVersionFile
{
    public string Url { get; init; } = "";
    public string Filename { get; init; } = "";
    public long FileSize { get; init; } = 0;
    public bool IsPrimary { get; init; }
}


public class PaginationPageItem
{
    public int PageNumber { get; init; }
    public string DisplayText { get; init; } = "";
    public bool IsActive { get; init; }
    public bool IsEllipsis { get; init; }
    public bool IsClickable => !IsActive && !IsEllipsis;
}

public partial class FilterCheckItem : ObservableObject
{
    public string Name { get; init; } = "";

    [ObservableProperty]
    private bool _isSelected;
}
