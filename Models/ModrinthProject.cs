using System;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.Models;

public partial class ModrinthProject : ObservableObject
{
    public string ProjectId { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public long Downloads { get; init; }
    public string IconUrl { get; init; } = "";
    public string[] GameVersions { get; init; } = Array.Empty<string>();
    public string[] Categories { get; init; } = Array.Empty<string>();
    public string[] Loaders { get; init; } = Array.Empty<string>();
    public DateTime Updated { get; init; }
    public string ProjectType { get; init; } = "mod";

    [ObservableProperty]
    private Bitmap? _icon;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string _installedVersion = "";

    public bool HasIcon => Icon != null;

    public string DownloadsLabel
    {
        get
        {
            var count = Downloads >= 1_000_000_000 ? $"{Downloads / 1e9:0.##}B"
                      : Downloads >= 1_000_000 ? $"{Downloads / 1e6:0.##}M"
                      : Downloads >= 1_000 ? $"{Downloads / 1e3:0.#}K"
                      : $"{Downloads}";
            return string.Format(L10n.T("mp_downloads_count"), count);
        }
    }

    public string VersionsLabel =>
        GameVersions.Length > 0
            ? $"MC {GameVersions[0]}{(GameVersions.Length > 1 ? $" +{GameVersions.Length - 1}" : "")}"
            : "";

    public string FormattedDate => Updated == default ? "" : Updated.ToString("dd.MM.yyyy");

    public string TagsLabel
    {
        get
        {
            var tags = new System.Collections.Generic.List<string>();
            if (Loaders.Length > 0) tags.AddRange(Loaders);
            foreach (var c in Categories)
            {
                if (c.Equals("fabric", System.StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("forge", System.StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("quilt", System.StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("neoforged", System.StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("fabric", System.StringComparison.OrdinalIgnoreCase)) continue;
                tags.Add(c);
                if (tags.Count >= 4) break;
            }
            return tags.Count == 0 ? "" : string.Join(" • ", tags);
        }
    }

    public string InstallButtonText =>
        IsInstalling ? L10n.T("mi_installing") : IsInstalled ? L10n.T("mi_installed") : L10n.T("mi_install");

    partial void OnIsInstallingChanged(bool value) => OnPropertyChanged(nameof(InstallButtonText));
    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(InstallButtonText));

    public void SetIcon(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return;
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);
            Dispatcher.UIThread.Post(() =>
            {
                Icon = bmp;
                OnPropertyChanged(nameof(HasIcon));
            });
        }
        catch { }
    }
}