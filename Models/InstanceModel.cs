using System;
using System.IO;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CustomMcLauncher.Models;

public partial class InstanceModel : ObservableObject
{
    [JsonIgnore]
    public string DisplayVersion => string.IsNullOrEmpty(LoaderType) || LoaderType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)
        ? Version
        : string.IsNullOrEmpty(LoaderVersion) ? $"{Version} {LoaderType}" : $"{Version} {LoaderType} {LoaderVersion}";
    [ObservableProperty] private string _id = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _name = "New Profile";
    [ObservableProperty] private string _version = "1.20.1";
    [ObservableProperty] private string _loaderType = "Vanilla";
    [ObservableProperty] private string _loaderVersion = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private int _maxMemoryMb = 4096;
    [ObservableProperty] private string? _iconPath;

    // Per-instance overrides (null = use global launcher settings)
    [ObservableProperty] private int? _gameWidth;
    [ObservableProperty] private int? _gameHeight;
    [ObservableProperty] private bool? _isFullscreen;
    [ObservableProperty] private string? _javaMode;
    [ObservableProperty] private string? _customJavaPath;
    [ObservableProperty] private string? _jvmArgs;
    [ObservableProperty] private int? _ramMb;

    // Logs tab display preferences
    [ObservableProperty] private bool _logsFollowOutput = true;
    [ObservableProperty] private bool _logsWrapLines = true;
    [ObservableProperty] private bool _logsShowSystemMessages = true;
    [ObservableProperty] private bool _logsFullJava;

    // Playtime tracking
    [ObservableProperty] private long _playtimeSeconds;
    [ObservableProperty] private DateTime? _lastPlayed;

    [JsonIgnore]
    public string PlaytimeDisplay
    {
        get
        {
            if (PlaytimeSeconds <= 0) return "Not played yet";
            var t = TimeSpan.FromSeconds(PlaytimeSeconds);
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m {t.Seconds}s";
            return $"{t.Seconds}s";
        }
    }

    [JsonIgnore]
    public string LastPlayedDisplay =>
        LastPlayed.HasValue ? LastPlayed.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "Never";

    [JsonIgnore]
    [ObservableProperty]
    private double _launchProgressPercent;

    [JsonIgnore]
    private Bitmap? _cachedIcon;

    [JsonIgnore]
    public Bitmap? IconBitmap
    {
        get
        {
            if (!string.IsNullOrEmpty(IconPath) && File.Exists(IconPath))
            {
                if (_cachedIcon == null)
                {
                    try { _cachedIcon = new Bitmap(IconPath); } catch { return null; }
                }
                return _cachedIcon;
            }
            return null;
        }
    }

    [JsonIgnore]
    [ObservableProperty]
    private bool _isRunning;

    [JsonIgnore]
    [ObservableProperty]
    private bool _isLaunching;

    [JsonIgnore]
    [ObservableProperty]
    private bool _isEditingName;

    [JsonIgnore]
    [ObservableProperty]
    private bool _isSelected;

    partial void OnIconPathChanged(string? value)
    {
        _cachedIcon?.Dispose();
        _cachedIcon = null;
        OnPropertyChanged(nameof(IconBitmap));
    }
}
