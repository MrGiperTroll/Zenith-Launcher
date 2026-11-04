using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.Models;

public partial class ModrinthUserProfile : ObservableObject, IDisposable
{
    public string Id { get; init; } = "";
    public string Username { get; init; } = "";
    public string Name { get; init; } = "";
    public string Bio { get; init; } = "";
    public string AvatarUrl { get; init; } = "";
    public DateTime Created { get; init; }
    public string Role { get; init; } = "developer";

    [ObservableProperty]
    private Bitmap? _avatarBitmap;

    public bool HasAvatar => AvatarBitmap != null;

    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : Username;

    public string WebsiteUrl
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Bio)) return "";
            var trimmed = Bio.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var spaceIdx = trimmed.IndexOfAny(new[] { ' ', '\r', '\n', '\t' });
                return spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;
            }
            return "";
        }
    }

    public string JoinedDisplay
    {
        get
        {
            if (Created == default) return "Joined recently";
            var span = DateTime.UtcNow - Created;
            if (span.TotalDays >= 365)
            {
                var years = (int)(span.TotalDays / 365);
                return years == 1 ? "Joined 1 year ago" : $"Joined {years} years ago";
            }
            if (span.TotalDays >= 30)
            {
                var months = (int)(span.TotalDays / 30);
                return months == 1 ? "Joined 1 month ago" : $"Joined {months} months ago";
            }
            return "Joined recently";
        }
    }

    public void LoadAvatar()
    {
        if (string.IsNullOrWhiteSpace(AvatarUrl) || AvatarBitmap != null) return;
        Task.Run(async () =>
        {
            try
            {
                var bytes = await ModrinthApiService.GetIconAsync(AvatarUrl);
                if (bytes is { Length: > 0 })
                {
                    using var ms = new MemoryStream(bytes);
                    var bmp = Bitmap.DecodeToWidth(ms, 96, BitmapInterpolationMode.MediumQuality);
                    Dispatcher.UIThread.Post(() =>
                    {
                        var old = AvatarBitmap;
                        AvatarBitmap = bmp;
                        try { old?.Dispose(); } catch { }
                        OnPropertyChanged(nameof(HasAvatar));
                    });
                }
            }
            catch { }
        });
    }

    public void Dispose()
    {
        try { AvatarBitmap?.Dispose(); } catch { }
        AvatarBitmap = null;
        OnPropertyChanged(nameof(HasAvatar));
    }
}
