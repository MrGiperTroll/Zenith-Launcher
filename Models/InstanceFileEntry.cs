using System;
using System.IO;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
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

    [JsonIgnore]
    private Bitmap? _previewImage;

    [JsonIgnore]
    public Bitmap? PreviewImage => _previewImage;

    [JsonIgnore]
    public bool HasPreview => _previewImage != null;

    public string DisplayName =>
        IsEnabled ? (IsDirectory ? Name.TrimEnd(Path.DirectorySeparatorChar) : Name)
                  : (Path.GetExtension(Name).Equals(".disabled", StringComparison.OrdinalIgnoreCase)
                      ? Name[..^9]
                      : Name + " (disabled)");

    public void SetPreview(byte[]? bytes, int decodeWidth = 64, bool allowDirectory = false)
    {
        _ = decodeWidth;
        _previewImage?.Dispose();
        _previewImage = null;

        if (bytes is { Length: > 0 } && (!IsDirectory || allowDirectory))
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                _previewImage = new Bitmap(ms);
            }
            catch
            {
                _previewImage = null;
            }
        }

        OnPropertyChanged(nameof(PreviewImage));
        OnPropertyChanged(nameof(HasPreview));
    }
}