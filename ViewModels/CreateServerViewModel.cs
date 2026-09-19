using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class CreateServerViewModel : ObservableObject
{
    private readonly ServerCreatorService _serverService = new();
    private readonly MainWindowViewModel _host;
    private CancellationTokenSource? _cts;
    private Process? _serverProcess;
    private StreamWriter? _serverStdin;
    private CancellationTokenSource? _stopTimeoutCts;
    private DateTime _serverStartTime;
    private readonly DispatcherTimer _uptimeTimer;

    // Creation Wizard Fields
    [ObservableProperty] private string _serverName = "My Local Server";
    [ObservableProperty] private string _selectedVersion = "1.21.4";
    [ObservableProperty] private ServerSoftwareOption _selectedSoftware;
    [ObservableProperty] private int _ramMb = 4096;
    public int RamGb
    {
        get => Math.Max(1, RamMb / 1024);
        set => RamMb = value * 1024;
    }
    [ObservableProperty] private int _serverPort = 25565;
    [ObservableProperty] private bool _agreeEula = true;
    [ObservableProperty] private bool _onlineMode = false; // Default: unchecked

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isCreated;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _createdServerDirectory = "";

    // Dashboard Mode & Master-Detail State
    [ObservableProperty] private bool _isDashboardMode;
    [ObservableProperty] private bool _isCreatingNewServer;
    [ObservableProperty] private bool _hasExistingServers;
    [ObservableProperty] private string? _selectedServerName;
    [ObservableProperty] private string _activeServerDirectory = "";
    [ObservableProperty] private int _selectedTab; // 0=Overview, 1=Properties, 2=Plugins, 3=Settings, 4=Players, 5=Files
    [ObservableProperty] private int _playersSubTab; // 0=Online, 1=Ops, 2=Whitelist, 3=Bans
    [ObservableProperty] private string _newPlayerName = "";
    [ObservableProperty] private string _configStatusMessage = "";
    [ObservableProperty] private bool _showWorldResetConfirm;
    [ObservableProperty] private string _worldResetStatusMessage = "";
    [ObservableProperty] private bool _showDeleteServerConfirm;

    // Technical Settings (Tab 3: Settings)
    [ObservableProperty] private string _serverJavaPath = "";
    [ObservableProperty] private string _serverJvmArgs = "";
    [ObservableProperty] private int _serverRamMb = 4096;
    [ObservableProperty] private string _serverSettingsStatusMessage = "";

    // Reset World 5-Second Countdown Timer
    [ObservableProperty] private bool _isResetCountdownActive;
    [ObservableProperty] private int _resetCountdownSeconds = 5;
    [ObservableProperty] private string _resetButtonText = "";
    [ObservableProperty] private bool _canConfirmResetWorld;
    private DispatcherTimer? _resetCountdownTimer;

    // In-App File Editor State
    [ObservableProperty] private bool _isFileEditorOpen;
    [ObservableProperty] private string _editingFileName = "";
    [ObservableProperty] private string _editingFileFullPath = "";
    [ObservableProperty] private string _editingFileContent = "";
    [ObservableProperty] private string _fileEditorStatusMessage = "";
    [ObservableProperty] private bool _isFileEditorSaving;

    // server.properties Visual Editor Fields (22 parameters)
    [ObservableProperty] private string _propsPort = "25565";
    [ObservableProperty] private string _propsMotd = "A Minecraft Server";
    [ObservableProperty] private string _propsGamemode = "survival";
    [ObservableProperty] private string _propsDifficulty = "easy";
    [ObservableProperty] private bool _propsPvp = true;
    [ObservableProperty] private int _propsSpawnProtection = 16;
    [ObservableProperty] private bool _propsCommandBlocks = true;
    [ObservableProperty] private bool _propsAllowFlight;
    [ObservableProperty] private int _propsViewDistance = 10;
    [ObservableProperty] private int _propsSimulationDistance = 10;
    [ObservableProperty] private bool _propsWhitelist;
    [ObservableProperty] private bool _propsHardcore;
    [ObservableProperty] private int _propsMaxPlayers = 20;
    [ObservableProperty] private bool _propsOnlineMode = false;
    [ObservableProperty] private string _propsLevelName = "world";
    [ObservableProperty] private string _propsLevelSeed = "";
    [ObservableProperty] private string _propsLevelType = "minecraft:normal";
    [ObservableProperty] private bool _propsGenerateStructures = true;
    [ObservableProperty] private bool _propsSpawnAnimals = true;
    [ObservableProperty] private bool _propsSpawnMonsters = true;
    [ObservableProperty] private bool _propsSpawnNpcs = true;
    [ObservableProperty] private bool _propsAllowNether = true;
    [ObservableProperty] private int _propsEntityBroadcastRangePercentage = 100;

    // Overview Live Status & Inline Edit Properties
    [ObservableProperty] private bool _isServerRunning;
    [ObservableProperty] private bool _isServerStopping;
    [ObservableProperty] private bool _isForceStopAvailable;
    [ObservableProperty] private bool _hasJavaMissingWarning;
    [ObservableProperty] private int _missingJavaMajor = 25;
    [ObservableProperty] private string _javaMissingWarningText = "";

    public string StopButtonText => IsServerStopping ? L10n.T("cs_server_stopping") : L10n.T("cs_stop_server");
    public string DownloadJavaButtonText => string.Format(L10n.T("cs_download_java"), MissingJavaMajor);
    public bool CanShowStopButton => IsServerRunning;

    partial void OnIsServerStoppingChanged(bool value) => OnPropertyChanged(nameof(StopButtonText));

    [ObservableProperty] private string _serverSoftwareDisplay = "Vanilla";
    [ObservableProperty] private string _serverAllocatedRamDisplay = "4096 MB";
    [ObservableProperty] private string _uptimeText = "00:00:00";
    [ObservableProperty] private bool _isEditingServerName;
    [ObservableProperty] private string _editingServerNameText = "";
    [ObservableProperty] private string _serverIpDisplay = "localhost:25565";
    [ObservableProperty] private bool _isIpCopied;
    public bool CanEditServer => !IsServerRunning;
    partial void OnIsServerRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditServer));
        OnPropertyChanged(nameof(CanShowStopButton));
    }

    public ObservableCollection<ServerNetworkEndpoint> NetworkEndpoints { get; } = new();

    public event Action? StateChanged;

    public ObservableCollection<string> AvailableVersions { get; } = new();
    public ObservableCollection<ServerSoftwareOption> AvailableSoftware { get; } = new();
    public int[] RamOptions { get; } = { 1024, 2048, 3072, 4096, 6144, 8192, 12288, 16384, 24576, 32768 };

    public string[] GamemodeOptions { get; } = { "survival", "creative", "adventure", "spectator" };
    public string[] DifficultyOptions { get; } = { "peaceful", "easy", "normal", "hard" };
    public string[] LevelTypeOptions { get; } = { "minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified" };

    public ObservableCollection<string> ExistingServers { get; } = new();
    public ObservableCollection<ServerPluginItem> InstalledPlugins { get; } = new();
    public ObservableCollection<ServerPlayerEntry> OpsList { get; } = new();
    public ObservableCollection<ServerPlayerEntry> WhitelistList { get; } = new();
    public ObservableCollection<ServerPlayerEntry> BansList { get; } = new();
    public ObservableCollection<string> OnlinePlayers { get; } = new();

    // File Manager State
    public ObservableCollection<ServerFileItem> ServerFiles { get; } = new();
    [ObservableProperty] private string _currentBrowsePath = "";
    [ObservableProperty] private string _currentBrowseRelativePath = "/";
    [ObservableProperty] private bool _canNavigateUp;
    [ObservableProperty] private bool _isFilesEmpty;
    [ObservableProperty] private string _fileOperationStatus = "";

    [ObservableProperty] private bool _isRefreshingOnlinePlayers;
    [ObservableProperty] private string _onlinePlayersCountText = "0 players online";

    public bool IsOverviewTab => SelectedTab == 0;
    public bool IsPropertiesTab => SelectedTab == 1;
    public bool IsPluginsTab => SelectedTab == 2;
    public bool IsSettingsTab => SelectedTab == 3;
    public bool IsWorldTab => SelectedTab == 3; // Kept for safety
    public bool IsPlayersTab => SelectedTab == 4;
    public bool IsFilesTab => SelectedTab == 5;

    public bool IsOnlinePlayersSubTab => PlayersSubTab == 0;
    public bool IsOpsSubTab => PlayersSubTab == 1;
    public bool IsWhitelistSubTab => PlayersSubTab == 2;
    public bool IsBansSubTab => PlayersSubTab == 3;

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewTab));
        OnPropertyChanged(nameof(IsPropertiesTab));
        OnPropertyChanged(nameof(IsPluginsTab));
        OnPropertyChanged(nameof(IsSettingsTab));
        OnPropertyChanged(nameof(IsWorldTab));
        OnPropertyChanged(nameof(IsPlayersTab));
        OnPropertyChanged(nameof(IsFilesTab));
        if (value == 5)
        {
            RefreshFiles();
        }
        StateChanged?.Invoke();
    }

    partial void OnIsDashboardModeChanged(bool value) => StateChanged?.Invoke();
    partial void OnIsCreatingNewServerChanged(bool value) => StateChanged?.Invoke();

    partial void OnPlayersSubTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsOnlinePlayersSubTab));
        OnPropertyChanged(nameof(IsOpsSubTab));
        OnPropertyChanged(nameof(IsWhitelistSubTab));
        OnPropertyChanged(nameof(IsBansSubTab));
        if (value == 0)
        {
            _ = RefreshOnlinePlayersAsync();
        }
    }

    public bool CanCreate => !IsBusy && !string.IsNullOrWhiteSpace(ServerName) && !string.IsNullOrWhiteSpace(SelectedVersion);

    public CreateServerViewModel(IInstanceService instanceService, MainWindowViewModel host)
    {
        _host = host;
        UpdateAvailableSoftware();
        _selectedSoftware = AvailableSoftware.FirstOrDefault() ?? ServerCreatorService.AvailableSoftware[0];
        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (s, e) =>
        {
            if (IsServerRunning)
            {
                var span = DateTime.Now - _serverStartTime;
                UptimeText = span.ToString(@"hh\:mm\:ss");
            }
        };

        RefreshExistingServers();
        if (ExistingServers.Count > 0)
        {
            SelectedServerName = ExistingServers[0];
            IsDashboardMode = true;
            IsCreatingNewServer = false;
        }
        else
        {
            IsDashboardMode = false;
            IsCreatingNewServer = true;
        }
        _ = LoadVersionsAsync();
    }

    public void UpdateAvailableSoftware()
    {
        var compatible = ServerCreatorService.GetCompatibleSoftware(SelectedVersion);
        AvailableSoftware.Clear();
        foreach (var opt in compatible)
        {
            AvailableSoftware.Add(opt);
        }

        if (SelectedSoftware == null || !AvailableSoftware.Any(s => s.Id == SelectedSoftware.Id))
        {
            SelectedSoftware = AvailableSoftware.FirstOrDefault() ?? ServerCreatorService.AvailableSoftware[0];
        }
    }

    public void RefreshExistingServers()
    {
        ExistingServers.Clear();
        foreach (var s in ServerCreatorService.GetExistingServers())
        {
            ExistingServers.Add(s);
        }
        HasExistingServers = ExistingServers.Count > 0;
    }

    [RelayCommand]
    public void SelectServer(string serverName)
    {
        SelectedServerName = serverName;
        IsCreatingNewServer = false;
        IsDashboardMode = true;
        StateChanged?.Invoke();
    }

    private string GenerateUniqueServerName()
    {
        const string baseName = "Server";
        var existing = ExistingServers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!existing.Contains(candidate) && !Directory.Exists(Path.Combine(ServerCreatorService.DefaultServersDirectory, candidate)))
                return candidate;
        }
    }

    [RelayCommand]
    public void StartCreateNewServer()
    {
        IsCreatingNewServer = true;
        ServerName = GenerateUniqueServerName();
        IsCreated = false;
        StatusText = "";
        StateChanged?.Invoke();
    }

    [RelayCommand]
    public void CancelCreateNewServer()
    {
        if (ExistingServers.Count > 0)
        {
            IsCreatingNewServer = false;
            if (string.IsNullOrWhiteSpace(SelectedServerName))
                SelectedServerName = ExistingServers[0];
            IsDashboardMode = true;
            StateChanged?.Invoke();
        }
    }

    [RelayCommand]
    public void SetRam(string gb)
    {
        if (int.TryParse(gb, out var val) && val > 0)
        {
            RamGb = val;
        }
    }

    [RelayCommand]
    public void RequestDeleteServer()
    {
        ShowDeleteServerConfirm = true;
    }

    [RelayCommand]
    public void CancelDeleteServer()
    {
        ShowDeleteServerConfirm = false;
    }

    [RelayCommand]
    public void ConfirmDeleteServer()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;

        try
        {
            ServerCreatorService.DeleteServer(ActiveServerDirectory);
            ShowDeleteServerConfirm = false;
            RefreshExistingServers();
            if (ExistingServers.Count > 0)
            {
                SelectedServerName = ExistingServers[0];
                IsCreatingNewServer = false;
                IsDashboardMode = true;
            }
            else
            {
                SelectedServerName = null;
                ActiveServerDirectory = "";
                IsCreatingNewServer = true;
                IsDashboardMode = false;
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to delete server", ex);
        }
    }

    partial void OnSelectedServerNameChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ActiveServerDirectory = Path.Combine(ServerCreatorService.DefaultServersDirectory, value);
            LoadServerDetails();
        }
        StateChanged?.Invoke();
    }

    public void LoadServerDetails()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory) || !Directory.Exists(ActiveServerDirectory))
            return;

        // Load server.properties (all 22 parameters)
        var props = ServerCreatorService.LoadProperties(ActiveServerDirectory);
        if (props.TryGetValue("server-port", out var port)) PropsPort = port;
        if (props.TryGetValue("motd", out var motd)) PropsMotd = motd;
        if (props.TryGetValue("gamemode", out var gm)) PropsGamemode = gm.ToLowerInvariant();
        if (props.TryGetValue("difficulty", out var diff)) PropsDifficulty = diff.ToLowerInvariant();
        if (props.TryGetValue("pvp", out var pvp) && bool.TryParse(pvp, out var bPvp)) PropsPvp = bPvp;
        if (props.TryGetValue("spawn-protection", out var sp) && int.TryParse(sp, out var iSp)) PropsSpawnProtection = iSp;
        if (props.TryGetValue("enable-command-block", out var cb) && bool.TryParse(cb, out var bCb)) PropsCommandBlocks = bCb;
        if (props.TryGetValue("allow-flight", out var af) && bool.TryParse(af, out var bAf)) PropsAllowFlight = bAf;
        if (props.TryGetValue("view-distance", out var vd) && int.TryParse(vd, out var iVd)) PropsViewDistance = iVd;
        if (props.TryGetValue("simulation-distance", out var sd) && int.TryParse(sd, out var iSd)) PropsSimulationDistance = iSd;
        if (props.TryGetValue("white-list", out var wl) && bool.TryParse(wl, out var bWl)) PropsWhitelist = bWl;
        if (props.TryGetValue("hardcore", out var hc) && bool.TryParse(hc, out var bHc)) PropsHardcore = bHc;
        if (props.TryGetValue("max-players", out var mp) && int.TryParse(mp, out var iMp)) PropsMaxPlayers = iMp;
        if (props.TryGetValue("online-mode", out var om) && bool.TryParse(om, out var bOm)) PropsOnlineMode = bOm;
        if (props.TryGetValue("level-name", out var ln)) PropsLevelName = ln;
        if (props.TryGetValue("level-seed", out var ls)) PropsLevelSeed = ls;
        if (props.TryGetValue("level-type", out var lt)) PropsLevelType = lt;
        if (props.TryGetValue("generate-structures", out var gs) && bool.TryParse(gs, out var bGs)) PropsGenerateStructures = bGs;
        if (props.TryGetValue("spawn-animals", out var sa) && bool.TryParse(sa, out var bSa)) PropsSpawnAnimals = bSa;
        if (props.TryGetValue("spawn-monsters", out var sm) && bool.TryParse(sm, out var bSm)) PropsSpawnMonsters = bSm;
        if (props.TryGetValue("spawn-npcs", out var sn) && bool.TryParse(sn, out var bSn)) PropsSpawnNpcs = bSn;
        if (props.TryGetValue("allow-nether", out var an) && bool.TryParse(an, out var bAn)) PropsAllowNether = bAn;
        if (props.TryGetValue("entity-broadcast-range-percentage", out var eb) && int.TryParse(eb, out var iEb)) PropsEntityBroadcastRangePercentage = iEb;

        // Load plugins / mods
        InstalledPlugins.Clear();
        foreach (var p in ServerCreatorService.GetInstalledPluginsOrMods(ActiveServerDirectory))
        {
            InstalledPlugins.Add(p);
        }

        // Load player lists
        OpsList.Clear();
        foreach (var op in ServerCreatorService.LoadPlayerList(ActiveServerDirectory, "ops.json"))
        {
            OpsList.Add(op);
        }

        WhitelistList.Clear();
        foreach (var w in ServerCreatorService.LoadPlayerList(ActiveServerDirectory, "whitelist.json"))
        {
            WhitelistList.Add(w);
        }

        BansList.Clear();
        foreach (var b in ServerCreatorService.LoadPlayerList(ActiveServerDirectory, "banned-players.json"))
        {
            BansList.Add(b);
        }

        ServerCreatorService.GetServerMetadata(ActiveServerDirectory, out var soft, out var ramDisplay, out var ramMb, out var javaPath, out var jvmArgs);
        ServerSoftwareDisplay = soft;
        ServerAllocatedRamDisplay = ramDisplay;
        ServerRamMb = ramMb;
        ServerJavaPath = javaPath;
        ServerJvmArgs = jvmArgs;
        ServerSettingsStatusMessage = "";
        RefreshNetworkEndpoints();

        _ = RefreshOnlinePlayersAsync();

        CurrentBrowsePath = ActiveServerDirectory;
        if (SelectedTab == 5)
        {
            RefreshFiles();
        }

        ConfigStatusMessage = "";
        WorldResetStatusMessage = "";
        ShowWorldResetConfirm = false;
        IsResetCountdownActive = false;
        CanConfirmResetWorld = false;
        _resetCountdownTimer?.Stop();
    }

    [RelayCommand]
    public void SelectTab(string tabIndex)
    {
        if (int.TryParse(tabIndex, out var idx))
        {
            SelectedTab = idx;
        }
    }

    [RelayCommand]
    public void SelectPlayersSubTab(string tabIndex)
    {
        if (int.TryParse(tabIndex, out var idx))
        {
            PlayersSubTab = idx;
        }
    }

    [RelayCommand]
    public async Task RefreshOnlinePlayersAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory) || !Directory.Exists(ActiveServerDirectory))
            return;

        IsRefreshingOnlinePlayers = true;
        try
        {
            await Task.Run(() =>
            {
                var logFile = Path.Combine(ActiveServerDirectory, "logs", "latest.log");
                var online = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (File.Exists(logFile))
                {
                    try
                    {
                        using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(fs);
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            int leftIdx = line.IndexOf(" left the game", StringComparison.OrdinalIgnoreCase);
                            if (leftIdx > 0)
                            {
                                int colonIdx = line.IndexOf("]: ", StringComparison.Ordinal);
                                int start = colonIdx > 0 ? colonIdx + 3 : 0;
                                if (leftIdx > start)
                                {
                                    var name = line.Substring(start, leftIdx - start).Trim();
                                    online.Remove(name);
                                    continue;
                                }
                            }

                            int lostIdx = line.IndexOf(" lost connection:", StringComparison.OrdinalIgnoreCase);
                            if (lostIdx > 0)
                            {
                                int colonIdx = line.IndexOf("]: ", StringComparison.Ordinal);
                                int start = colonIdx > 0 ? colonIdx + 3 : 0;
                                if (lostIdx > start)
                                {
                                    var name = line.Substring(start, lostIdx - start).Trim();
                                    online.Remove(name);
                                    continue;
                                }
                            }

                            int joinedIdx = line.IndexOf(" joined the game", StringComparison.OrdinalIgnoreCase);
                            if (joinedIdx > 0)
                            {
                                int colonIdx = line.IndexOf("]: ", StringComparison.Ordinal);
                                int start = colonIdx > 0 ? colonIdx + 3 : 0;
                                if (joinedIdx > start)
                                {
                                    var name = line.Substring(start, joinedIdx - start).Trim();
                                    if (!string.IsNullOrEmpty(name))
                                        online.Add(name);
                                    continue;
                                }
                            }

                            int loggedInIdx = line.IndexOf(" logged in with entity id", StringComparison.OrdinalIgnoreCase);
                            if (loggedInIdx > 0)
                            {
                                int colonIdx = line.IndexOf("]: ", StringComparison.Ordinal);
                                int start = colonIdx > 0 ? colonIdx + 3 : 0;
                                if (loggedInIdx > start)
                                {
                                    var segment = line.Substring(start, loggedInIdx - start).Trim();
                                    int bracketIdx = segment.IndexOf('[');
                                    var name = (bracketIdx > 0 ? segment.Substring(0, bracketIdx) : segment).Trim();
                                    if (!string.IsNullOrEmpty(name))
                                        online.Add(name);
                                    continue;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // File might be busy
                    }
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    OnlinePlayers.Clear();
                    foreach (var p in online)
                    {
                        OnlinePlayers.Add(p);
                    }
                    OnlinePlayersCountText = $"{OnlinePlayers.Count} player{(OnlinePlayers.Count == 1 ? "" : "s")} online";
                });
            });
        }
        finally
        {
            IsRefreshingOnlinePlayers = false;
        }
    }

    [RelayCommand]
    public void SaveProperties()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory) || !Directory.Exists(ActiveServerDirectory))
            return;

        var dict = new Dictionary<string, string>
        {
            ["server-port"] = PropsPort,
            ["motd"] = PropsMotd,
            ["gamemode"] = PropsGamemode,
            ["difficulty"] = PropsDifficulty,
            ["pvp"] = PropsPvp ? "true" : "false",
            ["spawn-protection"] = PropsSpawnProtection.ToString(),
            ["enable-command-block"] = PropsCommandBlocks ? "true" : "false",
            ["allow-flight"] = PropsAllowFlight ? "true" : "false",
            ["view-distance"] = PropsViewDistance.ToString(),
            ["simulation-distance"] = PropsSimulationDistance.ToString(),
            ["white-list"] = PropsWhitelist ? "true" : "false",
            ["hardcore"] = PropsHardcore ? "true" : "false",
            ["max-players"] = PropsMaxPlayers.ToString(),
            ["online-mode"] = PropsOnlineMode ? "true" : "false",
            ["level-name"] = PropsLevelName,
            ["level-seed"] = PropsLevelSeed,
            ["level-type"] = PropsLevelType,
            ["generate-structures"] = PropsGenerateStructures ? "true" : "false",
            ["spawn-animals"] = PropsSpawnAnimals ? "true" : "false",
            ["spawn-monsters"] = PropsSpawnMonsters ? "true" : "false",
            ["spawn-npcs"] = PropsSpawnNpcs ? "true" : "false",
            ["allow-nether"] = PropsAllowNether ? "true" : "false",
            ["entity-broadcast-range-percentage"] = PropsEntityBroadcastRangePercentage.ToString()
        };

        ServerCreatorService.SaveProperties(ActiveServerDirectory, dict);
        ConfigStatusMessage = L10n.T("cs_save_props") + "!";
        RefreshNetworkEndpoints();
    }

    [RelayCommand]
    public void StartRenameServer()
    {
        if (IsServerRunning) return;
        EditingServerNameText = SelectedServerName ?? "";
        IsEditingServerName = true;
    }

    [RelayCommand]
    public void CommitRenameServer()
    {
        if (!IsEditingServerName) return;
        if (IsServerRunning)
        {
            IsEditingServerName = false;
            return;
        }

        var newName = EditingServerNameText.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName == SelectedServerName)
        {
            IsEditingServerName = false;
            return;
        }

        if (ServerCreatorService.RenameServer(SelectedServerName!, newName, out var err))
        {
            SelectedServerName = newName;
            ActiveServerDirectory = Path.Combine(ServerCreatorService.DefaultServersDirectory, newName);
            RefreshExistingServers();
            ConfigStatusMessage = "Server renamed successfully.";
        }
        else
        {
            ConfigStatusMessage = err ?? "Failed to rename server.";
        }
        IsEditingServerName = false;
    }

    [RelayCommand]
    public void CancelRenameServer()
    {
        IsEditingServerName = false;
    }

    [RelayCommand]
    public async Task CopyIpAsync() => await CopyAddressAsync(ServerIpDisplay);

    [RelayCommand]
    public async Task CopyAddressAsync(string? address)
    {
        var target = string.IsNullOrWhiteSpace(address) ? ServerIpDisplay : address;
        try
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;
            if (topLevel?.Clipboard != null)
            {
                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.CreateText(target));
                await topLevel.Clipboard.SetDataAsync(transfer);
            }
            IsIpCopied = true;
            _ = Task.Delay(2000).ContinueWith(_ => Dispatcher.UIThread.Post(() => IsIpCopied = false));
        }
        catch { }
    }

    public void RefreshNetworkEndpoints()
    {
        NetworkEndpoints.Clear();
        var port = int.TryParse(PropsPort, out var p) ? p : 25565;
        var list = ServerCreatorService.GetLocalNetworkEndpoints(port);
        foreach (var ep in list)
        {
            NetworkEndpoints.Add(ep);
        }
        ServerIpDisplay = $"localhost:{port}";
    }

    [RelayCommand]
    public async Task StopServerAsync()
    {
        if (!IsServerRunning || _serverProcess == null || _serverProcess.HasExited)
        {
            IsServerRunning = false;
            IsServerStopping = false;
            IsForceStopAvailable = false;
            return;
        }

        IsServerStopping = true;
        IsForceStopAvailable = false;

        try
        {
            if (_serverStdin != null)
            {
                await _serverStdin.WriteLineAsync("stop");
                await _serverStdin.FlushAsync();

                // Non-blocking trailing newline after short delay to unblock any pause or confirmation
                _ = Task.Delay(1500).ContinueWith(async _ =>
                {
                    try
                    {
                        if (_serverStdin != null && IsServerRunning)
                        {
                            await _serverStdin.WriteLineAsync();
                            await _serverStdin.FlushAsync();
                        }
                    }
                    catch { }
                });
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Could not send 'stop' to server stdin: {ex.Message}");
        }

        _stopTimeoutCts?.Cancel();
        _stopTimeoutCts = new CancellationTokenSource();
        var ct = _stopTimeoutCts.Token;

        _ = Task.Delay(10000, ct).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsServerRunning && IsServerStopping)
                    {
                        IsForceStopAvailable = true;
                    }
                });
            }
        }, ct);
    }

    [RelayCommand]
    public void ForceStopServer()
    {
        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill(true);
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to force stop server", ex);
        }
        finally
        {
            IsServerRunning = false;
            IsServerStopping = false;
            IsForceStopAvailable = false;
            _stopTimeoutCts?.Cancel();
            _uptimeTimer.Stop();
            UptimeText = "00:00:00";
            _serverProcess = null;
            _serverStdin = null;
        }
    }

    [RelayCommand]
    public void OpenLogs()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;
        var logFile = Path.Combine(ActiveServerDirectory, "logs", "latest.log");
        if (File.Exists(logFile))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = logFile, UseShellExecute = true });
                return;
            }
            catch { }
        }
        OpenServerFolder();
    }

    [RelayCommand]
    public void SwitchToWizard()
    {
        IsDashboardMode = false;
        IsCreated = false;
    }

    [RelayCommand]
    public void SwitchToDashboard()
    {
        if (ExistingServers.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(SelectedServerName))
                SelectedServerName = ExistingServers[0];
            IsDashboardMode = true;
        }
    }

    private async Task LoadVersionsAsync()
    {
        IsBusy = true;
        StatusText = "Loading versions...";
        try
        {
            var versions = await _serverService.GetServerVersionsAsync(
                _host.ShowReleases,
                _host.ShowSnapshots,
                _host.ShowBetas,
                _host.ShowAlphas);

            AvailableVersions.Clear();
            foreach (var v in versions)
                AvailableVersions.Add(v);

            if (AvailableVersions.Count > 0)
            {
                SelectedVersion = AvailableVersions[0];
            }
            UpdateAvailableSoftware();
            StatusText = "";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading versions: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanCreate));
        }
    }

    partial void OnServerNameChanged(string value) => OnPropertyChanged(nameof(CanCreate));
    partial void OnSelectedVersionChanged(string value)
    {
        UpdateAvailableSoftware();
        OnPropertyChanged(nameof(CanCreate));
    }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanCreate));

    [RelayCommand]
    public async Task CreateServerAsync()
    {
        if (!CanCreate) return;
        IsBusy = true;
        IsCreated = false;
        ProgressPercent = 0;
        StatusText = L10n.T("cs_creating");
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(string Status, double Percent)>(p =>
            {
                StatusText = p.Status;
                ProgressPercent = p.Percent;
            });

            var targetDir = await _serverService.CreateServerAsync(
                ServerName,
                SelectedVersion,
                SelectedSoftware.Id,
                RamMb,
                ServerPort,
                AgreeEula,
                OnlineMode,
                progress,
                _cts.Token);

            CreatedServerDirectory = targetDir;
            IsCreated = true;
            StatusText = L10n.T("cs_created_success");

            RefreshExistingServers();
            SelectedServerName = Path.GetFileName(targetDir);
            IsCreatingNewServer = false;
            IsDashboardMode = true;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to create server", ex);
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    public void OpenServerFolder()
    {
        var dir = IsDashboardMode && !string.IsNullOrWhiteSpace(ActiveServerDirectory)
            ? ActiveServerDirectory
            : string.IsNullOrWhiteSpace(CreatedServerDirectory)
                ? ServerCreatorService.DefaultServersDirectory
                : CreatedServerDirectory;

        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to open server folder", ex);
        }
    }

    [RelayCommand]
    public void RunServer()
    {
        var dir = IsDashboardMode && !string.IsNullOrWhiteSpace(ActiveServerDirectory)
            ? ActiveServerDirectory
            : CreatedServerDirectory;

        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return;

        try
        {
            var javaPath = JavaVersionHelper.FindOrResolveServerJava(dir, SelectedVersion, out int reqMajor);
            if (string.IsNullOrWhiteSpace(javaPath))
            {
                MissingJavaMajor = reqMajor;
                JavaMissingWarningText = string.Format(L10n.T("cs_java_missing_warn"), reqMajor);
                HasJavaMissingWarning = true;
                OnPropertyChanged(nameof(DownloadJavaButtonText));
                return;
            }

            HasJavaMissingWarning = false;
            ServerCreatorService.EnsureRunBatJava(dir, javaPath);

            var batPath = Path.Combine(dir, "run.bat");
            if (File.Exists(batPath))
            {
                var title = SelectedServerName ?? Path.GetFileName(dir);
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c title Minecraft Server - {title} & run.bat",
                        WorkingDirectory = dir,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        CreateNoWindow = false
                    },
                    EnableRaisingEvents = true
                };

                p.Exited += (s, e) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsServerRunning = false;
                        IsServerStopping = false;
                        IsForceStopAvailable = false;
                        _stopTimeoutCts?.Cancel();
                        _uptimeTimer.Stop();
                        UptimeText = "00:00:00";
                        _serverProcess = null;
                        _serverStdin = null;
                    });
                };

                if (p.Start())
                {
                    _serverProcess = p;
                    _serverStdin = p.StandardInput;
                    _serverStdin.AutoFlush = true;
                    _serverStartTime = DateTime.Now;
                    IsServerRunning = true;
                    IsServerStopping = false;
                    IsForceStopAvailable = false;
                    _uptimeTimer.Start();
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to run server in cmd", ex);
        }
    }

    [RelayCommand]
    public void DownloadMissingJava()
    {
        var url = JavaVersionHelper.AdoptiumDownloadUrl(MissingJavaMajor);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to open download url: {url}", ex);
        }
    }

    // ==========================================
    // File Manager Operations
    // ==========================================

    [RelayCommand]
    public void RefreshFiles()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory) || !Directory.Exists(ActiveServerDirectory))
        {
            ServerFiles.Clear();
            IsFilesEmpty = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentBrowsePath) || !Directory.Exists(CurrentBrowsePath) ||
            !CurrentBrowsePath.StartsWith(ActiveServerDirectory, StringComparison.OrdinalIgnoreCase))
        {
            CurrentBrowsePath = ActiveServerDirectory;
        }

        try
        {
            var activeFull = Path.GetFullPath(ActiveServerDirectory).TrimEnd('\\', '/');
            var currFull = Path.GetFullPath(CurrentBrowsePath).TrimEnd('\\', '/');
            CanNavigateUp = !string.Equals(activeFull, currFull, StringComparison.OrdinalIgnoreCase);

            var rel = Path.GetRelativePath(activeFull, currFull);
            CurrentBrowseRelativePath = string.IsNullOrWhiteSpace(rel) || rel == "." ? "/" : "/" + rel.Replace('\\', '/');

            var items = new List<ServerFileItem>();

            var dirs = Directory.GetDirectories(CurrentBrowsePath);
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            foreach (var d in dirs)
            {
                var dirInfo = new DirectoryInfo(d);
                items.Add(new ServerFileItem
                {
                    Name = dirInfo.Name,
                    FullPath = d,
                    IsDirectory = true,
                    SizeBytes = 0,
                    SizeDisplay = "--",
                    ModifiedDisplay = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                    IconKind = "folder",
                    IconData = "M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z",
                    IconColor = "#60A5FA"
                });
            }

            var files = Directory.GetFiles(CurrentBrowsePath);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                var ext = fi.Extension.ToLowerInvariant();
                string iconData;
                string iconColor;
                string iconKind;

                if (ext is ".properties" or ".yml" or ".yaml" or ".json" or ".toml" or ".txt")
                {
                    iconKind = "config";
                    iconData = "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z";
                    iconColor = "#FBBF24";
                }
                else if (ext is ".jar" or ".bat" or ".sh" or ".cmd")
                {
                    iconKind = "package";
                    iconData = "M12 2l-8 4.5v9L12 20l8-4.5v-9L12 2zm0 2.2l5.7 3.2L12 10.6 6.3 7.4 12 4.2z";
                    iconColor = "#34D399";
                }
                else if (ext is ".log" or ".gz")
                {
                    iconKind = "log";
                    iconData = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-2 10H7v-2h10v2zm0-4H7V7h10v2z";
                    iconColor = "#A78BFA";
                }
                else
                {
                    iconKind = "file";
                    iconData = "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm4 18H6V4h7v5h5v11z";
                    iconColor = "#9CA3AF";
                }

                items.Add(new ServerFileItem
                {
                    Name = fi.Name,
                    FullPath = f,
                    IsDirectory = false,
                    SizeBytes = fi.Length,
                    SizeDisplay = FormatFileSize(fi.Length),
                    ModifiedDisplay = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                    IconKind = iconKind,
                    IconData = iconData,
                    IconColor = iconColor
                });
            }

            ServerFiles.Clear();
            foreach (var it in items) ServerFiles.Add(it);
            IsFilesEmpty = ServerFiles.Count == 0;
            FileOperationStatus = "";
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to refresh server files", ex);
            FileOperationStatus = ex.Message;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".properties", ".json", ".txt", ".yml", ".yaml", ".log",
        ".bat", ".sh", ".cmd", ".toml", ".cfg", ".ini", ".conf",
        ".xml", ".md", ".env", ".csv", ".mcmeta"
    };

    [RelayCommand]
    public async Task NavigateIntoFileItem(ServerFileItem? item)
    {
        if (item == null) return;
        if (item.IsDirectory)
        {
            CurrentBrowsePath = item.FullPath;
            RefreshFiles();
        }
        else
        {
            await OpenFileItem(item);
        }
    }

    [RelayCommand]
    public void NavigateUpDirectory()
    {
        if (!CanNavigateUp) return;
        try
        {
            var parent = Directory.GetParent(CurrentBrowsePath)?.FullName;
            if (parent != null && parent.StartsWith(ActiveServerDirectory, StringComparison.OrdinalIgnoreCase))
            {
                CurrentBrowsePath = parent;
                RefreshFiles();
            }
        }
        catch { }
    }

    [RelayCommand]
    public async Task OpenFileItem(ServerFileItem? item)
    {
        if (item == null) return;
        try
        {
            if (item.IsDirectory)
            {
                CurrentBrowsePath = item.FullPath;
                RefreshFiles();
            }
            else if (File.Exists(item.FullPath))
            {
                var ext = Path.GetExtension(item.FullPath);
                if (TextExtensions.Contains(ext))
                {
                    EditingFileName = Path.GetFileName(item.FullPath);
                    EditingFileFullPath = item.FullPath;
                    EditingFileContent = await File.ReadAllTextAsync(item.FullPath);
                    FileEditorStatusMessage = "";
                    IsFileEditorOpen = true;
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = item.FullPath, UseShellExecute = true });
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to open file {item.FullPath}", ex);
            FileOperationStatus = $"Open error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SaveEditedFileAsync()
    {
        if (string.IsNullOrWhiteSpace(EditingFileFullPath)) return;
        try
        {
            IsFileEditorSaving = true;
            await File.WriteAllTextAsync(EditingFileFullPath, EditingFileContent ?? "");
            FileEditorStatusMessage = L10n.T("cs_editor_saved");
            RefreshFiles();
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to save file {EditingFileFullPath}", ex);
            FileEditorStatusMessage = $"Save error: {ex.Message}";
        }
        finally
        {
            IsFileEditorSaving = false;
        }
    }

    [RelayCommand]
    public void CloseFileEditor()
    {
        IsFileEditorOpen = false;
        EditingFileName = "";
        EditingFileFullPath = "";
        EditingFileContent = "";
        FileEditorStatusMessage = "";
    }

    [RelayCommand]
    public void DeleteFileItem(ServerFileItem? item)
    {
        if (item == null) return;
        try
        {
            if (item.IsDirectory && Directory.Exists(item.FullPath))
            {
                Directory.Delete(item.FullPath, true);
            }
            else if (File.Exists(item.FullPath))
            {
                File.Delete(item.FullPath);
            }
            RefreshFiles();
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to delete {item.FullPath}", ex);
            FileOperationStatus = $"Delete error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task UploadServerFilesAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentBrowsePath) || !Directory.Exists(CurrentBrowsePath))
            return;

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = L10n.T("cs_file_upload"),
            AllowMultiple = true
        });

        if (files == null || files.Count == 0) return;

        try
        {
            foreach (var f in files)
            {
                var local = f.Path.LocalPath;
                if (!string.IsNullOrEmpty(local) && File.Exists(local))
                {
                    var dest = Path.Combine(CurrentBrowsePath, Path.GetFileName(local));
                    File.Copy(local, dest, true);
                }
            }
            RefreshFiles();
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to upload file(s)", ex);
            FileOperationStatus = $"Upload error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenCurrentFolderInExplorer()
    {
        var target = Directory.Exists(CurrentBrowsePath) ? CurrentBrowsePath : ActiveServerDirectory;
        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to open current folder in explorer", ex);
        }
    }

    // Plugins & Mods Management
    [RelayCommand]
    public void TogglePlugin(ServerPluginItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;
        try
        {
            ServerCreatorService.TogglePluginItem(item);
            InstalledPlugins.Clear();
            foreach (var p in ServerCreatorService.GetInstalledPluginsOrMods(ActiveServerDirectory))
            {
                InstalledPlugins.Add(p);
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to toggle plugin {item.Name}", ex);
        }
    }

    [RelayCommand]
    public void DeletePlugin(ServerPluginItem item)
    {
        try
        {
            if (File.Exists(item.FullPath))
            {
                File.Delete(item.FullPath);
            }
            InstalledPlugins.Remove(item);
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Failed to delete plugin/mod {item.Name}: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task AddPluginJarAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Plugin / Mod (.jar)",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("JAR file (*.jar)")
                {
                    Patterns = new[] { "*.jar" }
                }
            }
        });

        if (files.Count == 0) return;

        var modsDir = Path.Combine(ActiveServerDirectory, "mods");
        var pluginsDir = Path.Combine(ActiveServerDirectory, "plugins");
        var targetFolder = Directory.Exists(modsDir) ? modsDir : pluginsDir;
        Directory.CreateDirectory(targetFolder);

        foreach (var file in files)
        {
            try
            {
                var localPath = file.Path.LocalPath;
                var destPath = Path.Combine(targetFolder, Path.GetFileName(localPath));
                File.Copy(localPath, destPath, true);
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"Failed to copy jar {file.Name}", ex);
            }
        }

        InstalledPlugins.Clear();
        foreach (var p in ServerCreatorService.GetInstalledPluginsOrMods(ActiveServerDirectory))
        {
            InstalledPlugins.Add(p);
        }
    }

    [RelayCommand]
    public void OpenPluginsFolder()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;

        var pluginsDir = Path.Combine(ActiveServerDirectory, "plugins");
        var modsDir = Path.Combine(ActiveServerDirectory, "mods");
        var dirToOpen = Directory.Exists(pluginsDir) ? pluginsDir : Directory.Exists(modsDir) ? modsDir : ActiveServerDirectory;
        try
        {
            Directory.CreateDirectory(dirToOpen);
            Process.Start(new ProcessStartInfo { FileName = dirToOpen, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to open plugins folder", ex);
        }
    }

    // Technical Settings (Tab 3: Settings)
    [RelayCommand]
    public async Task BrowseServerJavaAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = L10n.T("cs_java_browse"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Java Executable")
                {
                    Patterns = new[] { "java.exe", "javaw.exe", "java", "*" }
                }
            }
        });

        if (files != null && files.Count > 0)
        {
            var localPath = files[0].Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                ServerJavaPath = localPath;
            }
        }
    }

    [RelayCommand]
    public void SaveServerSettings()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;

        try
        {
            ServerCreatorService.SanitizeAndConfigureRunBat(ActiveServerDirectory, ServerJavaPath, ServerRamMb, ServerJvmArgs);
            ServerAllocatedRamDisplay = $"{ServerRamMb} MB";
            ServerSettingsStatusMessage = L10n.T("cs_settings_saved");
            ConfigStatusMessage = L10n.T("cs_settings_saved");
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to save server settings", ex);
            ServerSettingsStatusMessage = $"Save error: {ex.Message}";
        }
    }

    // World Reset & Danger Actions (Tab 3: Settings)
    [RelayCommand]
    public void RequestResetWorld()
    {
        ShowWorldResetConfirm = true;
        IsResetCountdownActive = true;
        CanConfirmResetWorld = false;
        ResetCountdownSeconds = 5;
        ResetButtonText = string.Format(L10n.T("cs_reset_world_sure"), 5);
        WorldResetStatusMessage = "";

        _resetCountdownTimer?.Stop();
        _resetCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _resetCountdownTimer.Tick += (s, e) =>
        {
            ResetCountdownSeconds--;
            if (ResetCountdownSeconds > 0)
            {
                ResetButtonText = string.Format(L10n.T("cs_reset_world_sure"), ResetCountdownSeconds);
            }
            else
            {
                _resetCountdownTimer?.Stop();
                IsResetCountdownActive = false;
                CanConfirmResetWorld = true;
                ResetButtonText = L10n.T("cs_reset_world_confirm");
            }
        };
        _resetCountdownTimer.Start();
    }

    [RelayCommand]
    public void CancelResetWorld()
    {
        _resetCountdownTimer?.Stop();
        ShowWorldResetConfirm = false;
        IsResetCountdownActive = false;
        CanConfirmResetWorld = false;
    }

    [RelayCommand]
    public void ConfirmResetWorld()
    {
        if (!CanConfirmResetWorld || string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;

        try
        {
            _resetCountdownTimer?.Stop();
            ServerCreatorService.ResetWorld(ActiveServerDirectory, PropsLevelName);
            ShowWorldResetConfirm = false;
            IsResetCountdownActive = false;
            CanConfirmResetWorld = false;
            WorldResetStatusMessage = $"World '{PropsLevelName}' folders removed. A new world will generate on next start!";
        }
        catch (Exception ex)
        {
            WorldResetStatusMessage = $"Failed to reset world: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenWorldFolder()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;
        var worldDir = Path.Combine(ActiveServerDirectory, string.IsNullOrWhiteSpace(PropsLevelName) ? "world" : PropsLevelName);
        try
        {
            Directory.CreateDirectory(worldDir);
            Process.Start(new ProcessStartInfo { FileName = worldDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to open world folder", ex);
        }
    }

    // Players Management
    [RelayCommand]
    public void AddPlayer()
    {
        if (string.IsNullOrWhiteSpace(NewPlayerName) || string.IsNullOrWhiteSpace(ActiveServerDirectory))
            return;

        var trimmed = NewPlayerName.Trim();
        NewPlayerName = "";

        switch (PlayersSubTab)
        {
            case 0:
                if (!OpsList.Any(p => p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    OpsList.Add(new ServerPlayerEntry { Name = trimmed, Level = 4 });
                    ServerCreatorService.SavePlayerList(ActiveServerDirectory, "ops.json", OpsList);
                }
                break;
            case 1:
                if (!WhitelistList.Any(p => p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    WhitelistList.Add(new ServerPlayerEntry { Name = trimmed });
                    ServerCreatorService.SavePlayerList(ActiveServerDirectory, "whitelist.json", WhitelistList);
                }
                break;
            case 2:
                if (!BansList.Any(p => p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    BansList.Add(new ServerPlayerEntry { Name = trimmed, Reason = "Banned by operator." });
                    ServerCreatorService.SavePlayerList(ActiveServerDirectory, "banned-players.json", BansList);
                }
                break;
        }
    }

    [RelayCommand]
    public void RemovePlayer(ServerPlayerEntry entry)
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;

        switch (PlayersSubTab)
        {
            case 0:
                OpsList.Remove(entry);
                ServerCreatorService.SavePlayerList(ActiveServerDirectory, "ops.json", OpsList);
                break;
            case 1:
                WhitelistList.Remove(entry);
                ServerCreatorService.SavePlayerList(ActiveServerDirectory, "whitelist.json", WhitelistList);
                break;
            case 2:
                BansList.Remove(entry);
                ServerCreatorService.SavePlayerList(ActiveServerDirectory, "banned-players.json", BansList);
                break;
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        _cts?.Cancel();
        _host.GoBack();
    }
}
