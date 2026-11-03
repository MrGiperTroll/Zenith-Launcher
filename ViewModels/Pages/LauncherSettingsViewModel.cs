using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;
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
    public bool IsSettingsTabTheme => CurrentSettingsTab == "Theme";
    public bool IsSettingsTabLauncher => IsSettingsTabGeneral;

    [RelayCommand]
    public void SwitchSettingsTab(string tab)
    {
        if (IsSettingsTabTheme && tab != "Theme" && ThemeSettingsVm.HasUnsavedThemeChanges())
        {
            ThemeSettingsVm.CloseThemeEditor();
            return;
        }

        CurrentSettingsTab = tab;
        OnPropertyChanged(nameof(IsSettingsTabGame));
        OnPropertyChanged(nameof(IsSettingsTabJava));
        OnPropertyChanged(nameof(IsSettingsTabGeneral));
        OnPropertyChanged(nameof(IsSettingsTabAppearance));
        OnPropertyChanged(nameof(IsSettingsTabTheme));
        OnPropertyChanged(nameof(IsSettingsTabLauncher));
    }

    [RelayCommand]
    public void OpenThemeEditor()
    {
        ThemeSettingsVm.SnapshotCurrentSavedTheme();
        SwitchSettingsTab("Theme");
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

    // --- Modals (forwarded from ThemeSettingsVm) ---
    public bool IsRestartConfirmOpen => ThemeSettingsVm.IsRestartConfirmOpen;
    public bool IsThemeDiscardConfirmOpen => ThemeSettingsVm.IsThemeDiscardConfirmOpen;

    [RelayCommand]
    private void CancelRestartConfirm() => ThemeSettingsVm.CancelRestartConfirm();

    [RelayCommand]
    private void ConfirmRestartAndApplyTheme() => ThemeSettingsVm.ConfirmRestartAndApplyTheme();

    [RelayCommand]
    private void CancelDiscardThemeChanges() => ThemeSettingsVm.CancelDiscardThemeChanges();

    [RelayCommand]
    private void ConfirmDiscardThemeChanges() => ThemeSettingsVm.ConfirmDiscardThemeChanges();

    public LauncherSettingsViewModel(ThemeSettingsViewModel themeSettingsVm)
    {
        ThemeSettingsVm = themeSettingsVm;
        ThemeSettingsVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ThemeSettingsViewModel.IsRestartConfirmOpen))
                OnPropertyChanged(nameof(IsRestartConfirmOpen));
            else if (e.PropertyName == nameof(ThemeSettingsViewModel.IsThemeDiscardConfirmOpen))
                OnPropertyChanged(nameof(IsThemeDiscardConfirmOpen));
        };
        ThemeSettingsVm.CloseRequested = () => SwitchSettingsTab("Appearance");
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
    }
}
