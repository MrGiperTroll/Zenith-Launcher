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
    public bool IsSettingsTabGeneral => CurrentSettingsTab == "General";
    public bool IsSettingsTabAppearance => CurrentSettingsTab is "Appearance" or "Launcher";
    public bool IsSettingsTabTheme => CurrentSettingsTab == "Theme";
    public bool IsSettingsTabLauncher => IsSettingsTabAppearance;

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
            if (cfg.LauncherScale > 0.4 && cfg.LauncherScale < 3.0) LauncherScale = cfg.LauncherScale;

            if (!string.IsNullOrWhiteSpace(cfg.Language))
                SelectedLanguage = cfg.Language;

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
        cfg.LauncherScale = LauncherScale;
        cfg.Language = SelectedLanguage;
    }
}
