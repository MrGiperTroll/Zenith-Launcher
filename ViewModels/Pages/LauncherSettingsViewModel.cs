using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Models;
using ZenithLauncher.Services;
using ZenithLauncher.UI.Animations;
using ZenithLauncher.UI.Theming;
using ZenithLauncher.ViewModels.Common;

namespace ZenithLauncher.ViewModels.Pages;

public partial class LauncherSettingsViewModel : ViewModelBase
{
    private bool _isLoadingConfig;
    private bool _isSyncingHardwareAccel;

    public ThemeSettingsViewModel ThemeSettingsVm { get; }

    public Action? SaveConfigRequested { get; set; }
    public Action? ClearSnowAccumulationRequested { get; set; }
    public Action? VersionFiltersChanged { get; set; }
    public Action<string>? LanguageChanged { get; set; }
    public Action<bool>? DiscordRpcChanged { get; set; }
    public Action<double>? ScaleChanged { get; set; }
    public Action<bool>? HardwareAccelRestartRequested { get; set; }
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
        IsSearchAppearanceVisible = "appearance theme color accent name branding внешний вид тема цвет акцент оформление launcher лаунчер настройки scale масштаб интерфейс animations animation motion плавность анимации анимация анімації переходы particles snow leaves sakura image custom скролл прокрутка частицы снег листья сакура physics accumulation beta сугробы физика сніг".Contains(q);
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

    partial void OnIsLockAspectRatioChanged(bool value)
    {
        if (_isLoadingConfig) return;
        if (value && GameWidth > 0)
            GameHeight = Math.Clamp(GameWidth * 9 / 16, 600, 4320);
        SaveConfigRequested?.Invoke();
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
        new(new[] { "KeepOpen", "Hide", "Close" });

    [ObservableProperty] private string _selectedLaunchBehavior = "KeepOpen";

    partial void OnSelectedLaunchBehaviorChanged(string value)
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    // --- System & Tray & Hardware Accel ---
    [ObservableProperty] private bool _minimizeToTrayOnClose;
    [ObservableProperty] private bool _hardwareAcceleration = true;
    [ObservableProperty] private bool _uiAnimationsEnabled = true;
    [ObservableProperty] private bool _uiAnimPageTransitionsEnabled = true;
    [ObservableProperty] private bool _uiAnimTabsEnabled = true;
    [ObservableProperty] private bool _uiAnimModalsEnabled = true;
    [ObservableProperty] private bool _uiAnimSidebarsEnabled = true;
    [ObservableProperty] private bool _uiAnimPopupsEnabled = true;
    [ObservableProperty] private bool _smoothScrollEnabled = true;
    [ObservableProperty] private bool _isAnimationDetailsExpanded;
    [ObservableProperty] private string _cacheStatusText = string.Empty;
    [ObservableProperty] private bool _isClearingCache;

    // --- Background Particles (Theme Settings) ---
    [ObservableProperty] private string _particleType = "Off";
    [ObservableProperty] private string _particleLeafPalette = "Autumn";
    [ObservableProperty] private string _particleCustomImagePath = string.Empty;
    [ObservableProperty] private int _particleCount = 45;
    [ObservableProperty] private double _particleSpeed = 1.0;
    [ObservableProperty] private double _particleWind = 0.25;
    [ObservableProperty] private double _particleSize = 1.0;
    [ObservableProperty] private double _particleOpacity = 0.65;
    [ObservableProperty] private bool _snowPhysicsEnabled = false;
    [ObservableProperty] private bool _snowBottomAccumulationEnabled = false;
    [ObservableProperty] private bool _snowCursorInteractionEnabled = true;
    [ObservableProperty] private bool _snowPersistenceEnabled = true;
    [ObservableProperty] private double _snowAccumulationRate = 1.0;
    [ObservableProperty] private double _snowMaxHeight = 11.0;
    [ObservableProperty] private bool _isSnowPhysicsDetailsExpanded = false;

    public bool AreParticlesEnabled => !string.Equals(ParticleType, "Off", StringComparison.OrdinalIgnoreCase);
    public bool IsParticleOff => !AreParticlesEnabled;
    public bool IsParticleSnow => string.Equals(ParticleType, "Snow", StringComparison.OrdinalIgnoreCase);
    public bool IsParticleLeaves => string.Equals(ParticleType, "Leaves", StringComparison.OrdinalIgnoreCase);
    public bool IsParticleSakura => string.Equals(ParticleType, "Sakura", StringComparison.OrdinalIgnoreCase);
    public bool IsParticleCustom => string.Equals(ParticleType, "Custom", StringComparison.OrdinalIgnoreCase);

    public bool IsLeafPaletteSpring => string.Equals(ParticleLeafPalette, "Spring", StringComparison.OrdinalIgnoreCase);
    public bool IsLeafPaletteGolden => string.Equals(ParticleLeafPalette, "Golden", StringComparison.OrdinalIgnoreCase);
    public bool IsLeafPaletteAutumn => string.Equals(ParticleLeafPalette, "Autumn", StringComparison.OrdinalIgnoreCase);
    public bool IsLeafPaletteForest => string.Equals(ParticleLeafPalette, "Forest", StringComparison.OrdinalIgnoreCase);

    public bool HasParticleCustomImage => !string.IsNullOrWhiteSpace(ParticleCustomImagePath);
    public string ParticleCustomImageDisplayName => string.IsNullOrWhiteSpace(ParticleCustomImagePath)
        ? L10n.T("particles_image_none")
        : Path.GetFileName(ParticleCustomImagePath);

    public string ParticleCountText => ParticleCount.ToString("0");
    public string ParticleSpeedText => ParticleSpeed.ToString("0.0x", CultureInfo.InvariantCulture);
    public string ParticleWindText => (ParticleWind >= 0 ? "+" : "") + ParticleWind.ToString("0.0", CultureInfo.InvariantCulture);
    public string ParticleSizeText => ParticleSize.ToString("0.0x", CultureInfo.InvariantCulture);
    public string ParticleOpacityText => $"{Math.Round(ParticleOpacity * 100):0}%";
    public string SnowAccumulationRateText => SnowAccumulationRate.ToString("0.0x", CultureInfo.InvariantCulture);
    public string SnowMaxHeightText => $"{Math.Round(SnowMaxHeight):0} px";

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    private void SyncMotionConfiguration()
    {
        ZenithMotion.Configure(
            UiAnimationsEnabled,
            UiAnimPageTransitionsEnabled,
            UiAnimTabsEnabled,
            UiAnimModalsEnabled,
            UiAnimSidebarsEnabled,
            UiAnimPopupsEnabled,
            SmoothScrollEnabled);
    }

    partial void OnUiAnimationsEnabledChanged(bool value)
    {
        SyncMotionConfiguration();
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnUiAnimPageTransitionsEnabledChanged(bool value)
    {
        SyncMotionConfiguration();
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnUiAnimTabsEnabledChanged(bool value)
    {
        SyncMotionConfiguration();
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnUiAnimModalsEnabledChanged(bool value)
    {
        SyncMotionConfiguration();
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnUiAnimSidebarsEnabledChanged(bool value)
    {
        SyncMotionConfiguration();
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnUiAnimPopupsEnabledChanged(bool value)
    {
        SyncMotionConfiguration();
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnSmoothScrollEnabledChanged(bool value)
    {
        SyncMotionConfiguration();
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    [RelayCommand]
    private void ToggleAnimationDetails() => IsAnimationDetailsExpanded = !IsAnimationDetailsExpanded;

    partial void OnParticleTypeChanged(string value)
    {
        OnPropertyChanged(nameof(AreParticlesEnabled));
        OnPropertyChanged(nameof(IsParticleOff));
        OnPropertyChanged(nameof(IsParticleSnow));
        OnPropertyChanged(nameof(IsParticleLeaves));
        OnPropertyChanged(nameof(IsParticleSakura));
        OnPropertyChanged(nameof(IsParticleCustom));
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnParticleLeafPaletteChanged(string value)
    {
        OnPropertyChanged(nameof(IsLeafPaletteSpring));
        OnPropertyChanged(nameof(IsLeafPaletteGolden));
        OnPropertyChanged(nameof(IsLeafPaletteAutumn));
        OnPropertyChanged(nameof(IsLeafPaletteForest));
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnParticleCustomImagePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasParticleCustomImage));
        OnPropertyChanged(nameof(ParticleCustomImageDisplayName));
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnParticleCountChanged(int value) => OnPropertyChanged(nameof(ParticleCountText));
    partial void OnParticleSpeedChanged(double value) => OnPropertyChanged(nameof(ParticleSpeedText));
    partial void OnParticleWindChanged(double value) => OnPropertyChanged(nameof(ParticleWindText));
    partial void OnParticleSizeChanged(double value) => OnPropertyChanged(nameof(ParticleSizeText));
    partial void OnParticleOpacityChanged(double value) => OnPropertyChanged(nameof(ParticleOpacityText));

    partial void OnSnowPhysicsEnabledChanged(bool value)
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnSnowBottomAccumulationEnabledChanged(bool value)
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnSnowCursorInteractionEnabledChanged(bool value)
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnSnowPersistenceEnabledChanged(bool value)
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnSnowAccumulationRateChanged(double value) => OnPropertyChanged(nameof(SnowAccumulationRateText));
    partial void OnSnowMaxHeightChanged(double value) => OnPropertyChanged(nameof(SnowMaxHeightText));

    [RelayCommand]
    private void ToggleSnowPhysicsDetails() => IsSnowPhysicsDetailsExpanded = !IsSnowPhysicsDetailsExpanded;

    [RelayCommand]
    private void ClearSnowAccumulation()
    {
        ClearSnowAccumulationRequested?.Invoke();
    }

    [RelayCommand]
    private void SelectParticleType(string type)
    {
        if (!string.IsNullOrWhiteSpace(type))
            ParticleType = type;
    }

    [RelayCommand]
    private void SelectLeafPalette(string palette)
    {
        if (!string.IsNullOrWhiteSpace(palette))
            ParticleLeafPalette = palette;
    }

    [RelayCommand]
    private async Task PickParticleImageAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L10n.T("particles_pick_image_title"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Image Files (*.png, *.webp, *.jpg)") { Patterns = new[] { "*.png", "*.webp", "*.jpg", "*.jpeg" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count > 0)
        {
            ParticleCustomImagePath = files[0].Path.LocalPath;
            ParticleType = "Custom";
        }
    }

    [RelayCommand]
    private void ClearParticleImage()
    {
        ParticleCustomImagePath = string.Empty;
    }

    public void CommitParticleSettings()
    {
        if (!_isLoadingConfig) SaveConfigRequested?.Invoke();
    }

    partial void OnHardwareAccelerationChanged(bool value)
    {
        if (_isLoadingConfig || _isSyncingHardwareAccel) return;
        HardwareAccelRestartRequested?.Invoke(value);
    }

    public void RevertHardwareAcceleration()
    {
        _isSyncingHardwareAccel = true;
        try
        {
            HardwareAcceleration = Program.StartupHardwareAcceleration;
        }
        finally
        {
            _isSyncingHardwareAccel = false;
        }
    }

    public void CommitHardwareAcceleration(bool enabled)
    {
        _isSyncingHardwareAccel = true;
        try
        {
            HardwareAcceleration = enabled;
        }
        finally
        {
            _isSyncingHardwareAccel = false;
        }
        SaveConfigRequested?.Invoke();
    }

    [RelayCommand]
    public async Task ClearCacheAsync()
    {
        if (IsClearingCache) return;
        IsClearingCache = true;
        CacheStatusText = string.Empty;
        long bytesFreed = 0;

        await Task.Run(() =>
        {
            var targetDirs = new[]
            {
                Path.Combine(ZenithPaths.AppDataDir, "cache"),
                Path.Combine(ZenithPaths.AppDataDir, "temp"),
                Path.Combine(ZenithPaths.AppDataDir, "logs")
            };

            foreach (var dir in targetDirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    var di = new DirectoryInfo(dir);
                    foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            if (file.Name.Equals("launcher.log", StringComparison.OrdinalIgnoreCase)) continue;
                            bytesFreed += file.Length;
                            file.Delete();
                        }
                        catch { }
                    }
                }
                catch { }
            }
        });

        CatalogFirstPageCache.Clear();
        ModrinthApiService.ClearMemoryCache();

        IsClearingCache = false;
        var mb = Math.Max(0.1, Math.Round(bytesFreed / (1024.0 * 1024.0), 1));
        CacheStatusText = bytesFreed > 0
            ? $"{L10n.T("cache_cleared")} ({mb:0.#} MB)"
            : L10n.T("cache_cleared");
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
            var customCard = ThemeCards.FirstOrDefault(c => c.IsCustom);
            if (customCard != null)
                customCard.Name = L10n.T("theme_custom_card");
            SaveConfigRequested?.Invoke();
        }
    }

    // --- Discord RPC & Scale ---
    [ObservableProperty] private bool _discordRpcEnabled = true;
    [ObservableProperty] private double _launcherScale = 1.0;
    [ObservableProperty] private double _pendingLauncherScale = 1.0;
    [ObservableProperty] private string _scalePercentText = "100";
    [ObservableProperty] private string _curseForgeApiKey = string.Empty;

    partial void OnPendingLauncherScaleChanged(double value)
    {
        var percent = (int)Math.Round(value * 100);
        var text = percent.ToString();
        if (_scalePercentText != text)
        {
            _scalePercentText = text;
            OnPropertyChanged(nameof(ScalePercentText));
        }
    }

    public void ApplyScalePercentInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            ScalePercentText = Math.Round(PendingLauncherScale * 100).ToString("0");
            return;
        }

        var digits = new string(input.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var percent))
        {
            percent = Math.Clamp(percent, 80, 150);
            PendingLauncherScale = percent / 100.0;
            CommitLauncherScale();
            ScalePercentText = percent.ToString();
        }
        else
        {
            ScalePercentText = Math.Round(PendingLauncherScale * 100).ToString("0");
        }
    }

    partial void OnCurseForgeApiKeyChanged(string value)
    {
        if (!_isLoadingConfig)
        {
            SaveConfigRequested?.Invoke();
        }
    }

    public void CommitLauncherScale()
    {
        var finalScale = Math.Round(PendingLauncherScale, 2);
        PendingLauncherScale = finalScale;

        if (Math.Abs(LauncherScale - PendingLauncherScale) > 0.001)
        {
            LauncherScale = PendingLauncherScale;
        }
    }

    [RelayCommand]
    public void ResetLauncherScale()
    {
        PendingLauncherScale = 1.0;
        CommitLauncherScale();
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
    [ObservableProperty] private string _selectedThemeCardId = "zenith";
    [ObservableProperty] private string _selectedThemeMode = "Dark";
    [ObservableProperty] private string _selectedFontStyle = "Sans";
    [ObservableProperty] private string _customFontPath = string.Empty;
    [ObservableProperty] private string _customFontDisplayName = string.Empty;

    [ObservableProperty] private bool _isThemeRestartDialogOpen;
    [ObservableProperty] private string? _pendingThemeCardId;

    private bool _isSyncingCustomColor;
    private CancellationTokenSource? _customSaveDebounceCts;

    [ObservableProperty] private string _customAccentHex = string.Empty;
    [ObservableProperty] private HsvColor _customHsvColor = new(1.0, 262.0, 0.0, 1.0);
    [ObservableProperty] private string _customColorHexInput = "#FFFFFF";

    public IBrush CustomColorPreviewBrush
    {
        get
        {
            var hex = string.IsNullOrWhiteSpace(CustomAccentHex) ? "#FFFFFF" : CustomAccentHex;
            return Color.TryParse(hex, out var c) ? new SolidColorBrush(c) : Brushes.White;
        }
    }

    public string CustomColorDisplayHex => string.IsNullOrWhiteSpace(CustomAccentHex) ? "#FFFFFF" : CustomAccentHex;
    public bool HasCustomColorSaved => !string.IsNullOrWhiteSpace(CustomAccentHex);

    public ObservableCollection<ThemeSwatchModel> CustomColorSwatches { get; } = new()
    {
        new("#EF4444"),
        new("#F97316"),
        new("#EAB308"),
        new("#22C55E"),
        new("#10B981"),
        new("#06B6D4"),
        new("#3B82F6"),
        new("#6366F1"),
        new("#8B5CF6"),
        new("#EC4899")
    };

    public bool IsCustomFontOption => SelectedFontStyle == "Custom...";

    public ObservableCollection<ThemeCardModel> ThemeCards { get; } = new();
    public string[] FontStyleOptions { get; } = { "Sans", "Minecraft", "Serif", "Monospace", "System Default", "Custom..." };

    public void InitAppearanceModels()
    {
        ThemeCards.Clear();
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "zenith",
            Name = "Zenith",
            AccentHex = "#8B5CF6",
            AccentBrush = new SolidColorBrush(Color.Parse("#8B5CF6")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 139, 92, 246)),
            IsSelected = true
        });
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "cobalt",
            Name = "Cobalt",
            AccentHex = "#3B82F6",
            AccentBrush = new SolidColorBrush(Color.Parse("#3B82F6")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 59, 130, 246))
        });
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "mint",
            Name = "Mint",
            AccentHex = "#06B6D4",
            AccentBrush = new SolidColorBrush(Color.Parse("#06B6D4")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 6, 182, 212))
        });
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "bee",
            Name = "Bee",
            AccentHex = "#EAB308",
            AccentBrush = new SolidColorBrush(Color.Parse("#EAB308")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 234, 179, 8))
        });
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "parchment",
            Name = "Parchment",
            AccentHex = "#FB923C",
            AccentBrush = new SolidColorBrush(Color.Parse("#FB923C")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 251, 146, 60))
        });
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "crimson",
            Name = "Crimson",
            AccentHex = "#E11D48",
            AccentBrush = new SolidColorBrush(Color.Parse("#E11D48")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 225, 29, 72))
        });
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "rose",
            Name = "Rose",
            AccentHex = "#EC4899",
            AccentBrush = new SolidColorBrush(Color.Parse("#EC4899")),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 236, 72, 153))
        });
        ThemeCards.Add(new ThemeCardModel
        {
            Id = "custom",
            Name = L10n.T("theme_custom_card"),
            AccentHex = "#FFFFFF",
            AccentBrush = new SolidColorBrush(Colors.White),
            SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
            IsCustom = true
        });
    }

    partial void OnCustomHsvColorChanged(HsvColor value)
    {
        if (_isLoadingConfig || _isSyncingCustomColor) return;

        var hex = ZenithTheme.ColorToHex(value.ToRgb());
        _isSyncingCustomColor = true;
        try
        {
            if (!string.Equals(CustomColorHexInput, hex, StringComparison.OrdinalIgnoreCase))
                CustomColorHexInput = hex;
        }
        finally
        {
            _isSyncingCustomColor = false;
        }

        ApplyCustomColorUpdate(hex, immediateSave: false);
    }

    partial void OnCustomColorHexInputChanged(string value)
    {
        if (_isLoadingConfig || _isSyncingCustomColor || string.IsNullOrWhiteSpace(value)) return;

        var normalized = value.Trim();
        if (!normalized.StartsWith('#'))
            normalized = "#" + normalized;

        if (normalized.Length != 7 || !Color.TryParse(normalized, out var parsed))
            return;

        var canonical = ZenithTheme.ColorToHex(parsed);
        _isSyncingCustomColor = true;
        try
        {
            CustomHsvColor = new HsvColor(parsed);
        }
        finally
        {
            _isSyncingCustomColor = false;
        }

        ApplyCustomColorUpdate(canonical, immediateSave: false);
    }

    private void ApplyCustomColorUpdate(string hex, bool immediateSave)
    {
        CustomAccentHex = hex;
        OnPropertyChanged(nameof(CustomColorPreviewBrush));
        OnPropertyChanged(nameof(CustomColorDisplayHex));
        OnPropertyChanged(nameof(HasCustomColorSaved));

        var customCard = ThemeCards.FirstOrDefault(c => c.IsCustom);
        customCard?.UpdateColor(hex);

        if (immediateSave)
            SaveCustomThemeImmediately();
        else
            ScheduleAutoSaveCustomTheme();
    }

    private void ScheduleAutoSaveCustomTheme()
    {
        _customSaveDebounceCts?.Cancel();
        _customSaveDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _customSaveDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(160, cts.Token);
                if (!cts.IsCancellationRequested)
                    Dispatcher.UIThread.Post(() => SaveConfigRequested?.Invoke());
            }
            catch (OperationCanceledException) { }
        });
    }

    public void SaveCustomThemeImmediately()
    {
        _customSaveDebounceCts?.Cancel();
        _customSaveDebounceCts?.Dispose();
        _customSaveDebounceCts = null;
        if (!_isLoadingConfig)
            SaveConfigRequested?.Invoke();
    }

    [RelayCommand]
    public void PickCustomSwatch(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || !Color.TryParse(hex, out var parsed)) return;

        var canonical = ZenithTheme.ColorToHex(parsed);
        _isSyncingCustomColor = true;
        try
        {
            CustomHsvColor = new HsvColor(parsed);
            CustomColorHexInput = canonical;
        }
        finally
        {
            _isSyncingCustomColor = false;
        }

        ApplyCustomColorUpdate(canonical, immediateSave: true);
    }

    [RelayCommand]
    public void ResetCustomThemeColor()
    {
        _isSyncingCustomColor = true;
        try
        {
            CustomAccentHex = string.Empty;
            CustomColorHexInput = "#FFFFFF";
            CustomHsvColor = new HsvColor(1.0, 262.0, 0.0, 1.0);
        }
        finally
        {
            _isSyncingCustomColor = false;
        }

        OnPropertyChanged(nameof(CustomColorPreviewBrush));
        OnPropertyChanged(nameof(CustomColorDisplayHex));
        OnPropertyChanged(nameof(HasCustomColorSaved));

        var customCard = ThemeCards.FirstOrDefault(c => c.IsCustom);
        customCard?.UpdateColor("#FFFFFF");

        SaveCustomThemeImmediately();
    }

    [RelayCommand]
    public void ApplyCustomThemeCard()
    {
        var hex = !string.IsNullOrWhiteSpace(CustomAccentHex) ? CustomAccentHex : "#FFFFFF";
        PendingThemeCardId = "custom";
        ThemeSettingsVm.PendingRestartThemeHex = hex;
        ThemeSettingsVm.IsRestartConfirmOpen = true;
    }

    public void ApplyCurrentThemeLive()
    {
        var cfg = new LauncherConfigData();
        ApplyToConfig(cfg);
        ZenithTheme.Apply(cfg);
        foreach (var card in ThemeCards)
        {
            card.NotifyBrushesChanged();
        }
    }

    [RelayCommand]
    public void SelectThemeCard(string cardId)
    {
        if (cardId == "custom")
            return;

        if (cardId == SelectedThemeCardId) return;

        PendingThemeCardId = cardId;
        var card = ThemeCards.FirstOrDefault(c => c.Id == cardId);
        var hex = card?.AccentHex ?? "#8B5CF6";

        ThemeSettingsVm.PendingRestartThemeHex = hex;
        ThemeSettingsVm.IsRestartConfirmOpen = true;
    }

    [RelayCommand]
    public void ConfirmThemeRestart() => ThemeSettingsVm.ConfirmRestartAndApplyTheme();

    [RelayCommand]
    public void CancelThemeRestart() => ThemeSettingsVm.CancelRestartConfirm();

    partial void OnSelectedFontStyleChanged(string value)
    {
        OnPropertyChanged(nameof(IsCustomFontOption));
        if (value == "Custom...")
        {
            if (!string.IsNullOrWhiteSpace(CustomFontPath) && File.Exists(CustomFontPath))
            {
                ZenithTheme.ApplyFont(value, CustomFontPath);
            }
            else if (!_isLoadingConfig)
            {
                _ = BrowseCustomFontAsync();
            }
        }
        else
        {
            ZenithTheme.ApplyFont(value);
        }

        if (!_isLoadingConfig)
        {
            SaveConfigRequested?.Invoke();
        }
    }

    [RelayCommand]
    private async Task BrowseCustomFontAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider != null)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Font File (.ttf, .otf)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Font Files (*.ttf, *.otf)") { Patterns = new[] { "*.ttf", "*.otf", "*.woff2" } },
                    FilePickerFileTypes.All
                }
            });

            if (files.Count > 0)
            {
                var chosen = files[0].Path.LocalPath;
                CustomFontPath = chosen;
                CustomFontDisplayName = System.IO.Path.GetFileName(chosen);
                SelectedFontStyle = "Custom...";
                ZenithTheme.ApplyFont(SelectedFontStyle, CustomFontPath);
                SaveConfigRequested?.Invoke();
            }
        }
    }

    public LauncherSettingsViewModel(ThemeSettingsViewModel themeSettingsVm)
    {
        ThemeSettingsVm = themeSettingsVm;
        ThemeSettingsVm.ThemeApplied = hex =>
        {
            var matched = !string.IsNullOrEmpty(PendingThemeCardId)
                ? ThemeCards.FirstOrDefault(c => c.Id == PendingThemeCardId)
                : ThemeCards.FirstOrDefault(c => c.AccentHex.Equals(hex, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                SelectedThemeCardId = matched.Id;
                foreach (var c in ThemeCards) c.IsSelected = (c.Id == matched.Id);
            }
            PendingThemeCardId = null;
        };
        ThemeSettingsVm.ThemeRestartCancelled = () =>
        {
            PendingThemeCardId = null;
            foreach (var card in ThemeCards)
                card.IsSelected = (card.Id == SelectedThemeCardId);
        };
        L10n.LanguageChanged += () => OnPropertyChanged(nameof(ParticleCustomImageDisplayName));
        InitAppearanceModels();
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
            IsLockAspectRatio = cfg.IsLockAspectRatio;
            IsFullscreen = cfg.IsFullscreen;
            if (!string.IsNullOrWhiteSpace(cfg.JvmArgs)) JvmArgs = cfg.JvmArgs;
            if (!string.IsNullOrWhiteSpace(cfg.LaunchBehavior)) SelectedLaunchBehavior = cfg.LaunchBehavior;
            if (!string.IsNullOrWhiteSpace(cfg.SelectedDirectoryMode)) SelectedDirectoryMode = cfg.SelectedDirectoryMode;
            if (!string.IsNullOrWhiteSpace(cfg.JavaMode)) SelectedJavaMode = cfg.JavaMode;
            if (!string.IsNullOrWhiteSpace(cfg.CustomJavaPath)) CustomJavaPath = cfg.CustomJavaPath;
            ShowReleases = cfg.ShowReleases;
            ShowSnapshots = cfg.ShowSnapshots;
            ShowBetas = cfg.ShowBetas;
            ShowAlphas = cfg.ShowAlphas;
            UseFlatVersionList = cfg.UseFlatVersionList;
            AutoManageJava = cfg.AutoManageJava;
            DiscordRpcEnabled = cfg.DiscordRpcEnabled;
            MinimizeToTrayOnClose = cfg.MinimizeToTrayOnClose;
            UiAnimationsEnabled = cfg.UiAnimationsEnabled;
            UiAnimPageTransitionsEnabled = cfg.UiAnimPageTransitionsEnabled;
            UiAnimTabsEnabled = cfg.UiAnimTabsEnabled;
            UiAnimModalsEnabled = cfg.UiAnimModalsEnabled;
            UiAnimSidebarsEnabled = cfg.UiAnimSidebarsEnabled;
            UiAnimPopupsEnabled = cfg.UiAnimPopupsEnabled;
            SmoothScrollEnabled = cfg.SmoothScrollEnabled;
            SyncMotionConfiguration();

            ParticleType = string.IsNullOrWhiteSpace(cfg.ParticleType) ? "Off" : cfg.ParticleType;
            ParticleLeafPalette = string.IsNullOrWhiteSpace(cfg.ParticleLeafPalette) ? "Autumn" : cfg.ParticleLeafPalette;
            ParticleCustomImagePath = cfg.ParticleCustomImagePath ?? string.Empty;
            ParticleCount = Math.Clamp(cfg.ParticleCount > 0 ? cfg.ParticleCount : 45, 10, 160);
            ParticleSpeed = Math.Clamp(cfg.ParticleSpeed > 0 ? cfg.ParticleSpeed : 1.0, 0.2, 2.5);
            ParticleWind = Math.Clamp(cfg.ParticleWind, -1.5, 1.5);
            ParticleSize = Math.Clamp(cfg.ParticleSize > 0 ? cfg.ParticleSize : 1.0, 0.4, 2.2);
            ParticleOpacity = Math.Clamp(cfg.ParticleOpacity > 0 ? cfg.ParticleOpacity : 0.65, 0.15, 1.0);
            SnowPhysicsEnabled = cfg.SnowPhysicsEnabled;
            SnowBottomAccumulationEnabled = cfg.SnowBottomAccumulationEnabled;
            SnowCursorInteractionEnabled = cfg.SnowCursorInteractionEnabled;
            SnowPersistenceEnabled = cfg.SnowPersistenceEnabled;
            SnowAccumulationRate = Math.Clamp(cfg.SnowAccumulationRate > 0 ? cfg.SnowAccumulationRate : 1.0, 0.3, 3.0);
            SnowMaxHeight = Math.Clamp(cfg.SnowMaxHeight > 0 ? cfg.SnowMaxHeight : 11.0, 4.0, 24.0);
            _isSyncingHardwareAccel = true;
            try
            {
                HardwareAcceleration = cfg.HardwareAcceleration;
            }
            finally
            {
                _isSyncingHardwareAccel = false;
            }
            if (cfg.LauncherScale > 0.4 && cfg.LauncherScale < 3.0)
            {
                LauncherScale = cfg.LauncherScale;
                PendingLauncherScale = cfg.LauncherScale;
                ScalePercentText = Math.Round(cfg.LauncherScale * 100).ToString("0");
            }

            // Apply persisted Java paths immediately; verify/auto-discover missing slots off UI thread
            // so cold startup is never blocked by FileVersionInfo/Registry/Program Files scans.
            bool has25 = !string.IsNullOrWhiteSpace(cfg.Java25Path) && File.Exists(cfg.Java25Path);
            bool has21 = !string.IsNullOrWhiteSpace(cfg.Java21Path) && File.Exists(cfg.Java21Path);
            bool has17 = !string.IsNullOrWhiteSpace(cfg.Java17Path) && File.Exists(cfg.Java17Path);
            bool has8 = !string.IsNullOrWhiteSpace(cfg.Java8Path) && File.Exists(cfg.Java8Path);

            if (has25) Java25Location = cfg.Java25Path;
            if (has21) Java21Location = cfg.Java21Path;
            if (has17) Java17Location = cfg.Java17Path;
            if (has8) Java8Location = cfg.Java8Path;

            var snap25 = cfg.Java25Path;
            var snap21 = cfg.Java21Path;
            var snap17 = cfg.Java17Path;
            var snap8 = cfg.Java8Path;
            _ = Task.Run(() =>
            {
                static string? ResolveValidOrFind(string saved, int major)
                {
                    if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
                    {
                        var detectedMajor = JavaVersionHelper.GetJavaMajor(saved);
                        if (detectedMajor == major || detectedMajor == 0)
                            return null; // persisted path is already valid
                    }
                    return JavaVersionHelper.FindJavaForVersion(major);
                }

                var d25 = ResolveValidOrFind(snap25, 25);
                var d21 = ResolveValidOrFind(snap21, 21);
                var d17 = ResolveValidOrFind(snap17, 17);
                var d8 = ResolveValidOrFind(snap8, 8);

                if (d25 != null || d21 != null || d17 != null || d8 != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (!string.IsNullOrWhiteSpace(d25) && string.IsNullOrWhiteSpace(Java25Location)) Java25Location = d25;
                        if (!string.IsNullOrWhiteSpace(d21) && string.IsNullOrWhiteSpace(Java21Location)) Java21Location = d21;
                        if (!string.IsNullOrWhiteSpace(d17) && string.IsNullOrWhiteSpace(Java17Location)) Java17Location = d17;
                        if (!string.IsNullOrWhiteSpace(d8) && string.IsNullOrWhiteSpace(Java8Location)) Java8Location = d8;
                    });
                }
            });

            if (!string.IsNullOrWhiteSpace(cfg.Language))
                SelectedLanguage = cfg.Language;
            if (!string.IsNullOrWhiteSpace(cfg.CurseForgeApiKey))
                CurseForgeApiKey = cfg.CurseForgeApiKey;

            SelectedThemeMode = "Dark";
            SelectedFontStyle = string.IsNullOrWhiteSpace(cfg.FontStyle) ? "Sans" : cfg.FontStyle;
            CustomFontPath = cfg.CustomFontPath ?? string.Empty;
            CustomFontDisplayName = !string.IsNullOrWhiteSpace(CustomFontPath) ? Path.GetFileName(CustomFontPath) : string.Empty;

            var savedCustomHex = !string.IsNullOrWhiteSpace(cfg.CustomAccentColor)
                ? cfg.CustomAccentColor.Trim()
                : (cfg.SelectedThemeCardId == "custom" && !string.IsNullOrWhiteSpace(cfg.AccentColor) ? cfg.AccentColor.Trim() : string.Empty);

            var customCard = ThemeCards.FirstOrDefault(c => c.IsCustom);
            if (customCard != null)
                customCard.Name = L10n.T("theme_custom_card");

            if (!string.IsNullOrWhiteSpace(savedCustomHex) && Color.TryParse(savedCustomHex, out var customColor))
            {
                var canonical = ZenithTheme.ColorToHex(customColor);
                _isSyncingCustomColor = true;
                try
                {
                    CustomAccentHex = canonical;
                    CustomColorHexInput = canonical;
                    CustomHsvColor = new HsvColor(customColor);
                }
                finally
                {
                    _isSyncingCustomColor = false;
                }
                customCard?.UpdateColor(canonical);
            }
            else
            {
                _isSyncingCustomColor = true;
                try
                {
                    CustomAccentHex = string.Empty;
                    CustomColorHexInput = "#FFFFFF";
                    CustomHsvColor = new HsvColor(1.0, 262.0, 0.0, 1.0);
                }
                finally
                {
                    _isSyncingCustomColor = false;
                }
                customCard?.UpdateColor("#FFFFFF");
            }

            OnPropertyChanged(nameof(CustomColorPreviewBrush));
            OnPropertyChanged(nameof(CustomColorDisplayHex));
            OnPropertyChanged(nameof(HasCustomColorSaved));

            var currentAccent = !string.IsNullOrWhiteSpace(cfg.AccentColor)
                ? cfg.AccentColor
                : (!string.IsNullOrWhiteSpace(cfg.SimpleAccentColor) ? cfg.SimpleAccentColor : "#8B5CF6");

            string matchedId = !string.IsNullOrWhiteSpace(cfg.SelectedThemeCardId)
                ? cfg.SelectedThemeCardId
                : (currentAccent.ToUpperInvariant() switch
                {
                    "#06B6D4" or "#0891B2" => "mint",
                    "#EAB308" or "#D97706" => "bee",
                    "#FB923C" or "#EA580C" or "#F97316" => "parchment",
                    "#3B82F6" or "#2563EB" => "cobalt",
                    "#E11D48" or "#EF4444" => "crimson",
                    "#EC4899" or "#F43F5E" => "rose",
                    _ => "zenith"
                });

            if (!ThemeCards.Any(x => x.Id == matchedId))
            {
                matchedId = "zenith";
            }

            SelectedThemeCardId = matchedId;
            foreach (var card in ThemeCards)
                card.IsSelected = (card.Id == matchedId);

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
        cfg.IsLockAspectRatio = IsLockAspectRatio;
        cfg.IsFullscreen = IsFullscreen;
        cfg.JvmArgs = JvmArgs;
        cfg.LaunchBehavior = SelectedLaunchBehavior;
        cfg.SelectedDirectoryMode = SelectedDirectoryMode;
        cfg.JavaMode = SelectedJavaMode;
        cfg.CustomJavaPath = CustomJavaPath;
        cfg.ShowReleases = ShowReleases;
        cfg.ShowSnapshots = ShowSnapshots;
        cfg.ShowBetas = ShowBetas;
        cfg.ShowAlphas = ShowAlphas;
        cfg.UseFlatVersionList = UseFlatVersionList;
        cfg.AutoManageJava = AutoManageJava;
        cfg.DiscordRpcEnabled = DiscordRpcEnabled;
        cfg.MinimizeToTrayOnClose = MinimizeToTrayOnClose;
        cfg.UiAnimationsEnabled = UiAnimationsEnabled;
        cfg.UiAnimPageTransitionsEnabled = UiAnimPageTransitionsEnabled;
        cfg.UiAnimTabsEnabled = UiAnimTabsEnabled;
        cfg.UiAnimModalsEnabled = UiAnimModalsEnabled;
        cfg.UiAnimSidebarsEnabled = UiAnimSidebarsEnabled;
        cfg.UiAnimPopupsEnabled = UiAnimPopupsEnabled;
        cfg.SmoothScrollEnabled = SmoothScrollEnabled;
        cfg.ParticleType = ParticleType;
        cfg.ParticleLeafPalette = ParticleLeafPalette;
        cfg.ParticleCustomImagePath = ParticleCustomImagePath;
        cfg.ParticleCount = ParticleCount;
        cfg.ParticleSpeed = ParticleSpeed;
        cfg.ParticleWind = ParticleWind;
        cfg.ParticleSize = ParticleSize;
        cfg.ParticleOpacity = ParticleOpacity;
        cfg.SnowPhysicsEnabled = SnowPhysicsEnabled;
        cfg.SnowBottomAccumulationEnabled = SnowBottomAccumulationEnabled;
        cfg.SnowCursorInteractionEnabled = SnowCursorInteractionEnabled;
        cfg.SnowPersistenceEnabled = SnowPersistenceEnabled;
        cfg.SnowAccumulationRate = SnowAccumulationRate;
        cfg.SnowMaxHeight = SnowMaxHeight;
        cfg.HardwareAcceleration = HardwareAcceleration;
        cfg.CurseForgeApiKey = CurseForgeApiKey;
        cfg.LauncherScale = LauncherScale;
        
        cfg.Java25Path = Java25Location;
        cfg.Java21Path = Java21Location;
        cfg.Java17Path = Java17Location;
        cfg.Java8Path = Java8Location;
    
        cfg.Language = SelectedLanguage;

        cfg.ThemeMode = "Dark";
        cfg.FontStyle = SelectedFontStyle;
        cfg.CustomFontPath = CustomFontPath;

        cfg.SelectedThemeCardId = SelectedThemeCardId;
        cfg.CustomAccentColor = CustomAccentHex;
        cfg.IsCustomTheme = SelectedThemeCardId == "custom";

        cfg.AccentColor = SelectedThemeCardId switch
        {
            "mint" => "#06B6D4",
            "bee" => "#EAB308",
            "parchment" => "#FB923C",
            "cobalt" => "#3B82F6",
            "crimson" => "#E11D48",
            "rose" => "#EC4899",
            "custom" => !string.IsNullOrWhiteSpace(CustomAccentHex) ? CustomAccentHex : "#8B5CF6",
            _ => "#8B5CF6"
        };
        cfg.SimpleAccentColor = cfg.AccentColor;
        var harmonized = ZenithTheme.GenerateHarmonizedPalette(cfg.AccentColor, "Dark");
        cfg.BgColor = harmonized.BgColor;
        cfg.SidebarBgColor = harmonized.SidebarBgColor;
        cfg.SurfaceColor = harmonized.SurfaceColor;
        cfg.CardColor = harmonized.CardColor;
        cfg.PanelColor = harmonized.PanelColor;
        cfg.FieldColor = harmonized.FieldColor;
    }
}


public class ThemeSwatchModel
{
    public string Hex { get; }
    public IBrush ColorBrush { get; }

    public ThemeSwatchModel(string hex)
    {
        Hex = hex;
        ColorBrush = Color.TryParse(hex, out var c) ? new SolidColorBrush(c) : Brushes.White;
    }
}

public class ThemeCardModel : ObservableObject
{
    public string Id { get; set; } = "";
    public bool IsCustom { get; set; }

    private string _name = "";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _accentHex = "";
    public string AccentHex
    {
        get => _accentHex;
        set => SetProperty(ref _accentHex, value);
    }

    private IBrush _accentBrush = Brushes.Transparent;
    public IBrush AccentBrush
    {
        get => _accentBrush;
        set
        {
            if (SetProperty(ref _accentBrush, value))
            {
                OnPropertyChanged(nameof(CardBorderBrush));
                OnPropertyChanged(nameof(RadioBorderBrush));
            }
        }
    }

    private IBrush _softBrush = Brushes.Transparent;
    public IBrush SoftBrush
    {
        get => _softBrush;
        set => SetProperty(ref _softBrush, value);
    }

    public void UpdateColor(string hex)
    {
        if (!Color.TryParse(hex, out var color))
            color = Colors.White;

        AccentHex = ZenithTheme.ColorToHex(color);
        AccentBrush = new SolidColorBrush(color);
        SoftBrush = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(CardBorderBrush));
                OnPropertyChanged(nameof(CardBorderThickness));
                OnPropertyChanged(nameof(RadioBorderBrush));
                OnPropertyChanged(nameof(NameForegroundBrush));
            }
        }
    }

    public void NotifyBrushesChanged()
    {
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(RadioBorderBrush));
        OnPropertyChanged(nameof(NameForegroundBrush));
    }

    public IBrush CardBorderBrush
    {
        get
        {
            if (IsSelected) return AccentBrush;
            return Avalonia.Application.Current?.Resources.TryGetValue("BorderDarkBrush", out var b) == true && b is IBrush br
                ? br
                : new SolidColorBrush(Color.Parse("#25272D"));
        }
    }

    public Thickness CardBorderThickness => IsSelected ? new Thickness(1.5) : new Thickness(1);

    public IBrush RadioBorderBrush
    {
        get
        {
            if (IsSelected) return AccentBrush;
            return Avalonia.Application.Current?.Resources.TryGetValue("BorderDarkBrush", out var b) == true && b is IBrush br
                ? br
                : new SolidColorBrush(Color.Parse("#25272D"));
        }
    }

    public IBrush NameForegroundBrush
    {
        get
        {
            if (IsSelected)
            {
                return Avalonia.Application.Current?.Resources.TryGetValue("TextPrimaryBrush", out var b) == true && b is IBrush br
                    ? br
                    : Brushes.White;
            }
            return Avalonia.Application.Current?.Resources.TryGetValue("TextSecondaryBrush", out var b2) == true && b2 is IBrush br2
                ? br2
                : new SolidColorBrush(Color.Parse("#8E929B"));
        }
    }
}
