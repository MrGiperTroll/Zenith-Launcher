using System;
using System.IO;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ZenithLauncher.Models;

public partial class InstanceModel : ObservableObject
{
    [JsonIgnore]
    public string DisplayVersion => string.IsNullOrEmpty(LoaderType) || LoaderType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)
        ? Version
        : $"{Version} {LoaderType}";
    private string _id = Guid.NewGuid().ToString("N");
    public string Id { get => _id; set => SetProperty(ref _id, value); }

    private string _name = "New Profile";
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _version = "1.20.1";
    public string Version { get => _version; set => SetProperty(ref _version, value); }

    private string _loaderType = "Vanilla";
    public string LoaderType { get => _loaderType; set => SetProperty(ref _loaderType, value); }

    private string _loaderVersion = "";
    public string LoaderVersion { get => _loaderVersion; set => SetProperty(ref _loaderVersion, value); }

    private string _path = "";
    public string Path { get => _path; set => SetProperty(ref _path, value); }

    private int _maxMemoryMb = 4096;
    public int MaxMemoryMb { get => _maxMemoryMb; set => SetProperty(ref _maxMemoryMb, value); }

    private string? _iconPath;
    public string? IconPath
    {
        get => _iconPath;
        set
        {
            if (SetProperty(ref _iconPath, value))
                OnPropertyChanged(nameof(HasCustomIcon));
        }
    }

    private string _group = "";
    public string Group
    {
        get => _group;
        set
        {
            if (SetProperty(ref _group, value))
                OnPropertyChanged(nameof(HasGroup));
        }
    }

    [JsonIgnore]
    public bool HasGroup => !string.IsNullOrWhiteSpace(Group);

    // Modpack package tracking
    private string _modpackId = "";
    public string ModpackId { get => _modpackId; set => SetProperty(ref _modpackId, value); }

    private string _modpackVersion = "";
    public string ModpackVersion { get => _modpackVersion; set => SetProperty(ref _modpackVersion, value); }

    // Per-instance overrides (null = use global launcher settings)
    private int? _gameWidth;
    public int? GameWidth { get => _gameWidth; set => SetProperty(ref _gameWidth, value); }

    private int? _gameHeight;
    public int? GameHeight { get => _gameHeight; set => SetProperty(ref _gameHeight, value); }

    private bool? _isFullscreen;
    public bool? IsFullscreen { get => _isFullscreen; set => SetProperty(ref _isFullscreen, value); }

    private string? _javaMode;
    public string? JavaMode { get => _javaMode; set => SetProperty(ref _javaMode, value); }

    private string? _customJavaPath;
    public string? CustomJavaPath { get => _customJavaPath; set => SetProperty(ref _customJavaPath, value); }

    private string? _jvmArgs;
    public string? JvmArgs { get => _jvmArgs; set => SetProperty(ref _jvmArgs, value); }

    private int? _minRamMb;
    public int? MinRamMb { get => _minRamMb; set => SetProperty(ref _minRamMb, value); }

    private int? _ramMb;
    public int? RamMb { get => _ramMb; set => SetProperty(ref _ramMb, value); }

    // Logs tab display preferences
    private bool _logsFollowOutput = true;
    public bool LogsFollowOutput { get => _logsFollowOutput; set => SetProperty(ref _logsFollowOutput, value); }

    private bool _logsWrapLines = true;
    public bool LogsWrapLines { get => _logsWrapLines; set => SetProperty(ref _logsWrapLines, value); }

    private bool _logsShowSystemMessages = true;
    public bool LogsShowSystemMessages { get => _logsShowSystemMessages; set => SetProperty(ref _logsShowSystemMessages, value); }

    private bool _logsFullJava;
    public bool LogsFullJava { get => _logsFullJava; set => SetProperty(ref _logsFullJava, value); }

    // Playtime tracking
    private long _playtimeSeconds;
    public long PlaytimeSeconds { get => _playtimeSeconds; set => SetProperty(ref _playtimeSeconds, value); }

    private DateTime? _lastPlayed;
    public DateTime? LastPlayed { get => _lastPlayed; set => SetProperty(ref _lastPlayed, value); }

    private DateTime? _createdAt;
    public DateTime? CreatedAt { get => _createdAt; set => SetProperty(ref _createdAt, value); }

    [JsonIgnore]
    public string PlaytimeDisplay
    {
        get
        {
            if (PlaytimeSeconds <= 0) return Services.L10n.T("stat_not_played");
            var t = TimeSpan.FromSeconds(PlaytimeSeconds);
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m {t.Seconds}s";
            return $"{t.Seconds}s";
        }
    }

    [JsonIgnore]
    public string LastPlayedDisplay =>
        LastPlayed.HasValue ? LastPlayed.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : Services.L10n.T("stat_never");

    [JsonIgnore]
    public string CreatedAtDisplay =>
        CreatedAt.HasValue
            ? CreatedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
            : (System.IO.Directory.Exists(Path) ? System.IO.Directory.GetCreationTime(Path).ToString("dd.MM.yyyy HH:mm") : "—");

    [JsonIgnore]
    [ObservableProperty]
    private double _launchProgressPercent;

    [JsonIgnore]
    public bool HasCustomIcon
    {
        get
        {
            if (!string.IsNullOrEmpty(IconPath) && File.Exists(IconPath))
                return true;
            if (!string.IsNullOrEmpty(Path))
            {
                var localIcon = System.IO.Path.Combine(Path, "icon.png");
                if (File.Exists(localIcon))
                {
                    IconPath = localIcon;
                    return true;
                }
            }
            return false;
        }
    }

    [JsonIgnore]
    public bool HasModsSupport
    {
        get
        {
            var l = LoaderType?.Trim().ToLowerInvariant() ?? "";
            return l is "fabric" or "forge" or "neoforge" or "quilt";
        }
    }

    [JsonIgnore]
    public bool HasShaderSupport
    {
        get
        {
            var l = LoaderType?.Trim() ?? "";
            return !string.IsNullOrEmpty(l) && !l.Equals("Vanilla", StringComparison.OrdinalIgnoreCase);
        }
    }

    public void RefreshTimeDisplays()
    {
        OnPropertyChanged(nameof(PlaytimeDisplay));
        OnPropertyChanged(nameof(LastPlayedDisplay));
        OnPropertyChanged(nameof(CreatedAtDisplay));
    }


    [JsonIgnore]
    [ObservableProperty]
    private bool _isRunning;

    [JsonIgnore]
    [ObservableProperty]
    private bool _isLaunching;

    [JsonIgnore]
    [ObservableProperty]
    private bool _hasLaunchedInCurrentSession;

    [JsonIgnore]
    [ObservableProperty]
    private bool _isEditingName;

    [JsonIgnore]
    [ObservableProperty]
    private bool _isSelected;

    [JsonIgnore]
    [ObservableProperty]
    private bool _isDragging;
}

