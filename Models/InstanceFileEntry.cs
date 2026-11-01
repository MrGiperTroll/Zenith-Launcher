using System;
using System.IO;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CustomMcLauncher.Models;

public enum InstanceFileKind
{
    Generic,
    Mod,
    ResourcePack,
    ShaderPack,
    World,
    Screenshot,
    Server,
    DataPack
}

public partial class InstanceFileEntry : ObservableObject
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsDirectory { get; init; }
    public DateTime ModifiedAt { get; init; } = DateTime.MinValue;
    public InstanceFileKind Kind { get; set; } = InstanceFileKind.Generic;

    [ObservableProperty]
    private bool _isEnabled = true;

    // Rich card metadata (populated by ContentMetadataReader / servers reader)
    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _subtitle = "";

    [ObservableProperty]
    private string _meta = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _modrinthProjectId = "";

    [JsonIgnore]
    [ObservableProperty]
    private byte[]? _previewBytes;

    [JsonIgnore]
    public bool HasPreview => PreviewBytes != null && PreviewBytes.Length > 0;

    public string DisplayName =>
        IsEnabled ? (IsDirectory ? Name.TrimEnd(Path.DirectorySeparatorChar) : Name)
                  : (Path.GetExtension(Name).Equals(".disabled", StringComparison.OrdinalIgnoreCase)
                      ? Name[..^9]
                      : Name + " (disabled)");

    public string GameModeDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Meta)) return "Survival mode";
            if (Meta.Contains("Creative", StringComparison.OrdinalIgnoreCase)) return "Creative mode";
            if (Meta.Contains("Survival", StringComparison.OrdinalIgnoreCase)) return "Survival mode";
            if (Meta.Contains("Hardcore", StringComparison.OrdinalIgnoreCase)) return "Hardcore mode";
            if (Meta.Contains("Adventure", StringComparison.OrdinalIgnoreCase)) return "Adventure mode";
            if (Meta.Contains("Spectator", StringComparison.OrdinalIgnoreCase)) return "Spectator mode";
            return $"{Meta} mode";
        }
    }

    public string RelativeTimeDisplay
    {
        get
        {
            DateTime dt = DateTime.MinValue;
            if (!string.IsNullOrWhiteSpace(Subtitle) && DateTime.TryParse(Subtitle, out var parsed))
                dt = parsed;
            else if (ModifiedAt != DateTime.MinValue)
                dt = ModifiedAt;

            if (dt == DateTime.MinValue) return "Recently";
            var diff = DateTime.Now - dt;
            if (diff.TotalSeconds < 0) return "Just now";
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 2) return "Yesterday";
            if (diff.TotalDays < 30) return $"{(int)diff.TotalDays} days ago";
            if (diff.TotalDays < 365) return $"{(int)(diff.TotalDays / 30)} mo. ago";
            return $"{(int)(diff.TotalDays / 365)} y. ago";
        }
    }

    public void SetPreview(byte[]? bytes, int decodeWidth = 64, bool allowDirectory = false)
    {
        _ = decodeWidth;
        if (bytes is { Length: > 0 } && (!IsDirectory || allowDirectory))
        {
            PreviewBytes = bytes;
        }
        else
        {
            PreviewBytes = null;
        }

        OnPropertyChanged(nameof(HasPreview));
    }
}