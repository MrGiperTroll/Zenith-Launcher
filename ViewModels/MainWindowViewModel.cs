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

using Avalonia.Media;

using Avalonia.Platform.Storage;

using Avalonia.Threading;

using CmlLib.Core;

using CommunityToolkit.Mvvm.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using ZenithLauncher.Models;

using ZenithLauncher.Services;

using ZenithLauncher.UI.Theming;

using ZenithLauncher.ViewModels.Common;

using ZenithLauncher.ViewModels.Pages;

using ZenithLauncher.Views;



namespace ZenithLauncher.ViewModels;



public partial class MainWindowViewModel : ViewModelBase

{

    private bool _isLoadingConfig = false;

    private readonly IAccountService _accountService;

    private readonly IInstanceService _instanceService;

    private readonly ILaunchService _launchService;

    private readonly ModLoaderService _modLoaderService;

    private readonly List<(string Name, string Type)> _cachedVersions = new();



    [ObservableProperty]

    private string _statusText = string.Empty;



    public string VersionLabel { get; } =

        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";



    [ObservableProperty]

    private string _launcherName = "Zenith Launcher";



    partial void OnLauncherNameChanged(string value)

    {

        OnPropertyChanged(nameof(WindowTitle));

        SaveLauncherConfig();

    }



    public string WindowTitle => $"{LauncherName} v{VersionLabel}";



    [RelayCommand]

    public void ResetLauncherName() => LauncherName = "Zenith Launcher";

    public ObservableCollection<AccountModel> Accounts => AccountsVm.Accounts;



    public AccountModel? SelectedAccount

    {

        get => AccountsVm.SelectedAccount;

        set => AccountsVm.SelectedAccount = value;

    }



    public string ActiveAccountName => AccountsVm.ActiveAccountName;



    public string ActiveAccountType => AccountsVm.ActiveAccountType;



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

        IsSearchMemoryVisible = "memory ram allocated память озу".Contains(q);

        IsSearchResolutionVisible = "resolution window size разрешение экран".Contains(q);

        IsSearchFullscreenVisible = "fullscreen mode полноэкранный".Contains(q);

        IsSearchJavaVisible = "java runtime manage automatic download джава".Contains(q);

        IsSearchDirectoryVisible = "directory strategy isolation директория папка".Contains(q);

        IsSearchVersionFiltersVisible = "version filters releases snapshots версии фильтры".Contains(q);

        IsSearchJvmArgsVisible = "jvm arguments flags аргументы jvm".Contains(q);

        IsSearchLanguageVisible = "language localization english russian ukrainian turkish german french язык".Contains(q);

        IsSearchDiscordVisible = "discord rpc rich presence application id дискорд".Contains(q);

        IsSearchAppearanceVisible = "appearance theme color accent name branding внешний вид тема цвет акцент оформление launcher лаунчер настройки".Contains(q);

    }



    [ObservableProperty] private bool _autoManageJava = true;



    partial void OnAutoManageJavaChanged(bool value)

    {

        LauncherLog.Info($"Automatic Java management {(value ? "enabled" : "disabled")}.");

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



    public static string AppVersion =>

        typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";



    public ModpacksBrowserViewModel ModpacksBrowser { get; private set; } = null!;



    [ObservableProperty]

    private bool _isDeleteConfirmOpen;



    [ObservableProperty]

    private InstanceModel? _pendingDeleteInstance;

    [ObservableProperty] private bool _isCloneDialogOpen;

    [ObservableProperty] private CloneProfileViewModel? _cloneDialogVm;



    public ObservableCollection<AccountRow> AccountRows => AccountsVm.AccountRows;



    public AccountRow? ActiveAccountRow => AccountsVm.ActiveAccountRow;



    public ThemeSettingsViewModel ThemeSettingsVm { get; }

    public LauncherSettingsViewModel LauncherSettingsVm { get; }

    public AccountsViewModel AccountsVm { get; }



    public Avalonia.Media.Imaging.Bitmap? PrismImportIcon { get; } = ImportIconService.GetPrismIcon();

    public Avalonia.Media.Imaging.Bitmap? MultiMCImportIcon { get; } = ImportIconService.GetMultiMCIcon();



    public MainWindowViewModel()

    {

        ThemeSettingsVm = new ThemeSettingsViewModel

        {

            GetConfigSnapshot = () => GetLauncherConfigSnapshot(),

            SaveConfigRequested = () => SaveLauncherConfig()

        };

        LauncherSettingsVm = new LauncherSettingsViewModel(ThemeSettingsVm)

        {

            SaveConfigRequested = () => SaveLauncherConfig(),

            VersionFiltersChanged = () => FilterVersions(),

            LanguageChanged = lang => SelectedLanguage = lang,

            DiscordRpcChanged = enabled => DiscordRpcEnabled = enabled,

            ScaleChanged = scale => LauncherScale = scale,

            OpenJavaInstallerRequested = () => OpenJavaInstaller(),
            DownloadJavaRequested = async (major, prog) =>
            {
                return await DownloadJavaCoreAsync(major, force: true, progress: prog);
            },

            GetRecommendedJavaDisplay = () =>

            {

                var inst = SelectedInstance ?? _instanceService?.GetSelectedInstance();

                return inst != null

                    ? Services.JavaVersionHelper.RecommendedDisplay(inst.Version)

                    : L10n.T("java_display_auto");

            },

            GetWindowTitle = () => WindowTitle

        };



        LoadLauncherConfig();



        // Force-apply the language even when the loaded value equals the

        // field default ("English") — CommunityToolkit skips the change

        // notification in that case, so OnSelectedLanguageChanged never

        // fires and the UI stays on DetectSystemCode() (the OS language).

        Services.L10n.ApplyDisplay(SelectedLanguage);



        // Recompute language-dependent display strings AFTER the final language

        // is applied — LoadFromConfig above may have rendered them in the OS language.

        LauncherSettingsVm.UpdateJavaDisplay();



        _accountService = new AccountService();

        AccountsVm = new AccountsViewModel(_accountService);

        AccountsVm.AccountStateChanged += () =>

        {

            OnPropertyChanged(nameof(ActiveAccountName));

            OnPropertyChanged(nameof(ActiveAccountType));

            OnPropertyChanged(nameof(ActiveAccountRow));

            OnPropertyChanged(nameof(SelectedAccount));

            OnPropertyChanged(nameof(Accounts));

            OnPropertyChanged(nameof(AccountRows));

        };

        AccountsVm.OnStatusMessage += msg => StatusText = msg;

        _instanceService = new InstanceService();

        _launchService = new LaunchService((IInstanceService)_instanceService);

        _modLoaderService = new ModLoaderService();



        // Re-translate computed UI strings when the language changes.

        L10n.LanguageChanged += () => OnPropertyChanged(nameof(LaunchButtonText));

        L10n.LanguageChanged += () =>

        {

            if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)

                RebuildBreadcrumbs(_navHistory[_historyIndex]);

            CreateServerVm?.UpdatePluginsTabTitle();

        };



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



        // Console log lines are enqueued cheaply from any (reader) thread and flushed

        // to the UI in batches every 250 ms. Posting every single line to the UI thread

        // freezes the launcher for chatty modern versions (huge log floods) and lets the

        // game's stdout/stderr pipe buffer fill up. Batching keeps both the UI responsive

        // and the pipe drained so the game never blocks waiting on the launcher.

        _launchService.LogReceived += log =>

        {

            _consolePending.Enqueue(log);

        };



        _consoleFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

        _consoleFlushTimer.Tick += (_, _) => FlushConsolePending();

        _consoleFlushTimer.Start();



        ModLoaderService.Diagnostic += msg =>

        {

            Dispatcher.UIThread.Post(() =>

            {

                ConsoleLogs += $"[ModLoader] {msg}" + Environment.NewLine;

            });

        };



        // Discord Rich Presence lifecycle - initialize in background to avoid UI thread pipe blocking

        Task.Run(() =>
        {
            try
            {
                Services.DiscordPresenceService.Initialize(DiscordRpcEnabled);
                Services.DiscordPresenceService.SetBrowsingProfiles();
            }
            catch (Exception ex)
            {
                LauncherLog.Error("Discord RPC background init failed", ex);
            }
        });

        _launchService.GameLaunched += (instance, accountType) =>

            Services.DiscordPresenceService.SetPlaying(instance.Name, instance.Version, accountType, DateTime.UtcNow);

        _launchService.GameExited += (_, _) =>
        {
            Services.DiscordPresenceService.SetBrowsingProfiles();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!IsConsoleDialogOpen)
                {
                    ConsoleLogs = string.Empty;
                    while (_consolePending.TryDequeue(out _)) { }
                }
                Services.MemoryTrimmer.TrimMemory(force: true);
            });
        };

        



        RefreshState();



        // Silent startup session maintenance: renew expired tokens in the

        // background so returning users never see a re-login prompt.

        _ = Task.Run(async () =>

        {

            try

            {

                await Task.Delay(3000).ConfigureAwait(false);

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

                ModpacksBrowser.UpdateInstalledFlags();

            });

        });

        ModpacksBrowser.GetCurseForgeApiKey = () => LauncherSettingsVm?.CurseForgeApiKey ?? "";

        ModpacksBrowser.OnProjectSelectedChanged = _ =>

        {

            if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)

            {

                var cur = _navHistory[_historyIndex];

                if (cur.Page == LauncherNavPage.Modpacks)

                    RebuildBreadcrumbs(cur);

            }

        };

        ModpacksBrowser.OnAuthorProfileChanged = _ =>

        {

            if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)

            {

                var cur = _navHistory[_historyIndex];

                if (cur.Page == LauncherNavPage.Modpacks)

                    RebuildBreadcrumbs(cur);

            }

        };



        NavigateToProfiles();

        _ = CheckForUpdatesBackgroundAsync();



        // Final guarantee: re-apply the saved theme after all startup loading,

        // so no intermediate default state can stick.

        try { ZenithTheme.Apply(GetLauncherConfigSnapshot()); } catch { }

    }



    private async Task CheckForUpdatesBackgroundAsync()

    {

        try

        {

            var args = Environment.GetCommandLineArgs();
            if (args.Any(a => a.Equals("--test-update", StringComparison.OrdinalIgnoreCase) || a.Equals("--mock-update", StringComparison.OrdinalIgnoreCase)))
            {
                await Task.Delay(400);
                Avalonia.Threading.Dispatcher.UIThread.Post(OpenMockUpdate);
                return;
            }

            await Task.Delay(5000);

            var service = new GitHubUpdateService();

            var result = await service.CheckForUpdatesAsync();

            if (result is { IsNewerThanCurrent: true })

            {

                Avalonia.Threading.Dispatcher.UIThread.Post(() => OpenUpdate(result));

            }

        }

        catch (OperationCanceledException) { }

        catch (Exception ex)

        {

            LauncherLog.Error("Background update check failed.", ex);

        }

    }



    [ObservableProperty]

    private Views.UpdateNotificationViewModel? _updateVm;



    [ObservableProperty]

    private bool _isUpdateOpen;



    [ObservableProperty]

    private bool _isUpdateAvailable;



    [ObservableProperty]

    private bool _isUpdateChecking;



    [ObservableProperty]

    private bool _isInfoDialogOpen;



    [ObservableProperty]

    private string _infoDialogTitle = "";



    [ObservableProperty]

    private string _infoDialogMessage = "";



    [RelayCommand]

    private void CloseInfoDialog() => IsInfoDialogOpen = false;



    public void ShowInfoDialog(string title, string message)

    {

        InfoDialogTitle = title;

        InfoDialogMessage = message;

        IsInfoDialogOpen = true;

    }



    [RelayCommand]

    private void CloseUpdate()

    {

        IsUpdateOpen = false;

        IsUpdateAvailable = false;

    }



    [RelayCommand]

    private async Task CheckForUpdatesNowAsync()

    {

        if (IsUpdateChecking) return;

        IsUpdateChecking = true;

        StatusText = L10n.T("update_checking");

        try

        {

            var args = Environment.GetCommandLineArgs();
            if (args.Any(a => a.Equals("--test-update", StringComparison.OrdinalIgnoreCase) || a.Equals("--mock-update", StringComparison.OrdinalIgnoreCase)))
            {
                await Task.Delay(200);
                OpenMockUpdate();
                return;
            }

            var result = await new GitHubUpdateService().CheckForUpdatesAsync();

            if (result?.IsNewerThanCurrent == true)

            {

                OpenUpdate(result);

            }

            else

            {

                StatusText = L10n.T("update_uptodate");

                ShowInfoDialog(L10n.T("update_btn"), L10n.T("update_uptodate"));

            }

        }

        catch (Exception ex)

        {

            LauncherLog.Error("Manual update check failed.", ex);

            StatusText = L10n.T("update_failed");

            ShowInfoDialog(L10n.T("update_btn"), L10n.T("update_failed"));

        }

        finally

        {

            IsUpdateChecking = false;

        }

    }



    public void OpenMockUpdate()
    {
        var result = new UpdateCheckResult(
            Version: "1.2.0",
            DownloadUrl: "https://github.com/MrGiperTroll/Zenith-Launcher/releases/tag/v1.2.0",
            ReleaseNotes: "## ru\n• Переработан интерфейс лаунчера и дизайн окон\n• Улучшена производительность загрузки профилей\n• Добавлена поддержка новых версий Fabric и Forge\n• Оптимизировано потребление оперативной памяти\n\n## en\n• Redesigned launcher interface and window styles\n• Improved profile loading performance\n• Added support for newer Fabric and Forge builds\n• Optimized memory consumption\n\n## uk\n• Оновлено інтерфейс лаунчера та оформлення вікон\n• Покращено швидкодію завантаження профілів\n• Додано підтримку нових версій Fabric та Forge\n• Оптимізовано споживання оперативної пам'яті",
            IsNewerThanCurrent: true);

        OpenUpdate(result);
    }

    private void OpenUpdate(UpdateCheckResult result)

    {

        UpdateVm = new Views.UpdateNotificationViewModel(result, () =>

        {

            IsUpdateOpen = false;

            IsUpdateAvailable = false;

        });

        IsUpdateAvailable = true;

        IsUpdateOpen = true;

    }



    private async Task InitializeModLoadersAsync()

    {

        await Task.Delay(2000).ConfigureAwait(false);

        await _modLoaderService.InitializeDatabaseAsync().ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await UpdateAvailableLoadersAsync(SelectedVersion);
        });

    }


    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)

    {

        base.OnPropertyChanged(e);

        if (e.PropertyName is "JvmArgs" or "MinRamMb" or "RamMb" or "SelectedLaunchBehavior" or "GameWidth" or "GameHeight" or "IsFullscreen" or "CustomJavaPath" or "SelectedJavaMode"

            or "AutoManageJava" or "DiscordRpcEnabled" or "SelectedLanguage" or "LauncherScale" or "SelectedParticle")

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

                LauncherName = LauncherName,

                AccentColor = CurrentAccentHex,

                BgColor = BgHex,

                SurfaceColor = SurfaceHex,

                PanelColor = PanelHex,

                SidebarBgColor = SidebarBgHex,

                CardColor = CardHex,

                FieldColor = FieldHex,

                HoverColor = HoverHex,

                TextPrimaryColor = TextPrimaryHex,

                TextSecondaryColor = TextSecondaryHex,

                BorderDarkColor = BorderDarkHex,

                JvmArgs = JvmArgs,

                MinRamMb = MinRamMb,

                RamMb = RamMb,

                LaunchBehavior = SelectedLaunchBehavior,

                GameWidth = GameWidth,

                GameHeight = GameHeight,

                IsFullscreen = IsFullscreen,

                SelectedDirectoryMode = SelectedDirectoryMode,

                JavaMode = SelectedJavaMode,

                CustomJavaPath = CustomJavaPath,

                UseFlatVersionList = UseFlatVersionList,

                AutoManageJava = AutoManageJava,

                DiscordRpcEnabled = DiscordRpcEnabled,

                LauncherScale = LauncherScale,

                SelectedParticle = SelectedParticle,

                ParticleCount = ParticleCount,

                ParticleScale = ParticleScale,

                ParticleSpeed = ParticleSpeed,

                ParticleColorHex = ParticleColorHex,

                ParticleAngleDeg = ParticleAngleDeg,

                ParticleTexturePath = ParticleTexturePath,

                Language = SelectedLanguage,

                ThemeSubTab = ThemeSubTab,

                SimpleAccentColor = _savedSimpleAccentHex,

                IsSidebarExpanded = IsSidebarExpanded

            };

            LauncherSettingsVm?.ApplyToConfig(configData);

            ThemeSettingsVm?.ApplyToConfig(configData);

            File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(configData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        }

        catch { }

    }



    /// <summary>

    /// Snapshot of the current global launcher settings, used by the instance editor

    /// so per-instance settings inherit the global defaults unless explicitly overridden.

    /// </summary>

    public LauncherConfigData GetLauncherConfigSnapshot()

    {

        var snapshot = new LauncherConfigData

        {

            LauncherName = LauncherName,

            AccentColor = CurrentAccentHex,

            BgColor = BgHex,

            SurfaceColor = SurfaceHex,

            PanelColor = PanelHex,

            SidebarBgColor = SidebarBgHex,

            CardColor = CardHex,

            FieldColor = FieldHex,

            HoverColor = HoverHex,

            TextPrimaryColor = TextPrimaryHex,

            TextSecondaryColor = TextSecondaryHex,

            BorderDarkColor = BorderDarkHex,

            JvmArgs = JvmArgs,

            MinRamMb = MinRamMb,

            RamMb = RamMb,

            LaunchBehavior = SelectedLaunchBehavior,

            GameWidth = GameWidth,

            GameHeight = GameHeight,

            IsFullscreen = IsFullscreen,

            SelectedDirectoryMode = SelectedDirectoryMode,

            JavaMode = SelectedJavaMode,

            CustomJavaPath = CustomJavaPath,

            UseFlatVersionList = UseFlatVersionList,

            AutoManageJava = AutoManageJava,

            DiscordRpcEnabled = DiscordRpcEnabled,

            LauncherScale = LauncherScale,

            SelectedParticle = SelectedParticle,

            Language = SelectedLanguage,

            ThemeSubTab = ThemeSubTab,

            IsSidebarExpanded = IsSidebarExpanded

        };

        LauncherSettingsVm?.ApplyToConfig(snapshot);

        ThemeSettingsVm?.ApplyToConfig(snapshot);

        return snapshot;

    }



    public void LoadLauncherConfig()

    {

        _isLoadingConfig = true;

        try

        {

            var configPath = ZenithPaths.ConfigFilePath;

            if (File.Exists(configPath))

            {

                var json = File.ReadAllText(configPath);

                var cfg = System.Text.Json.JsonSerializer.Deserialize<LauncherConfigData>(json);

                if (cfg != null)

                {

                    if (!string.IsNullOrWhiteSpace(cfg.LauncherName))

                        LauncherName = cfg.LauncherName;



                    if (!string.IsNullOrWhiteSpace(cfg.ThemeSubTab))

                    {

                        ThemeSubTab = cfg.ThemeSubTab;

                        OnPropertyChanged(nameof(IsThemeSubTabSimple));

                        OnPropertyChanged(nameof(IsThemeSubTabAdvanced));

                    }



                    if (!string.IsNullOrWhiteSpace(cfg.BgColor)) BgHex = cfg.BgColor;

                    if (!string.IsNullOrWhiteSpace(cfg.SurfaceColor)) SurfaceHex = cfg.SurfaceColor;

                    if (!string.IsNullOrWhiteSpace(cfg.PanelColor)) PanelHex = cfg.PanelColor;

                    if (!string.IsNullOrWhiteSpace(cfg.SidebarBgColor)) SidebarBgHex = cfg.SidebarBgColor;

                    if (!string.IsNullOrWhiteSpace(cfg.CardColor)) CardHex = cfg.CardColor;

                    if (!string.IsNullOrWhiteSpace(cfg.FieldColor)) FieldHex = cfg.FieldColor;

                    if (!string.IsNullOrWhiteSpace(cfg.HoverColor)) HoverHex = cfg.HoverColor;

                    if (!string.IsNullOrWhiteSpace(cfg.TextPrimaryColor)) TextPrimaryHex = cfg.TextPrimaryColor;

                    if (!string.IsNullOrWhiteSpace(cfg.TextSecondaryColor)) TextSecondaryHex = cfg.TextSecondaryColor;

                    if (!string.IsNullOrWhiteSpace(cfg.BorderDarkColor)) BorderDarkHex = (cfg.BorderDarkColor == "#121622" || cfg.BorderDarkColor == "#182743" || cfg.BorderDarkColor == "#16181C") ? "#222834" : cfg.BorderDarkColor;

                    if (!string.IsNullOrWhiteSpace(cfg.SimpleAccentColor))

                    {

                        SimpleAccentHex = cfg.SimpleAccentColor;

                        _savedSimpleAccentHex = cfg.SimpleAccentColor;

                    }

                    else if (!string.IsNullOrWhiteSpace(cfg.AccentColor))

                    {

                        SimpleAccentHex = cfg.AccentColor;

                        _savedSimpleAccentHex = cfg.AccentColor;

                    }



                    if (!string.IsNullOrWhiteSpace(cfg.AccentColor))

                    {

                        CurrentAccentHex = cfg.AccentColor;

                        CustomThemeHex = cfg.AccentColor;

                        UpdatePreviewBrushes(cfg.AccentColor);

                    }



                    // Apply the theme corresponding to the saved ThemeSubTab

                    if (IsThemeSubTabSimple)

                    {

                        ApplySimpleTheme(SimpleAccentHex);

                    }

                    else

                    {

                        ZenithTheme.Apply(GetLauncherConfigSnapshot());

                    }



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

                    if (cfg.MinRamMb > 0) MinRamMb = cfg.MinRamMb;

                    if (cfg.RamMb > 0) RamMb = cfg.RamMb;

                    if (!string.IsNullOrWhiteSpace(cfg.LaunchBehavior))

                        SelectedLaunchBehavior = cfg.LaunchBehavior;

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

                    if (cfg.LauncherScale > 0.4 && cfg.LauncherScale < 3.0) LauncherScale = cfg.LauncherScale;

                    if (!string.IsNullOrEmpty(cfg.SelectedParticle)) SelectedParticle = cfg.SelectedParticle;

                    if (cfg.ParticleCount >= 10 && cfg.ParticleCount <= 150) ParticleCount = cfg.ParticleCount;

                    if (cfg.ParticleScale >= 0.3 && cfg.ParticleScale <= 2.5) ParticleScale = cfg.ParticleScale;

                    if (cfg.ParticleSpeed >= 0.2 && cfg.ParticleSpeed <= 3.0) ParticleSpeed = cfg.ParticleSpeed;

                    ParticleColorHex = cfg.ParticleColorHex ?? "";

                    if (cfg.ParticleAngleDeg >= 0 && cfg.ParticleAngleDeg <= 360) ParticleAngleDeg = cfg.ParticleAngleDeg;

                    ParticleTexturePath = cfg.ParticleTexturePath ?? "";

                    IsSidebarExpanded = cfg.IsSidebarExpanded;

                    LauncherSettingsVm?.LoadFromConfig(cfg);

                    ThemeSettingsVm?.LoadFromConfig(cfg);

                    SnapshotCurrentSavedTheme();

                }

            }

        }

        catch (Exception ex)

        {

            LauncherLog.Error("Failed to load launcher config - keeping current settings.", ex);

        }

        finally

        {

            _isLoadingConfig = false;

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


    public void RefreshState()

    {

        AccountsVm.RefreshAccounts();



        Instances.Clear();

        foreach (var inst in _instanceService.GetInstances())

        {

            inst.IsSelected = (inst.Id == SelectedInstanceId);

            inst.IsRunning = false;

            inst.IsLaunching = false;

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



    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private string _toastMessage = string.Empty;
    private System.Threading.CancellationTokenSource? _toastCts;

    public void ShowToast(string message)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new System.Threading.CancellationTokenSource();
        var token = _toastCts.Token;

        ToastMessage = message;
        IsToastVisible = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2500, token);
                if (!token.IsCancellationRequested)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            IsToastVisible = false;
                        }
                    });
                }
            }
            catch (TaskCanceledException)
            {
                // Ignored - superseded by subsequent toast
            }
        });
    }

    [ObservableProperty] private bool _isSwitchAccountsMenuOpen;

    [RelayCommand]
    public void OpenSwitchAccountsMenu() => IsSwitchAccountsMenuOpen = true;

    [RelayCommand]
    public void CloseSwitchAccountsMenu() => IsSwitchAccountsMenuOpen = false;

    [RelayCommand]
    public void ToggleSwitchAccountsMenu() => IsSwitchAccountsMenuOpen = !IsSwitchAccountsMenuOpen;

    [RelayCommand]
    public void SwitchAccount(AccountModel? account)
    {
        if (account == null) return;
        AccountsVm.SwitchAccount(account);
        RefreshState();
        OnPropertyChanged(nameof(ActiveAccountName));
        OnPropertyChanged(nameof(ActiveAccountType));
        OnPropertyChanged(nameof(ActiveAccountRow));
        ShowToast(string.Format(L10n.T("acc_switched_toast"), account.Username));
    }

    [RelayCommand]
    public async Task CopyActiveUsernameAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveAccountName)) return;
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
            {
                var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(window);
                if (topLevel?.Clipboard != null)
                {
                    var transfer = new DataTransfer();
                    transfer.Add(DataTransferItem.CreateText(ActiveAccountName));
                    await topLevel.Clipboard.SetDataAsync(transfer);
                    ShowToast(string.Format(L10n.T("acc_copied_toast"), ActiveAccountName));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to copy username: {ex.Message}");
        }
    }

    [ObservableProperty] private bool _isDeleteAccountConfirmOpen;
    [ObservableProperty] private string _pendingDeleteAccountName = string.Empty;
    [ObservableProperty] private AccountModel? _pendingDeleteAccount;

    [RelayCommand]
    public void RequestDeleteActiveAccount()
    {
        var active = ActiveAccountRow?.Model
            ?? AccountsVm.SelectedAccount
            ?? _accountService.GetActiveAccount()
            ?? _accountService.GetAccounts().FirstOrDefault(a => a.Username == ActiveAccountName)
            ?? _accountService.GetAccounts().FirstOrDefault();

        if (active == null)
        {
            ShowToast(L10n.T("acc_no_active_toast"));
            return;
        }

        PendingDeleteAccount = active;
        PendingDeleteAccountName = active.Username;
        IsDeleteAccountConfirmOpen = true;
    }

    [RelayCommand]
    private void CancelDeleteAccountConfirm()
    {
        IsDeleteAccountConfirmOpen = false;
        PendingDeleteAccount = null;
        PendingDeleteAccountName = string.Empty;
    }

    [RelayCommand]
    private void ConfirmDeleteAccount()
    {
        if (PendingDeleteAccount != null)
        {
            var name = PendingDeleteAccount.Username;
            AccountsVm.DeleteAccount(PendingDeleteAccount);
            RefreshState();
            OnPropertyChanged(nameof(ActiveAccountName));
            OnPropertyChanged(nameof(ActiveAccountType));
            OnPropertyChanged(nameof(ActiveAccountRow));
            ShowToast(string.Format(L10n.T("acc_deleted_toast"), name));
        }
        IsDeleteAccountConfirmOpen = false;
        PendingDeleteAccount = null;
        PendingDeleteAccountName = string.Empty;
    }

    [RelayCommand]

    private void OpenSettingsDialog()

    {

        NavigateToSettings();

    }







    [RelayCommand]

    private void OpenAccountsDialog()

    {

        NavigateToAccounts();

    }



    [RelayCommand]

    private void CloseDialogs()

    {

        SaveLauncherConfig();

        IsCreateDialogOpen = false;

        IsJavaInstallerOpen = false;

        IsVersionPickerOpen = false;

        IsElyByErrorOpen = false;

        IsDeleteConfirmOpen = false;

        IsRestartConfirmOpen = false;

        IsCloneDialogOpen = false;

        IsServerUnderDevDialogOpen = false;

        PendingDeleteInstance = null;

        FilterVersions();

    }




}
