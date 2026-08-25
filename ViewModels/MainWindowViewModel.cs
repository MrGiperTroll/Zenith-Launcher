using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CmlLib.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;
using CustomMcLauncher.Views;

namespace CustomMcLauncher.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty] private bool _showReleases = true;
    [ObservableProperty] private bool _showSnapshots = false;
    [ObservableProperty] private bool _showBetas = false;
    [ObservableProperty] private bool _showAlphas = false;

    partial void OnShowReleasesChanged(bool value) => FilterVersions();
    partial void OnShowSnapshotsChanged(bool value) => FilterVersions();
    partial void OnShowBetasChanged(bool value) => FilterVersions();
    partial void OnShowAlphasChanged(bool value) => FilterVersions();
    partial void OnSelectedDirectoryModeChanged(string value) => SaveLauncherConfig();
    [ObservableProperty] private bool _useFlatVersionList = false;
    partial void OnUseFlatVersionListChanged(bool value) => SaveLauncherConfig();
    [ObservableProperty] private string _customJavaPath = string.Empty;

    [ObservableProperty]
    private string _currentJavaDisplay = "";

    /// <summary>Concrete javaw.exe path for the current Java mode - always shown in Settings.</summary>
    [ObservableProperty]
    private string _resolvedJavaPath = "";

    /// <summary>True while "Custom" is selected (only then the path box is editable).</summary>
    [ObservableProperty]
    private bool _isCustomJavaMode;
    [RelayCommand] private void OpenConsoleDialog() => IsConsoleDialogOpen = true;
    [ObservableProperty]
    private ObservableCollection<string> _directoryModes = new() { "Separate directory for each instance", "Family", "Do not use separate directories" };

    [ObservableProperty]
    private string _selectedDirectoryMode = "Separate directory for each instance";

    [ObservableProperty]
    private ObservableCollection<string> _javaModes = new() { "Recommended", "System", "Custom" };

    private readonly IAccountService _accountService;
    private readonly IInstanceService _instanceService;
    private readonly ILaunchService _launchService;
    private readonly ModLoaderService _modLoaderService;
    private readonly List<Views.EditInstanceWindow> _editInstanceWindows = new();
    private readonly List<(string Name, string Type)> _cachedVersions = new();

    [ObservableProperty]
    private string _statusText = string.Empty;

    public string VersionLabel { get; } =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    [ObservableProperty]
    private string _jvmArgs = "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC";

    [ObservableProperty]
    private int _ramMb = 4096;

    [ObservableProperty]
    private int _gameWidth = 1280;

    [ObservableProperty]
    private int _gameHeight = 720;

    [ObservableProperty]
    private bool _isFullscreen = false;

    [ObservableProperty]
    private bool _isElyByErrorOpen = false;

    [ObservableProperty]
    private string _elyByErrorMessage = "A connection issue occurred while preparing the game. Please verify your internet connection.\n\nRecommendation: Try switching to an Offline account to launch and play without an internet connection.";

    // Accounts
    public ObservableCollection<AccountModel> Accounts { get; } = new();

    [ObservableProperty]
    private AccountModel? _selectedAccount;

    [ObservableProperty]
    private string _activeAccountName = "Steve";

    [ObservableProperty]
    private string _activeAccountType = "Offline";

    [ObservableProperty]
    private string _newOfflineUsername = string.Empty;

    /// <summary>True while a browser-based sign-in (Microsoft / Ely.by OAuth) is in flight.</summary>
    [ObservableProperty]
    private bool _isAccountsBusy = false;

    /// <summary>Last sign-in error, shown inside the accounts dialog (StatusText is hidden behind it).</summary>
    [ObservableProperty]
    private string _authErrorText = string.Empty;

    private CancellationTokenSource? _authCts;

    // Sign-in cancellation is automatic: closing the accounts dialog (or the main
    // window with it) cancels _authCts via AccountsWindow.Closed -> see OpenAccountsDialog.

    // Profiles / Instances
    public ObservableCollection<InstanceModel> Instances { get; } = new();

    [ObservableProperty]
    private InstanceModel? _selectedInstance;

    private string? _preRenameName;

    partial void OnSelectedInstanceChanged(InstanceModel? value)
    {
        OnPropertyChanged(nameof(CanLaunch));
        IsLaunching = value != null && _launchingInstanceIds.Contains(value.Id);
        UpdateJavaDisplay();
        _preRenameName = value?.Name;
        if (value != null)
            value.PropertyChanged += OnInstancePropertyChanged;
    }

    private void OnInstancePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstanceModel.IsRunning))
            OnPropertyChanged(nameof(CanLaunch));
        if (e.PropertyName == nameof(InstanceModel.IsLaunching))
        {
            OnPropertyChanged(nameof(CanLaunch));
            if (ReferenceEquals(sender, SelectedInstance))
                IsLaunching = SelectedInstance!.IsLaunching;
        }
    }

    public async void OnInstanceNameLostFocus()
    {
        if (SelectedInstance == null) return;
        var currentName = SelectedInstance.Name;
        if (string.Equals(currentName, _preRenameName, StringComparison.Ordinal)) return;

        var (ok, error) = await _instanceService.RenameInstanceAsync(SelectedInstance, currentName);
        if (!ok)
        {
            SelectedInstance.Name = _preRenameName ?? currentName;
            StatusText = error ?? "";
        }
        else
        {
            _preRenameName = SelectedInstance.Name;
            RefreshState();
        }
    }

    [ObservableProperty]
    private string _selectedInstanceId = string.Empty;

    [ObservableProperty]
    private string _selectedInstanceName = "No Profile Selected";

    [ObservableProperty]
    private string _selectedInstanceDetails = "Click '+ Create Profile' to begin";

    [ObservableProperty]
    private bool _hasInstances;

    [ObservableProperty]
    private string _newInstanceName = string.Empty;

    partial void OnNewInstanceNameChanged(string value)
    {
        IsNewInstanceNameDuplicate = !string.IsNullOrWhiteSpace(value) &&
            _instanceService.GetInstances().Any(i => i.Name.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    // Versions
    public ObservableCollection<string> AvailableVersions { get; } = new();

    [ObservableProperty]
    private string _selectedVersion = "";

    [ObservableProperty]
    private bool _isNewInstanceNameDuplicate;

    public bool CanCreateInstance => !string.IsNullOrWhiteSpace(SelectedVersion) &&
        (SelectedLoader == "Vanilla" || !string.IsNullOrWhiteSpace(SelectedLoaderBuild));

    [ObservableProperty] private string _settingsSearchQuery = string.Empty;

    partial void OnSettingsSearchQueryChanged(string value)
    {
        UpdateSearchVisibility();
    }

    [ObservableProperty] private bool _isSearchMemoryVisible = true;
    [ObservableProperty] private bool _isSearchResolutionVisible = true;
    [ObservableProperty] private bool _isSearchFullscreenVisible = true;
    [ObservableProperty] private bool _isSearchJavaVisible = true;
    [ObservableProperty] private bool _isSearchDirectoryVisible = true;
    [ObservableProperty] private bool _isSearchVersionFiltersVisible = true;
    [ObservableProperty] private bool _isSearchJvmArgsVisible = true;
    [ObservableProperty] private bool _isSearchLanguageVisible = true;
    [ObservableProperty] private bool _isSearchDiscordVisible = true;

    private void UpdateSearchVisibility()
    {
        var q = SettingsSearchQuery?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(q))
        {
            IsSearchMemoryVisible = IsSearchResolutionVisible = IsSearchFullscreenVisible = IsSearchJavaVisible = true;
            IsSearchDirectoryVisible = IsSearchVersionFiltersVisible = IsSearchJvmArgsVisible = true;
            IsSearchLanguageVisible = IsSearchDiscordVisible = true;
            return;
        }
        IsSearchMemoryVisible = "memory ram allocated".Contains(q);
        IsSearchResolutionVisible = "resolution window size".Contains(q);
        IsSearchFullscreenVisible = "fullscreen mode".Contains(q);
        IsSearchJavaVisible = "java runtime manage automatic download".Contains(q);
        IsSearchDirectoryVisible = "directory strategy isolation".Contains(q);
        IsSearchVersionFiltersVisible = "version filters releases snapshots".Contains(q);
        IsSearchJvmArgsVisible = "jvm arguments flags".Contains(q);
        IsSearchLanguageVisible = "language localization english russian ukrainian turkish german french".Contains(q);
        IsSearchDiscordVisible = "discord rpc rich presence application id".Contains(q);
    }

    [ObservableProperty] private bool _autoManageJava = true;

    partial void OnAutoManageJavaChanged(bool value)
    {
        LauncherLog.Info($"Automatic Java management {(value ? "enabled" : "disabled")}.");
    }

    [ObservableProperty] private bool _discordRpcEnabled = true;

    partial void OnDiscordRpcEnabledChanged(bool value)
    {
        if (!value)
        {
            Services.DiscordPresenceService.Shutdown();
            LauncherLog.Info("Discord RPC: disabled via settings toggle.");
        }
        else
        {
            Services.DiscordPresenceService.Initialize(true);
            Services.DiscordPresenceService.SetBrowsingProfiles();
            LauncherLog.Info("Discord RPC: enabled via settings toggle.");
        }
    }

    [ObservableProperty] private string _selectedLanguage = "English";

    public string[] LanguageOptions => Services.L10n.LanguageDisplayNames;

    partial void OnSelectedLanguageChanged(string value)
    {
        Services.L10n.ApplyDisplay(value);
        // The Discord status is rendered from the current language - push the
        // same logical activity again so it switches language instantly.
        Services.DiscordPresenceService.RefreshForLanguageChange();
        // Rebuild converter-driven collections so ComboBox items re-evaluate
        // their display converters with the new language.
        JavaModes = new ObservableCollection<string>(new[] { "Recommended", "System", "Custom" });
        DirectoryModes = new ObservableCollection<string>(new[] { "Separate directory for each instance", "Family", "Do not use separate directories" });
        ModpacksBrowser?.RefreshTranslations();
    }

    // --- Manual Java install (Settings "+" button) ---

    [ObservableProperty] private int _isJavaDownloading;

    [ObservableProperty] private string _javaDownloadStatus = string.Empty;

    public bool CanDownloadJava => IsJavaDownloading == 0;

    partial void OnIsJavaDownloadingChanged(int value) => OnPropertyChanged(nameof(CanDownloadJava));

    [RelayCommand]
    private async Task DownloadJavaAsync(object? parameter)
    {
        if (!int.TryParse(parameter?.ToString(), out var major)) return;
        if (IsJavaDownloading != 0) return;

        IsJavaDownloading = major;
        JavaDownloadStatus = string.Format(L10n.T("java_status_installing"), major);
        try
        {
            var ok = await Task.Run(() => _launchService.DownloadJavaRuntimeAsync(major, ZenithPaths.AppDataDir));
            JavaDownloadStatus = ok
                ? string.Format(L10n.T("java_status_installed"), major)
                : string.Format(L10n.T("java_status_failed"), major);
            StatusText = JavaDownloadStatus;

            if (ok)
            {
                // The freshly installed runtime becomes active immediately:
                // Recommended mode auto-picks it whenever a profile needs its
                // major version. Persisted right away so closing the dialog
                // (or the app) keeps the choice.
                SelectedJavaMode = "Recommended";
                SaveLauncherConfig();
            }
        }
        catch (Exception ex)
        {
            JavaDownloadStatus = $"Java {major}: {ex.Message}";
            LauncherLog.Error($"Manual Java {major} install crashed.", ex);
        }
        finally
        {
            IsJavaDownloading = 0;
            UpdateJavaDisplay();
        }
    }

    [ObservableProperty] private bool _isSettingsTabGame = true;
    [ObservableProperty] private bool _isLockAspectRatio = false;
    [ObservableProperty] private int _maxRamMb = 16384;

    [RelayCommand]
    private void SwitchSettingsTab(string tab)
    {
        IsSettingsTabGame = tab == "Game";
        OnPropertyChanged(nameof(IsSettingsTabGame));
        OnPropertyChanged(nameof(IsSettingsTabGeneral));
    }

    public bool IsSettingsTabGeneral => !IsSettingsTabGame;

    partial void OnGameWidthChanged(int value)
    {
        if (IsLockAspectRatio && value > 0)
            GameHeight = value * 9 / 16;
    }

    partial void OnGameHeightChanged(int value)
    {
        if (IsLockAspectRatio && value > 0)
            GameWidth = value * 16 / 9;
    }

    partial void OnSelectedVersionChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreateInstance));
        _ = UpdateAvailableLoadersAsync(value);
    }

    // Mod Loaders
    public ObservableCollection<string> AvailableLoaders { get; } = new();

    [ObservableProperty]
    private string _selectedLoader = "Vanilla";

    [ObservableProperty]
    private bool _isLoadingLoaders;

    public ObservableCollection<string> AvailableLoaderBuilds { get; } = new();

    [ObservableProperty]
    private string _selectedLoaderBuild = string.Empty;

    [ObservableProperty]
    private bool _showLoaderBuilds;

    [ObservableProperty]
    private string _loaderError = string.Empty;

    [ObservableProperty]
    private bool _loaderErrorVisible;

    public bool IsLoaderTypesVisible => !ShowLoaderBuilds;
    public bool IsLoaderBuildsVisible => ShowLoaderBuilds;

    public bool CanSwapLoader => SelectedLoader != "Vanilla";

    partial void OnShowLoaderBuildsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLoaderTypesVisible));
        OnPropertyChanged(nameof(IsLoaderBuildsVisible));
    }

    [RelayCommand]
    private void ToggleLoaderBuilds()
    {
        if (!ShowLoaderBuilds)
        {
            // switching to build mode — ensure builds are loaded
            if (AvailableLoaderBuilds.Count == 0 && SelectedLoader != "Vanilla")
                _ = UpdateLoaderBuildsAsync();
        }
        ShowLoaderBuilds = !ShowLoaderBuilds;
    }

    // Java
    [ObservableProperty]
    private string _selectedJavaMode = "Recommended";

    // Launch & Logs
    private readonly HashSet<string> _launchingInstanceIds = new();

    public bool CanLaunch => SelectedInstance == null || (!SelectedInstance.IsRunning && !_launchingInstanceIds.Contains(SelectedInstance.Id));

    [ObservableProperty]
    private bool _isLaunching;

    partial void OnIsLaunchingChanged(bool value) => OnPropertyChanged(nameof(LaunchButtonText));

    public string LaunchButtonText => IsLaunching ? L10n.T("cancel") : L10n.T("btn_launch_game");

    [ObservableProperty]
    private double _launchProgress;

    [ObservableProperty]
    private string _consoleLogs = string.Empty;

    [ObservableProperty]
    private string _currentDownloadFile = string.Empty;

    public static string AppVersion =>
        typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public bool IsDownloading => IsLaunching && LaunchProgress < 100;

    // UI Dialogs
    [ObservableProperty]
    private bool _isRightSidebarOpen;

    [ObservableProperty]
    private bool _isVersionPickerOpen;

    [ObservableProperty]
    private bool _isCreateDialogOpen;

    [ObservableProperty]
    private bool _isAboutOpen;

    [ObservableProperty]
    private bool _isModpacksView;

    public bool IsProfilesView => !IsModpacksView;

    partial void OnIsModpacksViewChanged(bool value) => OnPropertyChanged(nameof(IsProfilesView));

    [RelayCommand]
    private void ShowProfiles()
    {
        IsModpacksView = false;
        Services.DiscordPresenceService.SetBrowsingProfiles();
    }

    [RelayCommand]
    private void ShowModpacks()
    {
        IsModpacksView = true;
        Services.DiscordPresenceService.SetBrowsingModpacks();
    }

    public ModpacksBrowserViewModel ModpacksBrowser { get; private set; } = null!;

    [ObservableProperty]
    private bool _isConsoleDialogOpen;

    // Delete confirmation dialog
    [ObservableProperty]
    private bool _isDeleteConfirmOpen;

    [ObservableProperty]
    private InstanceModel? _pendingDeleteInstance;

    public ObservableCollection<AccountRow> AccountRows { get; } = new();

    public AccountRow? ActiveAccountRow
    {
        get
        {
            foreach (var r in AccountRows)
                if (r.IsActive) return r;
            return null;
        }
    }

    public MainWindowViewModel()
    {
        LoadLauncherConfig();

        // Force-apply the language even when the loaded value equals the
        // field default ("English") — CommunityToolkit skips the change
        // notification in that case, so OnSelectedLanguageChanged never
        // fires and the UI stays on DetectSystemCode() (the OS language).
        Services.L10n.ApplyDisplay(SelectedLanguage);

        _accountService = new AccountService();
        _instanceService = new InstanceService();
        _launchService = new LaunchService((IInstanceService)_instanceService);
        _modLoaderService = new ModLoaderService();

        // Re-translate computed UI strings when the language changes.
        L10n.LanguageChanged += () => OnPropertyChanged(nameof(LaunchButtonText));

        // Detect total physical RAM
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var memStatus = new MEMORYSTATUSEX();
                memStatus.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref memStatus))
                    MaxRamMb = (int)(memStatus.ullTotalPhys / (1024 * 1024));
            }
        }
        catch { MaxRamMb = 16384; }

        _launchService.ProgressChanged += p => Dispatcher.UIThread.Post(() => LaunchProgress = p);
        _launchService.CurrentFileChanged += f => Dispatcher.UIThread.Post(() => CurrentDownloadFile = f);
        _launchService.LogReceived += log =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ConsoleLogs += log + Environment.NewLine;
            });
        };

        ModLoaderService.Diagnostic += msg =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ConsoleLogs += $"[ModLoader] {msg}" + Environment.NewLine;
            });
        };

        // Discord Rich Presence lifecycle
        Services.DiscordPresenceService.Initialize(DiscordRpcEnabled);
        Services.DiscordPresenceService.SetBrowsingProfiles();
        _launchService.GameLaunched += (instance, accountType) =>
            Services.DiscordPresenceService.SetPlaying(instance.Name, instance.Version, accountType, DateTime.UtcNow);
        _launchService.GameExited += (_, _) => Services.DiscordPresenceService.SetBrowsingProfiles();
        _launchService.CrashDetected += (instance, exitCode, analysis) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { new Views.CrashReportWindow(analysis).Show(); }
                catch (Exception wex) { LauncherLog.Error("Failed to show crash report window", wex); }
            });
        };

        RefreshState();

        // Silent startup session maintenance: renew expired tokens in the
        // background so returning users never see a re-login prompt.
        _ = Task.Run(async () =>
        {
            try
            {
                var renewed = await _accountService.RefreshAllSessionsAsync().ConfigureAwait(false);
                if (renewed > 0)
                    Dispatcher.UIThread.Post(RefreshState);
            }
            catch (Exception ex)
            {
                LauncherLog.Error("Startup session refresh crashed.", ex);
            }
        });

        _ = LoadMojangVersionsAsync();
        _ = InitializeModLoadersAsync();
        UpdateJavaDisplay();
        ModpacksBrowser = new ModpacksBrowserViewModel(_instanceService, _modLoaderService, () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RefreshState();
                IsModpacksView = false;
                ModpacksBrowser.UpdateInstalledFlags();
            });
        });

        _ = CheckForUpdatesBackgroundAsync();
    }

    private async Task CheckForUpdatesBackgroundAsync()
    {
        try
        {
            await Task.Delay(5000);
            var service = new GitHubUpdateService();
            var result = await service.CheckForUpdatesAsync();
            if (result is { IsNewerThanCurrent: true })
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime
                        is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } main })
                        return;
                    var window = new UpdateNotificationWindow();
                    var vm = new UpdateNotificationViewModel(result, () => window.Close());
                    window.DataContext = vm;
                    window.ShowDialog(main);
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LauncherLog.Error("Background update check failed.", ex);
        }
    }

    private async Task InitializeModLoadersAsync()
    {
        await _modLoaderService.InitializeDatabaseAsync();
        await UpdateAvailableLoadersAsync(SelectedVersion);
    }

    [RelayCommand]
    public async Task SwitchToOfflineAccountAsync()
    {
        IsElyByErrorOpen = false;
        try
        {
            var targetName = !string.IsNullOrWhiteSpace(SelectedAccount?.Username)
                ? SelectedAccount.Username
                : (!string.IsNullOrWhiteSpace(ActiveAccountName) ? ActiveAccountName : "Steve");

            var existingAccounts = _accountService.GetAccounts();
            var offlineAcc = existingAccounts.FirstOrDefault(a => a.Type == Models.AccountType.Offline && string.Equals(a.Username, targetName, StringComparison.OrdinalIgnoreCase));

            if (offlineAcc == null)
            {
                offlineAcc = await _accountService.AddOfflineAccountAsync(targetName);
            }

            _accountService.SetActiveAccount(offlineAcc.Id);
            RefreshState();
        }
        catch (Exception)
        {
            StatusText = L10n.T("mw_status_switch_failed");
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is "JvmArgs" or "RamMb" or "GameWidth" or "GameHeight" or "IsFullscreen" or "CustomJavaPath" or "SelectedJavaMode"
            or "AutoManageJava" or "DiscordRpcEnabled" or "SelectedLanguage")
        {
            SaveLauncherConfig();
        }
    }

    public void SaveLauncherConfig()
    {
        try
        {
            ZenithPaths.EnsureAppDataExists();
            var configPath = ZenithPaths.ConfigFilePath;
            var configData = new LauncherConfigData
            {
                JvmArgs = JvmArgs,
                RamMb = RamMb,
                GameWidth = GameWidth,
                GameHeight = GameHeight,
                IsFullscreen = IsFullscreen,
                SelectedDirectoryMode = SelectedDirectoryMode,
                JavaMode = SelectedJavaMode,
                CustomJavaPath = CustomJavaPath,
                UseFlatVersionList = UseFlatVersionList,
                AutoManageJava = AutoManageJava,
                DiscordRpcEnabled = DiscordRpcEnabled,
                Language = SelectedLanguage
            };
            File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(configData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>
    /// Snapshot of the current global launcher settings, used by the instance editor
    /// so per-instance settings inherit the global defaults unless explicitly overridden.
    /// </summary>
    public LauncherConfigData GetLauncherConfigSnapshot() => new()
    {
        JvmArgs = JvmArgs,
        RamMb = RamMb,
        GameWidth = GameWidth,
        GameHeight = GameHeight,
        IsFullscreen = IsFullscreen,
        SelectedDirectoryMode = SelectedDirectoryMode,
        JavaMode = SelectedJavaMode,
        CustomJavaPath = CustomJavaPath,
        UseFlatVersionList = UseFlatVersionList,
        AutoManageJava = AutoManageJava,
        DiscordRpcEnabled = DiscordRpcEnabled,
        Language = SelectedLanguage
    };

    public void LoadLauncherConfig()
    {
        try
        {
            var configPath = ZenithPaths.ConfigFilePath;
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var cfg = System.Text.Json.JsonSerializer.Deserialize<LauncherConfigData>(json);
                if (cfg != null)
                {
                    // Language is restored FIRST and verbatim: an explicit
                    // choice (including "Auto") always wins over detection.
                    // The system language is consulted exactly once - on the
                    // very first run, when nothing has been stored yet.
                    if (string.IsNullOrWhiteSpace(cfg.Language))
                    {
                        // First launch: resolve the OS language once and
                        // persist it immediately as the user's choice.
                        SelectedLanguage = L10n.CurrentDisplay;
                        SaveLauncherConfig();
                        LauncherLog.Info($"First run: system language '{L10n.CurrentDisplay}' stored as default.");
                    }
                    else
                    {
                        SelectedLanguage = NormalizeStoredLanguage(cfg.Language);
                    }

                    if (!string.IsNullOrWhiteSpace(cfg.JvmArgs)) JvmArgs = cfg.JvmArgs;
                    if (cfg.RamMb > 0) RamMb = cfg.RamMb;
                    if (cfg.GameWidth > 0) GameWidth = cfg.GameWidth;
                    if (cfg.GameHeight > 0) GameHeight = cfg.GameHeight;
                    IsFullscreen = cfg.IsFullscreen;
                    if (!string.IsNullOrWhiteSpace(cfg.SelectedDirectoryMode))
                        SelectedDirectoryMode = cfg.SelectedDirectoryMode;
                    if (!string.IsNullOrWhiteSpace(cfg.JavaMode))
                        SelectedJavaMode = cfg.JavaMode;
                    if (!string.IsNullOrWhiteSpace(cfg.CustomJavaPath))
                        CustomJavaPath = cfg.CustomJavaPath;
                    UseFlatVersionList = cfg.UseFlatVersionList;
                    AutoManageJava = cfg.AutoManageJava;
                    DiscordRpcEnabled = cfg.DiscordRpcEnabled;
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to load launcher config - keeping current settings.", ex);
        }
    }

    /// <summary>
    /// Normalizes a stored language value (display name or legacy 2-letter code)
    /// into a display name that ApplyDisplay and the config file both understand.
    /// Returns "Auto" for "auto", empty, or completely unknown values.
    /// </summary>
    private static string NormalizeStoredLanguage(string stored)
    {
        return (stored ?? "").Trim() switch
        {
            "English" or "en" => "English",
            "Українська" or "uk" => "Українська",
            "Русский" or "ru" => "Русский",
            "Türkçe" or "tr" => "Türkçe",
            "Deutsch" or "de" => "Deutsch",
            "Français" or "fr" => "Français",
            "Auto" or "auto" => "Auto",
            _ => "Auto",
        };
    }

    private async Task UpdateAvailableLoadersAsync(string version)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var loaders = _modLoaderService.GetSupportedLoaders(version);
            if (!AvailableLoaders.SequenceEqual(loaders))
            {
                AvailableLoaders.Clear();
                foreach (var loader in loaders)
                    AvailableLoaders.Add(loader);
            }

            if (!AvailableLoaders.Contains(SelectedLoader))
                SelectedLoader = AvailableLoaders.FirstOrDefault() ?? "Vanilla";
            // Re-push the value: resetting ItemsSource wipes the ComboBox's visual
            // selection, and assigning the identical string raises no notification.
            OnPropertyChanged(nameof(SelectedLoader));
        });
        await UpdateLoaderBuildsAsync();
    }

    // Monotonic id: only the most recent builds request may touch the UI.
    private int _loaderBuildsRequestId;

    private async Task UpdateLoaderBuildsAsync()
    {
        var requestId = ++_loaderBuildsRequestId;
        var loader = SelectedLoader;
        var version = SelectedVersion;
        AvailableLoaderBuilds.Clear();
        if (string.IsNullOrWhiteSpace(loader) || loader == "Vanilla")
        {
            SelectedLoaderBuild = "";
            OnPropertyChanged(nameof(SelectedLoaderBuild));
            LoaderErrorVisible = false;
            LoaderError = string.Empty;
            return;
        }

        List<string> builds;
        try
        {
            builds = await _modLoaderService.GetLoaderBuildsAsync(version, loader);
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to load loader builds", ex);
            builds = new List<string> { "Latest (Auto)" };
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (requestId != _loaderBuildsRequestId) return; // stale response - a newer request owns the UI
            AvailableLoaderBuilds.Clear();
            foreach (var b in builds)
                AvailableLoaderBuilds.Add(b);

            SelectedLoaderBuild = AvailableLoaderBuilds.FirstOrDefault() ?? "";
            OnPropertyChanged(nameof(SelectedLoaderBuild));

            LoaderErrorVisible = false;
            LoaderError = string.Empty;
        });
    }

    partial void OnSelectedLoaderChanged(string value)
    {
        _ = UpdateLoaderBuildsAsync();
        OnPropertyChanged(nameof(CanSwapLoader));
    }

    partial void OnSelectedLoaderBuildChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreateInstance));
    }

    partial void OnSelectedJavaModeChanged(string value)
    {
        UpdateJavaDisplay();
    }

    partial void OnCustomJavaPathChanged(string value)
    {
        UpdateJavaDisplay();
    }

    private void UpdateJavaDisplay()
    {
        IsCustomJavaMode = SelectedJavaMode == "Custom";

        switch (SelectedJavaMode)
        {
            case "Custom":
                CurrentJavaDisplay = Services.JavaVersionHelper.CustomDisplay(CustomJavaPath);
                ResolvedJavaPath = CustomJavaPath;
                break;

            case "System":
                CurrentJavaDisplay = L10n.T("java_display_system_path");
                ResolvedJavaPath = Services.JavaVersionHelper.FindSystemJavaPath() ?? "";
                break;

            default: // Recommended
                var inst = SelectedInstance ?? _instanceService.GetSelectedInstance();
                CurrentJavaDisplay = inst != null
                    ? Services.JavaVersionHelper.RecommendedDisplay(inst.Version)
                    : L10n.T("java_display_auto");
                ResolvedJavaPath = "";
                _ = ResolveRecommendedJavaPathAsync(inst?.Version);
                break;
        }
    }

    /// <summary>
    /// Resolves the exact javaw.exe Recommended mode would launch for the
    /// selected profile (no downloads) so the Settings dialog can always show
    /// a real path. Runs off the UI thread; a result that arrives after the
    /// user switched the mode is discarded.
    /// </summary>
    private async Task ResolveRecommendedJavaPathAsync(string? mcVersion)
    {
        try
        {
            var path = await _launchService.ResolveJavaForDisplayAsync(mcVersion);
            if (SelectedJavaMode != "Recommended") return; // user switched meanwhile
            ResolvedJavaPath = path ?? "";
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to resolve the recommended Java path for display.", ex);
        }
    }

    /// <summary>Passthrough for the per-instance editor dialog.</summary>
    internal Task<string?> ResolveJavaForEditorAsync(string? instanceVersion) =>
        _launchService.ResolveJavaForDisplayAsync(instanceVersion);

    [RelayCommand]
    private async Task PickCustomJavaAsync()
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
                CustomJavaPath = files[0].Path.LocalPath;
                SelectedJavaMode = "Custom";
                SaveLauncherConfig(); // picked file is active + persisted at once
            }
        }
    }

    public async Task<bool> CheckCommandLineLaunchAsync(string[]? args)
    {
        if (args == null || args.Length < 2 || !args[0].Equals("--launch", StringComparison.OrdinalIgnoreCase))
            return false;

        var targetId = args[1].Trim('\"');
        var match = Instances.FirstOrDefault(i => i.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))
                 ?? Instances.FirstOrDefault(i => i.Name.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            StatusText = string.Format(L10n.T("mw_status_profile_missing"), targetId);
            return false;
        }

        SelectInstance(match);
        await LaunchGameAsync();
        return match.IsRunning;
    }

    public bool HasProfile(string idOrName) =>
        Instances.Any(i => i.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase))
        || Instances.Any(i => i.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));

    public async Task LoadMojangVersionsAsync()
    {
        try
        {
            var path = new MinecraftPath(ZenithPaths.AppDataDir);
            var launcher = new MinecraftLauncher(path);
            var versions = await LaunchService.WithManifestRetryAsync(() => launcher.GetAllVersionsAsync().AsTask());

            _cachedVersions.Clear();
            foreach (var v in versions)
            {
                // Never force a type on local entries - unknown types are filtered
                // downstream; forcing "release" leaked modloader profiles into the list.
                if (ModLoaderService.IsLoaderProfileId(v.Name)) continue;
                _cachedVersions.Add((v.Name, v.Type ?? ""));
            }

            var cachePath = Path.Combine(ZenithPaths.AppDataDir, "versions_cache.json");
            try { File.WriteAllText(cachePath, System.Text.Json.JsonSerializer.Serialize(_cachedVersions)); } catch { }

            FilterVersions();
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to fetch Mojang version list, falling back to cache", ex);
            // Try cache fallback
            var cachePath = Path.Combine(ZenithPaths.AppDataDir, "versions_cache.json");
            _cachedVersions.Clear();
            if (File.Exists(cachePath))
            {
                try
                {
                    var cached = System.Text.Json.JsonSerializer.Deserialize<List<(string Name, string Type)>>(File.ReadAllText(cachePath));
                    if (cached != null && cached.Count > 0)
                        _cachedVersions.AddRange(cached);
                }
                catch { }
            }
            if (_cachedVersions.Count == 0)
            {
                _cachedVersions.Add(("1.21.1", "release"));
                _cachedVersions.Add(("1.20.4", "release"));
                _cachedVersions.Add(("1.20.1", "release"));
                _cachedVersions.Add(("1.16.5", "release"));
                _cachedVersions.Add(("1.8.9", "release"));
                _cachedVersions.Add(("1.7.10", "release"));
                _cachedVersions.Add(("1.0", "release"));
            }
            FilterVersions();
        }
        finally
        {
            if (!AvailableVersions.Contains(SelectedVersion) && AvailableVersions.Count > 0)
                SelectedVersion = AvailableVersions.First();
        }
    }

    public void FilterVersions()
    {
        AvailableVersions.Clear();

        foreach (var v in _cachedVersions)
        {
            if (ModLoaderService.IsLoaderProfileId(v.Name)) continue;
            if (v.Type == "release" && ShowReleases)
                AvailableVersions.Add(v.Name);
            else if (v.Type == "snapshot" && ShowSnapshots)
                AvailableVersions.Add(v.Name);
            else if (v.Type == "old_beta" && ShowBetas)
                AvailableVersions.Add(v.Name);
            else if (v.Type == "old_alpha" && ShowAlphas)
                AvailableVersions.Add(v.Name);
        }

        if (AvailableVersions.Count == 0 && _cachedVersions.Count > 0)
        {
            foreach (var v in _cachedVersions)
            {
                if (ModLoaderService.IsLoaderProfileId(v.Name)) continue;
                AvailableVersions.Add(v.Name);
            }
        }
    }

    public void RefreshState()
    {
        var allAccs = _accountService.GetAccounts();
        Accounts.Clear();
        foreach (var acc in allAccs)
            Accounts.Add(acc);

        var activeAcc = _accountService.GetActiveAccount();
        ActiveAccountName = activeAcc?.Username ?? "Steve";
        ActiveAccountType = activeAcc?.Type.ToString() ?? "Offline";
        SelectedAccount = activeAcc;

        AccountRows.Clear();
        foreach (var acc in allAccs)
        {
            var row = new AccountRow(acc) { IsActive = activeAcc != null && acc.Id == activeAcc.Id };
            AccountRows.Add(row);
            _ = row.LoadHeadAsync();
        }
        HasNoAccounts = AccountRows.Count == 0;
        OnPropertyChanged(nameof(ActiveAccountRow));

        Instances.Clear();
        foreach (var inst in _instanceService.GetInstances())
        {
            inst.IsSelected = (inst.Id == SelectedInstanceId);
            Instances.Add(inst);
        }

        HasInstances = Instances.Count > 0;

        var selected = _instanceService.GetSelectedInstance();
        if (selected != null)
        {
            SelectedInstance = selected;
            SelectedInstanceId = selected.Id;
            SelectedInstanceName = selected.Name;
            SelectedInstanceDetails = selected.DisplayVersion;
        }
        else
            {
                SelectedInstance = null;
                SelectedInstanceId = "";
                SelectedInstanceName = L10n.T("mw_no_profile");
                SelectedInstanceDetails = L10n.T("ei_click_create");
                IsRightSidebarOpen = false;
            }
    }

    [RelayCommand]
    private void SelectInstance(InstanceModel instance)
    {
        if (instance == null) return;

        _instanceService.SetSelectedInstance(instance.Id);
        SelectedInstanceId = instance.Id;
        foreach (var inst in Instances)
            inst.IsSelected = (inst.Id == instance.Id);

        SelectedInstance = instance;
        SelectedInstanceName = instance.Name;
        SelectedInstanceDetails = instance.DisplayVersion;
        IsRightSidebarOpen = true;
    }

    [RelayCommand]
    private void CloseRightSidebar() => IsRightSidebarOpen = false;

    [RelayCommand]
    private void CreateDesktopShortcut(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                StatusText = L10n.T("mw_status_shortcut_os");
                return;
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutPath = Path.Combine(desktop, $"{inst.Name}.lnk");
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;
            var workDir = AppDomain.CurrentDomain.BaseDirectory;

            // Default: the launcher's own (white) icon. If the profile has a custom
            // icon, build a real .ico for it so Explorer shows it on the shortcut.
            var iconLocation = $"{Esc(exePath)},0";
            var customIco = ShortcutIconService.GetOrCreateInstanceIco(inst);
            if (!string.IsNullOrEmpty(customIco))
                iconLocation = $"{Esc(customIco)},0";

            // .NET's dynamic COM dispatch to WScript.Shell is unreliable in Avalonia apps,
            // so generate the .lnk via a small PowerShell script instead.
            var script =
                "$ws = New-Object -ComObject WScript.Shell\n" +
                $"$sc = $ws.CreateShortcut('{Esc(shortcutPath)}')\n" +
                $"$sc.TargetPath = '{Esc(exePath)}'\n" +
                $"$sc.Arguments = '--launch \"{inst.Name}\"'\n" +
                $"$sc.WorkingDirectory = '{Esc(workDir)}'\n" +
                $"$sc.IconLocation = '{iconLocation}'\n" +
                $"$sc.Description = 'Launch {Esc(inst.Name)} (Minecraft {inst.Version}) - Zenith Launcher'\n" +
                "$sc.Save()\n";

            var scriptPath = Path.Combine(Path.GetTempPath(), $"zenith_shortcut_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(scriptPath, script);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -NoLogo -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null && !p.WaitForExit(15000))
                    p.Kill(true);
                StatusText = File.Exists(shortcutPath)
                    ? string.Format(L10n.T("mw_status_shortcut_done"), inst.Name)
                    : L10n.T("mw_status_shortcut_failed");
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to create shortcut", ex);
            StatusText = L10n.T("mw_status_shortcut_failed");
        }

        static string Esc(string s) => s.Replace("'", "''");
    }

    [RelayCommand]
    private void EditInstance(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        SelectInstance(inst);
        OpenEditInstanceWindow(inst);
    }

    private void OpenEditInstanceWindow(InstanceModel instance)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var viewModel = new EditInstanceViewModel(instance, this, _instanceService);
            var window = new Views.EditInstanceWindow { DataContext = viewModel };
            _editInstanceWindows.Add(window);
            window.Closed += (_, _) =>
            {
                _editInstanceWindows.Remove(window);
                Services.DiscordPresenceService.SetBrowsingProfiles();
            };
            Services.DiscordPresenceService.SetEditingInstance(instance.Name);
            window.ShowDialog(desktop.MainWindow);
        }
    }

    public void CloseEditInstanceWindow(EditInstanceViewModel viewModel)
    {
        var window = _editInstanceWindows.FirstOrDefault(w => ReferenceEquals(w.DataContext, viewModel));
        if (window != null)
            window.Close();
    }

    [RelayCommand]
    private void OpenCopyDialog(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var vm = new CloneProfileViewModel(inst, _instanceService);
                vm.Cloned += created =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        RefreshState();
                        SelectInstance(created);
                        StatusText = string.Format(L10n.T("mw_status_cloned"), created.Name);
                    });
                };
                var win = new Views.CloneProfileWindow { DataContext = vm };
                vm.RequestClose += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => win.Close());
                win.ShowDialog(desktop.MainWindow);
                return;
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to open Clone Profile window", ex);
        }
        StatusText = L10n.T("mw_status_clone_open_failed");
    }

    [RelayCommand]
    private async Task CopyConsoleLogsAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(window)?.Clipboard;
            if (clipboard != null)
            {
                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.CreateText(ConsoleLogs ?? string.Empty));
                await clipboard.SetDataAsync(transfer);
            }
        }
    }

    [RelayCommand]
    private void CloseConsoleDialog() => IsConsoleDialogOpen = false;

    [RelayCommand]
    private void SwitchAccount(AccountModel account)
    {
        if (account == null) return;
        _accountService.SetActiveAccount(account.Id);
        RefreshState();
    }

    [RelayCommand]
    private void DeleteAccount(AccountModel account)
    {
        if (account == null) return;
        _accountService.RemoveAccount(account.Id);
        RefreshState();
    }

    /// <summary>True while the right panel of the accounts window shows the offline-name form.</summary>
    /// <summary>True when the accounts dialog should show its empty-state hint.</summary>
    [ObservableProperty]
    private bool _hasNoAccounts;

    [ObservableProperty]
    private bool _isOfflinePanelOpen = false;

    [RelayCommand]
    private async Task AddOfflineAccountAsync()
    {
        if (!string.IsNullOrWhiteSpace(NewOfflineUsername))
        {
            var acc = await _accountService.AddOfflineAccountAsync(NewOfflineUsername.Trim());
            _accountService.SetActiveAccount(acc.Id);
            NewOfflineUsername = "";
            IsOfflinePanelOpen = false;
            RefreshState();
        }
    }

    [RelayCommand]
    private void ShowOfflinePanel() => IsOfflinePanelOpen = true;

    /// <summary>
    /// Hard-resets every in-progress sign-in flag so the accounts UI unlocks
    /// immediately - used when the accounts dialog closes while a browser
    /// sign-in is still waiting (tab closed, browser closed, error, timeout).
    /// </summary>
    public void ResetAuthUiState()
    {
        IsAccountsBusy = false;
        AuthErrorText = "";
    }

    [RelayCommand]
    private void CancelOfflinePanel() => IsOfflinePanelOpen = false;

    [RelayCommand]
    private async Task LoginElyByAsync()
    {
        if (IsAccountsBusy)
        {
            StatusText = L10n.T("mw_status_signin_busy");
            return;
        }
        IsAccountsBusy = true;
        AuthErrorText = "";
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _authCts = cts;
        try
        {
            StatusText = L10n.T("acc_status_ely_browser");
            await _accountService.AuthenticateElyByOAuthAsync(cts.Token);
            RefreshState();
            StatusText = L10n.T("acc_status_ely_added");
        }
        catch (OperationCanceledException)
        {
            var msg = L10n.T("mw_status_signin_timeout");
            StatusText = msg;
            AuthErrorText = msg;
        }
        catch (InvalidOperationException ex)
        {
            LauncherLog.Error("Ely.by login failed", ex);
            StatusText = ex.Message;
            AuthErrorText = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            LauncherLog.Error("Ely.by login failed", ex);
            var msg = L10n.T("acc_status_conn_unreachable");
            StatusText = msg;
            AuthErrorText = msg;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Ely.by login failed", ex);
            var msg = $"Ely.by login failed ({ex.GetType().Name}). See launcher.log for details.";
            StatusText = msg;
            AuthErrorText = msg;
        }
        finally
        {
            // Clear only our own CTS: a newer sign-in may have replaced _authCts.
            if (ReferenceEquals(_authCts, cts)) _authCts = null;
            IsAccountsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoginMicrosoftAsync() => await LoginMicrosoftInternalAsync();
    private async Task LoginMicrosoftInternalAsync()
    {
        if (IsAccountsBusy)
        {
            StatusText = L10n.T("mw_status_signin_busy");
            return;
        }
        IsAccountsBusy = true;
        AuthErrorText = "";
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // browser sign-in takes time
        _authCts = cts;
        try
        {
            StatusText = "Signing in with Microsoft...";
            var acc = await _accountService.AuthenticateMicrosoftAsync(cts.Token);
            _accountService.SetActiveAccount(acc.Id);
            RefreshState();
            StatusText = L10n.T("acc_status_ms_added");
        }
        catch (OperationCanceledException)
        {
            var msg = L10n.T("mw_status_signin_timeout");
            StatusText = msg;
            AuthErrorText = msg;
        }
        catch (InvalidOperationException ex)
        {
            LauncherLog.Error("Microsoft login failed", ex);
            StatusText = ex.Message;
            AuthErrorText = ex.Message;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Microsoft login failed", ex);
            var msg = string.Format(L10n.T("acc_status_ms_failed"), ex.GetType().Name);
            StatusText = msg;
            AuthErrorText = msg;
        }
        finally
        {
            // Clear only our own CTS: a newer sign-in may have replaced _authCts.
            if (ReferenceEquals(_authCts, cts)) _authCts = null;
            IsAccountsBusy = false;
        }
    }

    [RelayCommand]
    private void StartRenameInstance(InstanceModel instance)
    {
        if (instance == null) return;
        instance.IsEditingName = true;
    }

    [RelayCommand]
    private async Task FinishRenameInstanceAsync(InstanceModel instance)
    {
        if (instance == null) return;
        instance.IsEditingName = false;

        // Check for duplicate name
        var allInstances = _instanceService.GetInstances();
        if (allInstances.Any(i => i.Id != instance.Id && i.Name.Equals(instance.Name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = string.Format(L10n.T("mw_status_name_exists"), instance.Name);
            return;
        }

        await _instanceService.SaveInstanceAsync(instance);
        RefreshState();
    }

    [RelayCommand]
    private async Task ChangeInstanceIconAsync(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider != null)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Profile Icon",
                AllowMultiple = false,
                FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
            });

            if (files.Count > 0)
            {
                var srcFile = files[0].Path.LocalPath;
                var targetExt = Path.GetExtension(srcFile);
                var destIcon = Path.Combine(inst.Path, $"icon{targetExt}");
                Directory.CreateDirectory(inst.Path);
                File.Copy(srcFile, destIcon, true);
                inst.IconPath = destIcon;
                await _instanceService.SaveInstanceAsync(inst);
                RefreshState();
            }
        }
    }

    [RelayCommand]
    private async Task LaunchSpecificInstanceAsync(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        SelectInstance(inst);
        await LaunchGameAsync();
    }

    [RelayCommand]
    private void KillInstance(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        _launchingInstanceIds.Remove(inst.Id);
        inst.IsLaunching = false;
        _launchService.KillProcess(inst.Id);
        inst.IsRunning = false;
        OnPropertyChanged(nameof(CanLaunch));
        if (ReferenceEquals(SelectedInstance, inst))
            IsLaunching = false;
    }

    [RelayCommand]
    private void OpenProfileFolder(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst != null && Directory.Exists(inst.Path))
        {
            Process.Start(new ProcessStartInfo { FileName = inst.Path, UseShellExecute = true, Verb = "open" });
        }
    }

    [RelayCommand]
    private async Task ExportCurseForgeAsync(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        await ExportZipArchiveAsync(inst, "CurseForge", $"{inst.Name}-CurseForge.zip");
    }

    [RelayCommand]
    private async Task ExportModrinthAsync(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        await ExportZipArchiveAsync(inst, "Modrinth", $"{inst.Name}.mrpack");
    }

    [RelayCommand]
    private async Task ExportPrismAsync(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        await ExportZipArchiveAsync(inst, "Prism", $"{inst.Name}-Prism.zip");
    }

    private async Task ExportZipArchiveAsync(InstanceModel instance, string format, string defaultFileName)
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider != null)
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = $"Export to {format}",
                SuggestedFileName = defaultFileName
            });

            if (file != null)
            {
                var targetPath = file.Path.LocalPath;
                if (File.Exists(targetPath)) File.Delete(targetPath);

                // Only export relevant modpack folders, not full game directory
                var relevantFolders = new[] { "mods", "config", "scripts", "kubejs", "shaderpacks", "resourcepacks" };
                await Task.Run(() =>
                {
                    using var archive = ZipFile.Open(targetPath, ZipArchiveMode.Create);
                    foreach (var folder in relevantFolders)
                    {
                        var folderPath = Path.Combine(instance.Path, folder);
                        if (Directory.Exists(folderPath))
                            AddDirectoryToZip(archive, folderPath, folder);
                    }
                    // Include manifest files if they exist
                    foreach (var manifest in new[] { "manifest.json", "modrinth.index.json" })
                    {
                        var manifestPath = Path.Combine(instance.Path, manifest);
                        if (File.Exists(manifestPath))
                            archive.CreateEntryFromFile(manifestPath, manifest);
                    }
                });
            }
        }
    }

    [RelayCommand]
    private void ConfirmDeleteProfile()
    {
        var inst = PendingDeleteInstance;
        if (inst == null) return;
        _instanceService.DeleteInstance(inst.Id);
        IsDeleteConfirmOpen = false;
        PendingDeleteInstance = null;
        RefreshState();
        ModpacksBrowser?.UpdateInstalledFlags();
    }

    [RelayCommand]
    private void CancelDeleteProfile()
    {
        IsDeleteConfirmOpen = false;
        PendingDeleteInstance = null;
    }

    [RelayCommand]
    private void RequestDeleteProfile(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        PendingDeleteInstance = inst;
        IsDeleteConfirmOpen = true;
    }

    [RelayCommand]
    private void OpenRootDirectory()
    {
        var dir = ZenithPaths.AppDataDir;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true, Verb = "open" });
    }

    [RelayCommand]
    private void ToggleVersionPicker() => IsVersionPickerOpen = !IsVersionPickerOpen;

    [RelayCommand]
    private void SelectVersion(string version)
    {
        SelectedVersion = version;
        IsVersionPickerOpen = false;
        IsElyByErrorOpen = false;
    }

    [RelayCommand]
    private void ClearVersion()
    {
        SelectedVersion = "";
    }

    [RelayCommand]
    private void OpenNewInstanceDialog()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var vm = new CreateProfileViewModel(_instanceService, _modLoaderService,
                    UseFlatVersionList, ShowReleases, ShowSnapshots, ShowBetas, ShowAlphas);
                vm.ProfileCreated += inst =>
                {
                    _instanceService.SetSelectedInstance(inst.Id);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshState());
                };
                var win = new Views.CreateProfileWindow { DataContext = vm };
                vm.RequestClose += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => win.Close());
                win.Closed += (_, _) => Services.DiscordPresenceService.SetBrowsingProfiles();
                Services.DiscordPresenceService.SetCreatingInstance();
                win.ShowDialog(desktop.MainWindow);
                return;
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to open Create Profile window", ex);
        }
        // Fallback to old overlay
        FilterVersions();
        IsVersionPickerOpen = false;
        IsElyByErrorOpen = false;
        IsCreateDialogOpen = true;
        _ = UpdateAvailableLoadersAsync(SelectedVersion);
    }

    [RelayCommand]
    private void OpenSettingsDialog()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var win = new Views.LauncherSettingsWindow { DataContext = this };
            win.Closed += (_, _) => Services.DiscordPresenceService.SetBrowsingProfiles();
            Services.DiscordPresenceService.SetInSettings();
            win.ShowDialog(desktop.MainWindow);
        }
    }

    [RelayCommand]
    private void OpenJavaInstaller()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var win = new Views.JavaInstallerWindow { DataContext = this };
            win.Closed += (_, _) => Services.DiscordPresenceService.SetBrowsingProfiles();
            Services.DiscordPresenceService.SetChoosingVersion();
            win.ShowDialog(desktop.MainWindow);
        }
    }

    [RelayCommand]
    private void OpenAbout() => IsAboutOpen = true;

    [RelayCommand]
    private void OpenAccountsDialog()
    {
        RefreshState();
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var win = new Views.AccountsWindow { DataContext = this };
            // Automatic sign-in cancellation: when the dialog closes - manually or
            // together with the main window - any in-flight browser sign-in is cancelled
            // and its state is hard-reset, unlocking the UI without reopening the dialog.
            win.Closed += (_, _) =>
            {
                try { _authCts?.Cancel(); } catch { /* already disposed */ }
                ResetAuthUiState();
                Services.DiscordPresenceService.SetBrowsingProfiles();
            };
            Services.DiscordPresenceService.SetCreatingAccount();
            win.ShowDialog(desktop.MainWindow);
        }
    }

    [RelayCommand]
    private void CloseDialogs()
    {
        SaveLauncherConfig();
        IsCreateDialogOpen = false;
        IsAboutOpen = false;
        IsVersionPickerOpen = false;
        IsElyByErrorOpen = false;
        IsDeleteConfirmOpen = false;
        PendingDeleteInstance = null;
        FilterVersions();
    }

    [RelayCommand]
    private async Task ConfirmCreateInstanceAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedVersion))
        {
            StatusText = L10n.T("mw_status_need_version");
            return;
        }

        var name = NewInstanceName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = L10n.T("ph_default_profile");

        var allNames = _instanceService.GetInstances().Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var finalName = name;
        if (allNames.Contains(finalName))
        {
            int idx = 1;
            while (allNames.Contains($"{finalName}_{idx}"))
                idx++;
            finalName = $"{finalName}_{idx}";
        }

        var inst = await _instanceService.CreateInstanceAsync(finalName, SelectedVersion, SelectedLoader, SelectedLoaderBuild);
        if (inst == null)
        {
            StatusText = string.Format(L10n.T("mw_status_name_exists"), finalName);
            return;
        }
        _instanceService.SetSelectedInstance(inst.Id);
        CloseDialogs();
        RefreshState();

        // Reset create dialog form state
        NewInstanceName = "";
        SelectedVersion = "";
        SelectedLoader = "Vanilla";
        SelectedLoaderBuild = "";
        ShowLoaderBuilds = false;
    }

    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        var instance = SelectedInstance ?? _instanceService.GetSelectedInstance();
        var account = _accountService.GetActiveAccount() ?? await _accountService.AddOfflineAccountAsync(ActiveAccountName);

        if (instance == null) return;
        if (instance.IsRunning || _launchingInstanceIds.Contains(instance.Id)) return;

        instance.MaxMemoryMb = instance.RamMb ?? RamMb;

        _launchingInstanceIds.Add(instance.Id);
        instance.IsLaunching = true;
        if (ReferenceEquals(SelectedInstance, instance))
            IsLaunching = true;
        LaunchProgress = 0;
        Services.DiscordPresenceService.SetLaunchingGame(instance.Name);

        try
        {
            if (SelectedJavaMode == "Custom" && (string.IsNullOrWhiteSpace(CustomJavaPath) || !File.Exists(CustomJavaPath)))
            {
                StatusText = L10n.T("mw_status_java_invalid");
                return;
            }

            var process = await Task.Run(() => _launchService.LaunchAsync(instance, account));
            StatusText = string.Format(L10n.T("mw_status_launched"), instance.Name);
        }
        catch (HttpRequestException ex)
        {
            if (SelectedAccount?.Type == Models.AccountType.ElyBy)
            {
                IsElyByErrorOpen = true;
                StatusText = L10n.T("mw_status_conn_error");
                return;
            }
            StatusText = string.Format(L10n.T("mw_status_http_error"), ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            ConsoleLogs += $"[ERROR] {ex.Message}\n";
            ConsoleLogs += $"[ERROR] Stack: {ex}\n";
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Launch failed", ex);
            ConsoleLogs += $"[CRITICAL ERROR] {ex}\n";
            StatusText = string.Format(L10n.T("mw_status_launch_failed"), ex.Message);
        }
        finally
        {
            _launchingInstanceIds.Remove(instance.Id);
            instance.IsLaunching = false;
            if (ReferenceEquals(SelectedInstance, instance))
                IsLaunching = false;
            OnPropertyChanged(nameof(CanLaunch));
            // Launch aborted before the process started - return to browsing.
            if (!instance.IsRunning)
                Services.DiscordPresenceService.SetBrowsingProfiles();
        }
    }

    private static void AddDirectoryToZip(ZipArchive archive, string sourceDir, string entryPrefix)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            archive.CreateEntryFromFile(file, Path.Combine(entryPrefix, relativePath));
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [RelayCommand]
    private async Task LaunchOrCancelAsync()
    {
        if (IsLaunching)
        {
            var instance = SelectedInstance ?? _instanceService.GetSelectedInstance();
            if (instance != null)
            {
                _launchingInstanceIds.Remove(instance.Id);
                instance.IsLaunching = false;
                _launchService.KillProcess(instance.Id);
                instance.IsRunning = false;
            }
            IsLaunching = false;
            StatusText = L10n.T("mw_status_launch_cancelled");
        }
        else
        {
            await LaunchGameAsync();
        }
    }
}
