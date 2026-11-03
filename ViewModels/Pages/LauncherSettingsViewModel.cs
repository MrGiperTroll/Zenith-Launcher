using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;
using CustomMcLauncher.UI.Theming;
using CustomMcLauncher.ViewModels.Common;

namespace CustomMcLauncher.ViewModels.Pages;

public partial class LauncherSettingsViewModel : ViewModelBase
{
    private bool _isLoadingConfig;

    public ThemeSettingsViewModel ThemeSettingsVm { get; }

    public Action? SaveConfigRequested { get; set; }
    public Action? VersionFiltersChanged { get; set; }
    public Action<string>? LanguageChanged { get; set; }
    public Action<bool>? DiscordRpcChanged { get; set; }
    public Action<double>? ScaleChanged { get; set; }
    public Action? OpenJavaInstallerRequested { get; set; }
    public Func<int, IProgress<double>?, Task<bool>>? DownloadJavaRequested { get; set; }
    public Func<string>? GetRecommendedJavaDisplay { get; set; }
    public Func<string>? GetWindowTitle { get; set; }

    public string WindowTitle => GetWindowTitle?.Invoke() ?? "Zenith Launcher";

    // --- Search & Category Filtering ---
    [ObservableProperty] private string _settingsSearchQuery = string.Empty;

    partial void OnSettingsSearchQueryChanged(string value) => UpdateSearchVisibility();

    [ObservableProperty] private bool _isSearchMemoryVisible = true;
    [ObservableProperty] private bool _isSearchResolutionVisible = true;
    [ObservableProperty] private bool _isSearchFullscreenVisible = true;
    [ObservableProperty] private bool _isSearchJavaVisible = true;
    [ObservableProperty] private bool _isSearchDirectoryVisible = true;
    [ObservableProperty] private bool _isSearchVersionFiltersVisible = true;
    [ObservableProperty] private bool _isSearchJvmArgsVisible = true;
    [ObservableProperty] private bool _isSearchLanguageVisible = true;
    [ObservableProperty] private bool _isSearchDiscordVisible = true;
    [ObservableProperty] private bool _isSearchAppearanceVisible = true;

    private void UpdateSearchVisibility()
    {
        var q = SettingsSearchQuery?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(q))
        {
            IsSearchMemoryVisible = IsSearchResolutionVisible = IsSearchFullscreenVisible = IsSearchJavaVisible = true;
            IsSearchDirectoryVisible = IsSearchVersionFiltersVisible = IsSearchJvmArgsVisible = true;
            IsSearchLanguageVisible = IsSearchDiscordVisible = IsSearchAppearanceVisible = true;
            return;
        }

        IsSearchMemoryVisible = "memory ram allocated память выделить".Contains(q);
        IsSearchResolutionVisible = "resolution window size разрешение экран".Contains(q);
        IsSearchFullscreenVisible = "fullscreen mode полноэкранный".Contains(q);
        IsSearchJavaVisible = "java runtime manage automatic download джава".Contains(q);
        IsSearchDirectoryVisible = "directory strategy isolation директория папка".Contains(q);
        IsSearchVersionFiltersVisible = "version filters releases snapshots версии фильтры".Contains(q);
        IsSearchJvmArgsVisible = "jvm arguments flags аргументы jvm".Contains(q);
        IsSearchLanguageVisible = "language localization english russian язык".Contains(q);
        IsSearchDiscordVisible = "discord rich presence rpc дискорд статус".Contains(q);
        IsSearchAppearanceVisible = "appearance theme color accent name branding внешний вид тема цвет акцент оформление launcher лаунчер настройки".Contains(q);
    }

    // --- Tabs ---
    [ObservableProperty] private string _currentSettingsTab = "Game";
    public bool IsSettingsTabGame => CurrentSettingsTab == "Game";
    public bool IsSettingsTabJava => CurrentSettingsTab == "Java";
    public bool IsSettingsTabGeneral => CurrentSettingsTab is "General" or "Launcher";
    public bool IsSettingsTabAppearance => CurrentSettingsTab == "Appearance";
    public bool IsSettingsTabTheme => false;
    public bool IsSettingsTabLauncher => IsSettingsTabGeneral;

    [RelayCommand]
    public void SwitchSettingsTab(string tab)
    {
        CurrentSettingsTab = tab;
        OnPropertyChanged(nameof(IsSettingsTabGame));
        OnPropertyChanged(nameof(IsSettingsTabJava));
        OnPropertyChanged(nameof(IsSettingsTabGeneral));
        OnPropertyChanged(nameof(IsSettingsTabAppearance));
        OnPropertyChanged(nameof(IsSettingsTabTheme));
        OnPropertyChanged(nameof(IsSettingsTabLauncher));
    }

    // --- RAM Configuration ---
    [ObservableProperty] private int _minRamMb = 512;
    [ObservableProperty] private int _ramMb = 4096;
    [ObservableProperty] private int _maxRamMb = 16384;

    partial void OnMinRamMbChanged(int value)
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnRamMbChanged(int value)
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    [RelayCommand]
    private void StepMinRamDown()
    {
        MinRamMb = Math.Clamp(MinRamMb - 512, 512, MaxRamMb);
    }

    [RelayCommand]
    private void StepMinRamUp()
    {
        MinRamMb = Math.Clamp(MinRamMb + 512, 512, MaxRamMb);
    }

    [RelayCommand]
    private void StepRamDown()
    {
        RamMb = Math.Clamp(RamMb - 512, 512, MaxRamMb);
    }

    [RelayCommand]
    private void StepRamUp()
    {
        RamMb = Math.Clamp(RamMb + 512, 512, MaxRamMb);
    }

    // --- Resolution & Display ---
    [ObservableProperty] private int _gameWidth = 1280;
    [ObservableProperty] private int _gameHeight = 720;
    [ObservableProperty] private bool _isLockAspectRatio = false;
    [ObservableProperty] private bool _isFullscreen = false;

    partial void OnGameWidthChanged(int value)
    {
        if (value < 800) { GameWidth = 800; return; }
        if (value > 7680) { GameWidth = 7680; return; }
        if (IsLockAspectRatio && value > 0)
            GameHeight = value * 9 / 16;
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnGameHeightChanged(int value)
    {
        if (value < 600) { GameHeight = 600; return; }
        if (value > 4320) { GameHeight = 4320; return; }
        if (IsLockAspectRatio && value > 0)
            GameWidth = value * 16 / 9;
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnIsFullscreenChanged(bool value)
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    // --- Java Runtime ---
    [ObservableProperty]
    private ObservableCollection<string> _javaModes = new() { "Recommended", "System", "Custom" };

    [ObservableProperty] private string _selectedJavaMode = "Recommended";
    [ObservableProperty] private string _currentJavaDisplay = "Recommended (Adoptium)";
    [ObservableProperty] private string _effectiveJavaPath = "";
    [ObservableProperty] private string _customJavaPath = string.Empty;
    [ObservableProperty] private string _resolvedJavaPath = string.Empty;
    [ObservableProperty] private bool _autoManageJava = true;
    [ObservableProperty] private bool _isEffectiveJavaPathInvalid = false;
    [ObservableProperty] private bool _isCustomJavaMode = false;

    partial void OnSelectedJavaModeChanged(string value)
    {
        UpdateJavaDisplay();
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnCustomJavaPathChanged(string value)
    {
        UpdateJavaDisplay();
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnAutoManageJavaChanged(bool value)
    {
        LauncherLog.Info($"Automatic Java management {(value ? "enabled" : "disabled")}.");
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnEffectiveJavaPathChanged(string value)
    {
        IsEffectiveJavaPathInvalid = !string.IsNullOrWhiteSpace(value) && value != "java" && !File.Exists(value);
        if (!_isLoadingConfig && SelectedJavaMode == "Custom" && value != CustomJavaPath)
        {
            CustomJavaPath = value;
        }
    }

    
    // --- Java Installations (Versions 25, 21, 17, 8) ---
    [ObservableProperty] private int _downloadingMajor;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadStatusText = string.Empty;

    public bool IsJava25Downloading => DownloadingMajor == 25;
    public bool IsJava21Downloading => DownloadingMajor == 21;
    public bool IsJava17Downloading => DownloadingMajor == 17;
    public bool IsJava8Downloading => DownloadingMajor == 8;

    [ObservableProperty] private string _java25Location = string.Empty;
    [ObservableProperty] private bool _isJava25Valid;
    public string Java25DownloadButtonText => IsJava25Downloading
        ? (DownloadProgress > 0 ? $"Downloading {DownloadProgress:P0}..." : "Downloading...")
        : (IsJava25Valid ? "Redownload" : "Download");
    partial void OnJava25LocationChanged(string value)
    {
        IsJava25Valid = !string.IsNullOrWhiteSpace(value) && File.Exists(value);
        OnPropertyChanged(nameof(Java25DownloadButtonText));
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }
    partial void OnIsJava25ValidChanged(bool value) => OnPropertyChanged(nameof(Java25DownloadButtonText));

    [ObservableProperty] private string _java21Location = string.Empty;
    [ObservableProperty] private bool _isJava21Valid;
    public string Java21DownloadButtonText => IsJava21Downloading
        ? (DownloadProgress > 0 ? $"Downloading {DownloadProgress:P0}..." : "Downloading...")
        : (IsJava21Valid ? "Redownload" : "Download");
    partial void OnJava21LocationChanged(string value)
    {
        IsJava21Valid = !string.IsNullOrWhiteSpace(value) && File.Exists(value);
        OnPropertyChanged(nameof(Java21DownloadButtonText));
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }
    partial void OnIsJava21ValidChanged(bool value) => OnPropertyChanged(nameof(Java21DownloadButtonText));

    [ObservableProperty] private string _java17Location = string.Empty;
    [ObservableProperty] private bool _isJava17Valid;
    public string Java17DownloadButtonText => IsJava17Downloading
        ? (DownloadProgress > 0 ? $"Downloading {DownloadProgress:P0}..." : "Downloading...")
        : (IsJava17Valid ? "Redownload" : "Download");
    partial void OnJava17LocationChanged(string value)
    {
        IsJava17Valid = !string.IsNullOrWhiteSpace(value) && File.Exists(value);
        OnPropertyChanged(nameof(Java17DownloadButtonText));
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }
    partial void OnIsJava17ValidChanged(bool value) => OnPropertyChanged(nameof(Java17DownloadButtonText));

    [ObservableProperty] private string _java8Location = string.Empty;
    [ObservableProperty] private bool _isJava8Valid;
    public string Java8DownloadButtonText => IsJava8Downloading
        ? (DownloadProgress > 0 ? $"Downloading {DownloadProgress:P0}..." : "Downloading...")
        : (IsJava8Valid ? "Redownload" : "Download");
    partial void OnJava8LocationChanged(string value)
    {
        IsJava8Valid = !string.IsNullOrWhiteSpace(value) && File.Exists(value);
        OnPropertyChanged(nameof(Java8DownloadButtonText));
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }
    partial void OnIsJava8ValidChanged(bool value) => OnPropertyChanged(nameof(Java8DownloadButtonText));

    private void NotifyDownloadState()
    {
        OnPropertyChanged(nameof(IsJava25Downloading));
        OnPropertyChanged(nameof(IsJava21Downloading));
        OnPropertyChanged(nameof(IsJava17Downloading));
        OnPropertyChanged(nameof(IsJava8Downloading));
        OnPropertyChanged(nameof(Java25DownloadButtonText));
        OnPropertyChanged(nameof(Java21DownloadButtonText));
        OnPropertyChanged(nameof(Java17DownloadButtonText));
        OnPropertyChanged(nameof(Java8DownloadButtonText));
    }

    [RelayCommand]
    public void DetectJava(string versionStr)
    {
        if (int.TryParse(versionStr, out int major))
        {
            var detected = JavaVersionHelper.FindJavaForVersion(major);
            if (!string.IsNullOrWhiteSpace(detected))
            {
                SetJavaLocation(major, detected);
            }
        }
    }

    [RelayCommand]
    public async Task InstallJavaAsync(string versionStr)
    {
        if (!int.TryParse(versionStr, out int major)) return;
        if (DownloadingMajor != 0) return;

        DownloadingMajor = major;
        DownloadProgress = 0;
        DownloadStatusText = $"Downloading Java {major}...";
        NotifyDownloadState();

        var progress = new Progress<double>(p =>
        {
            DownloadProgress = p;
            DownloadStatusText = $"Downloading Java {major}... {p:P0}";
            NotifyDownloadState();
        });

        try
        {
            if (DownloadJavaRequested != null)
            {
                await DownloadJavaRequested(major, progress);
                var detected = JavaVersionHelper.FindJavaForVersion(major);
                if (!string.IsNullOrWhiteSpace(detected))
                {
                    SetJavaLocation(major, detected);
                }
            }
            else
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(JavaVersionHelper.AdoptiumDownloadUrl(major))
                    {
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
        finally
        {
            DownloadingMajor = 0;
            DownloadProgress = 0;
            DownloadStatusText = string.Empty;
            NotifyDownloadState();
        }
    }

    [RelayCommand]
    public async Task BrowseJavaAsync(string versionStr)
    {
        if (!int.TryParse(versionStr, out int major)) return;
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider != null)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Select Java {major} Executable (javaw.exe / java)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Java Executable") { Patterns = new[] { "javaw.exe", "java.exe", "java" } },
                    FilePickerFileTypes.All
                }
            });

            if (files.Count > 0)
            {
                SetJavaLocation(major, files[0].Path.LocalPath);
            }
        }
    }

    public void SetJavaLocation(int major, string path)
    {
        switch (major)
        {
            case 25: Java25Location = path; break;
            case 21: Java21Location = path; break;
            case 17: Java17Location = path; break;
            case 8: Java8Location = path; break;
        }
        SaveConfigRequested?.Invoke();
    }

    public void UpdateJavaDisplay()
    {
        IsCustomJavaMode = SelectedJavaMode == "Custom";

        switch (SelectedJavaMode)
        {
            case "Custom":
                CurrentJavaDisplay = JavaVersionHelper.CustomDisplay(CustomJavaPath);
                ResolvedJavaPath = CustomJavaPath;
                EffectiveJavaPath = CustomJavaPath;
                break;

            case "System":
                CurrentJavaDisplay = L10n.T("java_display_system_path");
                ResolvedJavaPath = JavaVersionHelper.FindSystemJavaPath() ?? "";
                EffectiveJavaPath = ResolvedJavaPath;
                break;

            default: // Recommended
                var display = GetRecommendedJavaDisplay?.Invoke();
                CurrentJavaDisplay = display ?? "Recommended (Auto)";
                EffectiveJavaPath = "";
                break;
        }
    }

    [RelayCommand]
    private async Task BrowseJavaPathAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider != null)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Java Executable (javaw.exe / java)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Java Executable") { Patterns = new[] { "javaw.exe", "java.exe", "java" } },
                    FilePickerFileTypes.All
                }
            });

            if (files.Count > 0)
            {
                var chosen = files[0].Path.LocalPath;
                SelectedJavaMode = "Custom";
                CustomJavaPath = chosen;
                EffectiveJavaPath = chosen;
                SaveConfigRequested?.Invoke();
            }
        }
    }

    [RelayCommand]
    private void OpenJavaInstaller()
    {
        OpenJavaInstallerRequested?.Invoke();
    }

    // --- JVM Arguments ---
    [ObservableProperty] private string _jvmArgs = string.Empty;

    partial void OnJvmArgsChanged(string value)
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    // --- Launch Behavior ---
    public ObservableCollection<string> LaunchBehaviors { get; } =
        new(new[] { "KeepOpen", "HideAndReopen", "CloseOnLaunch" });

    [ObservableProperty] private string _selectedLaunchBehavior = "KeepOpen";

    partial void OnSelectedLaunchBehaviorChanged(string value)
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    // --- Directory Strategy ---
    [ObservableProperty]
    private ObservableCollection<string> _directoryModes = new()
    {
        "Separate directory for each instance",
        "Family",
        "Do not use separate directories"
    };

    [ObservableProperty]
    private string _selectedDirectoryMode = "Separate directory for each instance";

    partial void OnSelectedDirectoryModeChanged(string value)
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    // --- Version Filters ---
    [ObservableProperty] private bool _showReleases = true;
    [ObservableProperty] private bool _showSnapshots = false;
    [ObservableProperty] private bool _showBetas = false;
    [ObservableProperty] private bool _showAlphas = false;
    [ObservableProperty] private bool _useFlatVersionList = false;

    partial void OnShowReleasesChanged(bool value)
    {
        if (!_isLoadingConfig) { VersionFiltersChanged?.Invoke(); SaveConfigRequested?.Invoke(); }
    }

    partial void OnShowSnapshotsChanged(bool value)
    {
        if (!_isLoadingConfig) { VersionFiltersChanged?.Invoke(); SaveConfigRequested?.Invoke(); }
    }

    partial void OnShowBetasChanged(bool value)
    {
        if (!_isLoadingConfig) { VersionFiltersChanged?.Invoke(); SaveConfigRequested?.Invoke(); }
    }

    partial void OnShowAlphasChanged(bool value)
    {
        if (!_isLoadingConfig) { VersionFiltersChanged?.Invoke(); SaveConfigRequested?.Invoke(); }
    }

    partial void OnUseFlatVersionListChanged(bool value)
    {
        if (!_isLoadingConfig) { VersionFiltersChanged?.Invoke(); SaveConfigRequested?.Invoke(); }
    }

    // --- Language ---
    [ObservableProperty] private string _selectedLanguage = "English";

    public string[] LanguageOptions => L10n.LanguageDisplayNames;

    partial void OnSelectedLanguageChanged(string value)
    {
        if (!_isLoadingConfig)
        {
            LanguageChanged?.Invoke(value);
            JavaModes = new ObservableCollection<string>(new[] { "Recommended", "System", "Custom" });
            DirectoryModes = new ObservableCollection<string>(new[] { "Separate directory for each instance", "Family", "Do not use separate directories" });
            UpdateJavaDisplay();
            SaveConfigRequested?.Invoke();
        }
    }

    // --- Discord RPC & Scale ---
    [ObservableProperty] private bool _discordRpcEnabled = true;
    [ObservableProperty] private double _launcherScale = 1.0;
    [ObservableProperty] private double _pendingLauncherScale = 1.0;
    [ObservableProperty] private string _curseForgeApiKey = string.Empty;

    partial void OnCurseForgeApiKeyChanged(string value)
    {
        if (!_isLoadingConfig)
        {
            SaveConfigRequested?.Invoke();
        }
    }

    public void CommitLauncherScale()
    {
        if (Math.Abs(LauncherScale - PendingLauncherScale) > 0.001)
        {
            LauncherScale = PendingLauncherScale;
        }
    }

    partial void OnDiscordRpcEnabledChanged(bool value)
    {
        if (!_isLoadingConfig)
        {
            DiscordRpcChanged?.Invoke(value);
            SaveConfigRequested?.Invoke();
        }
    }

    partial void OnLauncherScaleChanged(double value)
    {
        if (!_isLoadingConfig)
        {
            ScaleChanged?.Invoke(value);
            SaveConfigRequested?.Invoke();
        }
    }

    // --- Appearance & Theming ---
    [ObservableProperty] private string _selectedThemeCardId = "purple";
    [ObservableProperty] private bool _isCustomThemeActive = false;
    [ObservableProperty] private string _customAccentHex = "#8B5CF6";
    [ObservableProperty] private IBrush _customAccentBrush = new SolidColorBrush(Color.Parse("#8B5CF6"));
    [ObservableProperty] private string _selectedThemeMode = "Dark";
    [ObservableProperty] private ThemeModeItem? _selectedThemeModeItem;
    [ObservableProperty] private string _selectedFontStyle = "Sans";

    public ObservableCollection<ThemeCardModel> ThemeCards { get; } = new();
    public ObservableCollection<ThemeModeItem> ThemeModes { get; } = new();
    public string[] FontStyleOptions { get; } = { "Sans", "Serif", "Monospace", "System Default" };

    public void InitAppearanceModels()
    {
        ThemeCards.Clear();
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "purple",
            Name = "Zenith",
            AccentHex = "#8B5CF6",
            AccentBrush = new SolidColorBrush(Color.Parse("#8B5CF6")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 139, 92, 246)),
            IsSelected = true
        });
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "blue",
            Name = "Cobalt",
            AccentHex = "#3B82F6",
            AccentBrush = new SolidColorBrush(Color.Parse("#3B82F6")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 59, 130, 246))
        });
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "green",
            Name = "Emerald",
            AccentHex = "#10B981",
            AccentBrush = new SolidColorBrush(Color.Parse("#10B981")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 16, 185, 129))
        });
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "red",
            Name = "Crimson",
            AccentHex = "#EF4444",
            AccentBrush = new SolidColorBrush(Color.Parse("#EF4444")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 239, 68, 68))
        });
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "orange",
            Name = "Amber",
            AccentHex = "#F97316",
            AccentBrush = new SolidColorBrush(Color.Parse("#F97316")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 249, 115, 22))
        });
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "custom",
            Name = "Custom",
            AccentHex = "",
            IsCustom = true,
            AccentBrush = new SolidColorBrush(Color.Parse("#A855F7")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 168, 85, 247))
        });

        RefreshThemeModes();
    }

    public void RefreshThemeModes()
    {
        ThemeModes.Clear();
        ThemeModes.Add(new ThemeModeItem
        {
            Id = "System",
            Name = L10n.CurrentLanguage == "ru" ? "Системная" : "System",
            IconData = "M20 3H4c-1.1 0-2 .9-2 2v11c0 1.1.9 2 2 2h6l-2 2v1h8v-1l-2-2h6c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 13H4V5h16v11z",
            IsSelected = string.Equals(SelectedThemeMode, "System", StringComparison.OrdinalIgnoreCase)
        });
        ThemeModes.Add(new ThemeModeItem
        {
            Id = "Light",
            Name = L10n.CurrentLanguage == "ru" ? "Светлая" : "Light",
            IconData = "M12 7c-2.76 0-5 2.24-5 5s2.24 5 5 5 5-2.24 5-5-2.24-5-5-5zM2 13h2c.55 0 1-.45 1-1s-.45-1-1-1H2c-.55 0-1 .45-1 1s.45 1 1 1zm18 0h2c.55 0 1-.45 1-1s-.45-1-1-1h-2c-.55 0-1 .45-1 1s.45 1 1 1zM11 2v2c0 .55.45 1 1 1s1-.45 1-1V2c0-.55-.45-1-1-1s-1 .45-1 1zm0 18v2c0 .55.45 1 1 1s1-.45 1-1v-2c0-.55-.45-1-1-1s-1 .45-1 1zM5.99 4.58a.996.996 0 0 0-1.41 0 .996.996 0 0 0 0 1.41l1.06 1.06c.39.39 1.03.39 1.41 0s.39-1.03 0-1.41L5.99 4.58zm12.37 12.37a.996.996 0 0 0-1.41 0 .996.996 0 0 0 0 1.41l1.06 1.06c.39.39 1.03.39 1.41 0s.39-1.03 0-1.41l-1.06-1.06zm1.06-10.96a.996.996 0 0 0 0-1.41.996.996 0 0 0-1.41 0l-1.06 1.06c-.39.39-.39 1.03 0 1.41s1.03.39 1.41 0l1.06-1.06zM7.05 18.36a.996.996 0 0 0 0-1.41.996.996 0 0 0-1.41 0l-1.06 1.06c-.39.39-.39 1.03 0 1.41s1.03.39 1.41 0l1.06-1.06z",
            IsSelected = string.Equals(SelectedThemeMode, "Light", StringComparison.OrdinalIgnoreCase)
        });
        ThemeModes.Add(new ThemeModeItem
        {
            Id = "Dark",
            Name = L10n.CurrentLanguage == "ru" ? "Тёмная" : "Dark",
            IconData = "M12.3 2a10 10 0 0 0-1.9 20 10 10 0 0 0 8.6-5 8 8 0 0 1-10.7-10.7A10 10 0 0 0 12.3 2z",
            IsSelected = string.Equals(SelectedThemeMode, "Dark", StringComparison.OrdinalIgnoreCase)
        });

        SelectedThemeModeItem = ThemeModes.FirstOrDefault(m => string.Equals(m.Id, SelectedThemeMode, StringComparison.OrdinalIgnoreCase)) ?? ThemeModes.LastOrDefault();
    }

    [RelayCommand]
    public void SelectThemeCard(string cardId)
    {
        SelectedThemeCardId = cardId;
        foreach (var card in ThemeCards)
        {
            card.IsSelected = (card.Id == cardId);
        }
        IsCustomThemeActive = (cardId == "custom");

        ApplyThemeSelection();
    }

    [RelayCommand]
    public void PickQuickCustomColor(string hex)
    {
        CustomAccentHex = hex;
    }

    partial void OnCustomAccentHexChanged(string value)
    {
        if (Color.TryParse(value, out var c))
        {
            CustomAccentBrush = new SolidColorBrush(c);
            var customCard = ThemeCards.FirstOrDefault(x => x.Id == "custom");
            if (customCard != null)
            {
                customCard.AccentBrush = CustomAccentBrush;
                customCard.SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, c.R, c.G, c.B));
            }
            if (IsCustomThemeActive && !_isLoadingConfig)
            {
                ApplyThemeSelection();
            }
        }
    }

    partial void OnSelectedThemeModeItemChanged(ThemeModeItem? value)
    {
        if (value == null) return;
        if (!string.Equals(SelectedThemeMode, value.Id, StringComparison.OrdinalIgnoreCase))
        {
            SelectedThemeMode = value.Id;
            foreach (var mode in ThemeModes)
                mode.IsSelected = string.Equals(mode.Id, value.Id, StringComparison.OrdinalIgnoreCase);

            if (!_isLoadingConfig)
            {
                ApplyThemeSelection();
            }
        }
    }

    partial void OnSelectedFontStyleChanged(string value)
    {
        ZenithTheme.ApplyFont(value);
        if (!_isLoadingConfig)
        {
            SaveConfigRequested?.Invoke();
        }
    }

    private void ApplyThemeSelection()
    {
        if (_isLoadingConfig) return;

        string accent = SelectedThemeCardId switch
        {
            "purple" => "#8B5CF6",
            "blue" => "#3B82F6",
            "green" => "#10B981",
            "red" => "#EF4444",
            "orange" => "#F97316",
            "custom" => CustomAccentHex,
            _ => "#8B5CF6"
        };

        var harmonized = ZenithTheme.GenerateHarmonizedPalette(accent, SelectedThemeMode);
        harmonized.ThemeMode = SelectedThemeMode;
        harmonized.FontStyle = SelectedFontStyle;

        ZenithTheme.Apply(harmonized);
        ThemeSettingsVm.SetSimpleThemePreview(accent);

        SaveConfigRequested?.Invoke();
    }

    public LauncherSettingsViewModel(ThemeSettingsViewModel themeSettingsVm)
    {
        ThemeSettingsVm = themeSettingsVm;
        InitAppearanceModels();
        L10n.LanguageChanged += () => RefreshThemeModes();
    }

    public void LoadFromConfig(LauncherConfigData cfg)
    {
        _isLoadingConfig = true;
        try
        {
            if (cfg.MinRamMb > 0) MinRamMb = cfg.MinRamMb;
            if (cfg.RamMb > 0) RamMb = cfg.RamMb;
            if (cfg.GameWidth > 0) GameWidth = cfg.GameWidth;
            if (cfg.GameHeight > 0) GameHeight = cfg.GameHeight;
            IsFullscreen = cfg.IsFullscreen;
            if (!string.IsNullOrWhiteSpace(cfg.JvmArgs)) JvmArgs = cfg.JvmArgs;
            if (!string.IsNullOrWhiteSpace(cfg.LaunchBehavior)) SelectedLaunchBehavior = cfg.LaunchBehavior;
            if (!string.IsNullOrWhiteSpace(cfg.SelectedDirectoryMode)) SelectedDirectoryMode = cfg.SelectedDirectoryMode;
            if (!string.IsNullOrWhiteSpace(cfg.JavaMode)) SelectedJavaMode = cfg.JavaMode;
            if (!string.IsNullOrWhiteSpace(cfg.CustomJavaPath)) CustomJavaPath = cfg.CustomJavaPath;
            UseFlatVersionList = cfg.UseFlatVersionList;
            AutoManageJava = cfg.AutoManageJava;
            DiscordRpcEnabled = cfg.DiscordRpcEnabled;
            if (cfg.LauncherScale > 0.4 && cfg.LauncherScale < 3.0) { LauncherScale = cfg.LauncherScale; PendingLauncherScale = cfg.LauncherScale; }

            if (!string.IsNullOrWhiteSpace(cfg.Java25Path) && File.Exists(cfg.Java25Path) && (JavaVersionHelper.GetJavaMajor(cfg.Java25Path) == 25 || JavaVersionHelper.GetJavaMajor(cfg.Java25Path) == 0)) Java25Location = cfg.Java25Path;
            else { var d25 = JavaVersionHelper.FindJavaForVersion(25); if (!string.IsNullOrWhiteSpace(d25)) Java25Location = d25; }

            if (!string.IsNullOrWhiteSpace(cfg.Java21Path) && File.Exists(cfg.Java21Path) && (JavaVersionHelper.GetJavaMajor(cfg.Java21Path) == 21 || JavaVersionHelper.GetJavaMajor(cfg.Java21Path) == 0)) Java21Location = cfg.Java21Path;
            else { var d21 = JavaVersionHelper.FindJavaForVersion(21); if (!string.IsNullOrWhiteSpace(d21)) Java21Location = d21; }

            if (!string.IsNullOrWhiteSpace(cfg.Java17Path) && File.Exists(cfg.Java17Path) && (JavaVersionHelper.GetJavaMajor(cfg.Java17Path) == 17 || JavaVersionHelper.GetJavaMajor(cfg.Java17Path) == 0)) Java17Location = cfg.Java17Path;
            else { var d17 = JavaVersionHelper.FindJavaForVersion(17); if (!string.IsNullOrWhiteSpace(d17)) Java17Location = d17; }

            if (!string.IsNullOrWhiteSpace(cfg.Java8Path) && File.Exists(cfg.Java8Path) && (JavaVersionHelper.GetJavaMajor(cfg.Java8Path) == 8 || JavaVersionHelper.GetJavaMajor(cfg.Java8Path) == 0)) Java8Location = cfg.Java8Path;
            else { var d8 = JavaVersionHelper.FindJavaForVersion(8); if (!string.IsNullOrWhiteSpace(d8)) Java8Location = d8; }

            if (!string.IsNullOrWhiteSpace(cfg.Language))
                SelectedLanguage = cfg.Language;
            if (!string.IsNullOrWhiteSpace(cfg.CurseForgeApiKey))
                CurseForgeApiKey = cfg.CurseForgeApiKey;

            SelectedThemeMode = string.IsNullOrWhiteSpace(cfg.ThemeMode) ? "Dark" : cfg.ThemeMode;
            SelectedFontStyle = string.IsNullOrWhiteSpace(cfg.FontStyle) ? "Sans" : cfg.FontStyle;

            var currentAccent = !string.IsNullOrWhiteSpace(cfg.AccentColor)
                ? cfg.AccentColor
                : (!string.IsNullOrWhiteSpace(cfg.SimpleAccentColor) ? cfg.SimpleAccentColor : "#8B5CF6");

            CustomAccentHex = currentAccent;
            if (Color.TryParse(currentAccent, out var cColor))
            {
                CustomAccentBrush = new SolidColorBrush(cColor);
                var customCard = ThemeCards.FirstOrDefault(x => x.Id == "custom");
                if (customCard != null)
                {
                    customCard.AccentBrush = CustomAccentBrush;
                    customCard.SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, cColor.R, cColor.G, cColor.B));
                }
            }

            string matchedId = currentAccent.ToUpperInvariant() switch
            {
                "#8B5CF6" or "#7C3AED" => "purple",
                "#3B82F6" or "#2563EB" => "blue",
                "#10B981" or "#059669" => "green",
                "#EF4444" or "#DC2626" => "red",
                "#F97316" or "#EA580C" => "orange",
                _ => "custom"
            };

            SelectedThemeCardId = matchedId;
            IsCustomThemeActive = (matchedId == "custom");
            foreach (var card in ThemeCards)
                card.IsSelected = (card.Id == matchedId);

            RefreshThemeModes();

            UpdateJavaDisplay();
        }
        finally
        {
            _isLoadingConfig = false;
        }
    }

    public void ApplyToConfig(LauncherConfigData cfg)
    {
        cfg.MinRamMb = MinRamMb;
        cfg.RamMb = RamMb;
        cfg.GameWidth = GameWidth;
        cfg.GameHeight = GameHeight;
        cfg.IsFullscreen = IsFullscreen;
        cfg.JvmArgs = JvmArgs;
        cfg.LaunchBehavior = SelectedLaunchBehavior;
        cfg.SelectedDirectoryMode = SelectedDirectoryMode;
        cfg.JavaMode = SelectedJavaMode;
        cfg.CustomJavaPath = CustomJavaPath;
        cfg.UseFlatVersionList = UseFlatVersionList;
        cfg.AutoManageJava = AutoManageJava;
        cfg.DiscordRpcEnabled = DiscordRpcEnabled;
        cfg.CurseForgeApiKey = CurseForgeApiKey;
        cfg.LauncherScale = LauncherScale;
        
        cfg.Java25Path = Java25Location;
        cfg.Java21Path = Java21Location;
        cfg.Java17Path = Java17Location;
        cfg.Java8Path = Java8Location;
    
        cfg.Language = SelectedLanguage;

        cfg.ThemeMode = SelectedThemeMode;
        cfg.FontStyle = SelectedFontStyle;
        cfg.AccentColor = SelectedThemeCardId switch
        {
            "purple" => "#8B5CF6",
            "blue" => "#3B82F6",
            "green" => "#10B981",
            "red" => "#EF4444",
            "orange" => "#F97316",
            "custom" => CustomAccentHex,
            _ => "#8B5CF6"
        };
        cfg.SimpleAccentColor = cfg.AccentColor;
    }
}

public class ThemeCardModel : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string AccentHex { get; set; } = "";
    public IBrush AccentBrush { get; set; } = Brushes.Transparent;
    public IBrush SoftBrush { get; set; } = Brushes.Transparent;
    public bool IsCustom { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public class ThemeModeItem : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string IconData { get; set; } = "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
