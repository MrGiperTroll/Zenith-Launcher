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
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System.IO.Compression;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class ServerItemEntry : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private Bitmap? _iconBitmap;
    [ObservableProperty] private bool _hasIcon;
}

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
    [ObservableProperty] private string _selectedVersion = "";
    [ObservableProperty] private ServerSoftwareOption _selectedSoftware;
    [ObservableProperty] private string _selectedLoaderBuild = "Latest (Auto)";
    public ObservableCollection<string> AvailableLoaderBuilds { get; } = new();
    public bool IsLoaderBuildVisible => SelectedSoftware != null && (SelectedSoftware.Id == "forge" || SelectedSoftware.Id == "neoforge" || SelectedSoftware.Id == "fabric");

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
    [ObservableProperty] private string _propsServerName = "";
    [ObservableProperty] private string _activeServerDirectory = "";
    [ObservableProperty] private int _selectedTab; // 0=Overview, 1=Properties, 2=Plugins, 3=Settings, 4=Players, 5=Files
    [ObservableProperty] private int _playersSubTab; // 0=Online, 1=Ops, 2=Whitelist, 3=Bans
    [ObservableProperty] private string _newPlayerName = "";
    [ObservableProperty] private string _configStatusMessage = "";
    [ObservableProperty] private bool _showWorldResetConfirm;
    [ObservableProperty] private string _worldResetStatusMessage = "";
    [ObservableProperty] private bool _showDeleteServerConfirm;

    // Master versions list for software compatibility filtering
    private readonly List<string> _allMinecraftVersions = new();

    // Server Icon state
    [ObservableProperty] private bool _hasServerIcon;
    [ObservableProperty] private Bitmap? _serverIconBitmap;

    // Console & Live Logs state (Tab 6)
    [ObservableProperty] private string _consoleLogText = "";
    [ObservableProperty] private string _consoleCommandText = "";

    // Customizable Quick Commands in Console tab
    public ObservableCollection<string> QuickCommands { get; } = new();
    [ObservableProperty] private string _newQuickCommandText = "";
    [ObservableProperty] private bool _isAddingQuickCommand;

    // World Management state (Tab 7)
    [ObservableProperty] private bool _hasWorldIcon;
    [ObservableProperty] private Bitmap? _worldIconBitmap;

    // World Management state (Tab 7)
    [ObservableProperty] private string _worldNameDisplay = "world";
    [ObservableProperty] private string _worldSizeDisplay = "0 MB";
    [ObservableProperty] private bool _isWorldSettingsOpen;
    [ObservableProperty] private string _worldStatusMessage = "";
    [ObservableProperty] private string _worldDifficulty = "normal";
    [ObservableProperty] private bool _worldHardcore;
    [ObservableProperty] private string _worldSeed = "";
    [ObservableProperty] private bool _worldGenerateStructures = true;
    [ObservableProperty] private bool _worldBonusChest;
    [ObservableProperty] private bool _ruleKeepInventory;
    [ObservableProperty] private bool _ruleMobGriefing = true;
    [ObservableProperty] private bool _ruleDoDaylightCycle = true;
    [ObservableProperty] private bool _ruleDoWeatherCycle = true;
    [ObservableProperty] private bool _ruleDoMobSpawning = true;
    public ObservableCollection<string> EnabledDatapacks { get; } = new();
    public ObservableCollection<string> DisabledDatapacks { get; } = new();

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
    [ObservableProperty] private bool _propsForceGamemode = false;
    [ObservableProperty] private bool _propsRequireResourcePack = false;
    [ObservableProperty] private string _propsResourcePack = "";
    [ObservableProperty] private string _propsResourcePackPrompt = "";
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
    public int[] RamOptions { get; } = { 512, 1024, 2048, 3072, 4096, 6144, 8192, 12288, 16384, 24576, 32768 };

    public string[] GamemodeOptions { get; } = { "survival", "creative", "adventure", "spectator" };
    public string[] DifficultyOptions { get; } = { "peaceful", "easy", "normal", "hard" };
    public string[] LevelTypeOptions { get; } = { "minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified" };

    public ObservableCollection<string> ExistingServers { get; } = new();
    public ObservableCollection<ServerItemEntry> ExistingServersList { get; } = new();
    [ObservableProperty] private ServerItemEntry? _selectedServerItem;
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
    public bool IsPlayersTab => SelectedTab == 4;
    public bool IsFilesTab => SelectedTab == 5;
    public bool IsConsoleTab => SelectedTab == 6;
    public bool IsWorldTab => SelectedTab == 7;

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
        OnPropertyChanged(nameof(IsConsoleTab));
        OnPropertyChanged(nameof(IsWorldTab));
        OnPropertyChanged(nameof(IsPlayersTab));
        OnPropertyChanged(nameof(IsFilesTab));
        if (value == 5)
        {
            RefreshFiles();
        }
        else if (value == 6)
        {
            LoadLatestLog();
        }
        else if (value == 7)
        {
            RefreshWorldInfo();
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
        if (AvailableSoftware.Count == 0)
        {
            foreach (var opt in ServerCreatorService.AvailableSoftware)
            {
                AvailableSoftware.Add(opt);
            }
        }

        if (SelectedSoftware == null)
        {
            SelectedSoftware = AvailableSoftware.FirstOrDefault() ?? ServerCreatorService.AvailableSoftware[0];
        }
    }

    public void RefreshExistingServers()
    {
        ExistingServers.Clear();
        foreach (var entry in ExistingServersList)
        {
            entry.IconBitmap?.Dispose();
        }
        ExistingServersList.Clear();

        foreach (var s in ServerCreatorService.GetExistingServers())
        {
            ExistingServers.Add(s);
            var iconPath = Path.Combine(ServerCreatorService.DefaultServersDirectory, s, "server-icon.png");
            Bitmap? bmp = null;
            if (File.Exists(iconPath))
            {
                try
                {
                    using var fs = File.OpenRead(iconPath);
                    bmp = new Bitmap(fs);
                }
                catch { }
            }
            ExistingServersList.Add(new ServerItemEntry
            {
                Name = s,
                IconBitmap = bmp,
                HasIcon = bmp != null
            });
        }
        HasExistingServers = ExistingServersList.Count > 0;
        if (SelectedServerName != null)
        {
            SelectedServerItem = ExistingServersList.FirstOrDefault(x => x.Name == SelectedServerName);
        }
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
            SelectedServerItem = ExistingServersList.FirstOrDefault(x => x.Name == value);
        }
        StateChanged?.Invoke();
    }

    partial void OnSelectedServerItemChanged(ServerItemEntry? value)
    {
        if (value != null && value.Name != SelectedServerName)
        {
            SelectedServerName = value.Name;
        }
    }

    public void LoadServerDetails()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory) || !Directory.Exists(ActiveServerDirectory))
            return;

        PropsServerName = SelectedServerName ?? "";
        LoadQuickCommands();

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
        if (props.TryGetValue("force-gamemode", out var fg) && bool.TryParse(fg, out var bFg)) PropsForceGamemode = bFg;
        if (props.TryGetValue("require-resource-pack", out var rrp) && bool.TryParse(rrp, out var bRrp)) PropsRequireResourcePack = bRrp;
        if (props.TryGetValue("resource-pack", out var rp)) PropsResourcePack = rp;
        if (props.TryGetValue("resource-pack-prompt", out var rpp)) PropsResourcePackPrompt = rpp;
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
        LoadServerIcon();
        RefreshWorldInfo();
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
            ["force-gamemode"] = PropsForceGamemode ? "true" : "false",
            ["spawn-animals"] = PropsSpawnAnimals ? "true" : "false",
            ["spawn-monsters"] = PropsSpawnMonsters ? "true" : "false",
            ["spawn-npcs"] = PropsSpawnNpcs ? "true" : "false",
            ["allow-nether"] = PropsAllowNether ? "true" : "false",
            ["entity-broadcast-range-percentage"] = PropsEntityBroadcastRangePercentage.ToString(),
            ["require-resource-pack"] = PropsRequireResourcePack ? "true" : "false",
            ["resource-pack"] = PropsResourcePack ?? "",
            ["resource-pack-prompt"] = PropsResourcePackPrompt ?? ""
        };

        ServerCreatorService.SaveProperties(ActiveServerDirectory, dict);
        if (!string.IsNullOrWhiteSpace(PropsServerName) && PropsServerName != SelectedServerName)
        {
            CommitRenameDirect(PropsServerName);
        }
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

    public bool CommitRenameDirect(string newName)
    {
        if (IsServerRunning) return false;
        newName = newName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(newName) || newName == SelectedServerName)
            return false;

        if (ServerCreatorService.RenameServer(SelectedServerName!, newName, out var err))
        {
            SelectedServerName = newName;
            ActiveServerDirectory = Path.Combine(ServerCreatorService.DefaultServersDirectory, newName);
            RefreshExistingServers();
            ConfigStatusMessage = "Server renamed successfully.";
            return true;
        }
        else
        {
            ConfigStatusMessage = err ?? "Failed to rename server.";
            return false;
        }
    }

    [RelayCommand]
    public void CancelRenameServer()
    {
        IsEditingServerName = false;
    }

    [RelayCommand]
    public void StepServerRamDown() => ServerRamMb = Math.Max(512, ServerRamMb - 512);

    [RelayCommand]
    public void StepServerRamUp() => ServerRamMb = Math.Min(32768, ServerRamMb + 512);

    [RelayCommand]
    public async Task CopyIpAsync() => await CopyAddressAsync(ServerIpDisplay);

    [RelayCommand]
    public async Task SelectAndCopyAddressAsync(string? address)
    {
        if (!string.IsNullOrWhiteSpace(address))
        {
            ServerIpDisplay = address;
        }
        await CopyAddressAsync(address);
    }

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
        SelectedTab = 6;
        LoadLatestLog();
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

            _allMinecraftVersions.Clear();
            _allMinecraftVersions.AddRange(versions);

            PopulateInitialSoftware();
            await UpdateAvailableVersionsForSelectedSoftwareAsync();
            await UpdateLoaderBuildsAsync();
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
        _ = UpdateLoaderBuildsAsync();
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
                _cts.Token,
                SelectedLoaderBuild);

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
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
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

                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) AppendConsoleLog(e.Data);
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) AppendConsoleLog(e.Data);
                };

                if (p.Start())
                {
                    _serverProcess = p;
                    _serverStdin = p.StandardInput;
                    _serverStdin.AutoFlush = true;
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
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

        [ObservableProperty] private bool _isRenamingFile;
    [ObservableProperty] private string _renamingFileNewName = "";
    private ServerFileItem? _renamingFileItem;

    [RelayCommand]
    public void StartRenameFile(ServerFileItem? item)
    {
        if (item == null) return;
        _renamingFileItem = item;
        RenamingFileNewName = item.Name;
        IsRenamingFile = true;
    }

    [RelayCommand]
    public void CancelRenameFile()
    {
        IsRenamingFile = false;
        _renamingFileItem = null;
    }

    [RelayCommand]
    public void ConfirmRenameFile()
    {
        if (_renamingFileItem == null || string.IsNullOrWhiteSpace(RenamingFileNewName)) return;
        try
        {
            var newPath = Path.Combine(CurrentBrowsePath, RenamingFileNewName.Trim());
            if (newPath == _renamingFileItem.FullPath) { CancelRenameFile(); return; }
            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                FileOperationStatus = "File or folder with this name already exists.";
                return;
            }
            if (_renamingFileItem.IsDirectory)
                Directory.Move(_renamingFileItem.FullPath, newPath);
            else
                File.Move(_renamingFileItem.FullPath, newPath);
            
            CancelRenameFile();
            RefreshFiles();
        }
        catch (Exception ex)
        {
            FileOperationStatus = $"Rename failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CreateNewFolder()
    {
        if (string.IsNullOrWhiteSpace(CurrentBrowsePath)) return;
        try {
            string baseName = "New Folder";
            string name = baseName;
            int counter = 1;
            while (Directory.Exists(Path.Combine(CurrentBrowsePath, name)) || File.Exists(Path.Combine(CurrentBrowsePath, name))) {
                name = $"{baseName} ({counter++})";
            }
            var di = Directory.CreateDirectory(Path.Combine(CurrentBrowsePath, name));
            RefreshFiles();
            var item = ServerFiles.FirstOrDefault(f => f.Name == name);
            if (item != null) StartRenameFile(item);
        } catch { }
    }

    [RelayCommand]
    public void CreateNewFile()
    {
        if (string.IsNullOrWhiteSpace(CurrentBrowsePath)) return;
        try {
            string baseName = "new_file";
            string ext = ".txt";
            string name = baseName + ext;
            int counter = 1;
            while (Directory.Exists(Path.Combine(CurrentBrowsePath, name)) || File.Exists(Path.Combine(CurrentBrowsePath, name))) {
                name = $"{baseName}_{counter++}{ext}";
            }
            File.WriteAllText(Path.Combine(CurrentBrowsePath, name), "");
            RefreshFiles();
            var item = ServerFiles.FirstOrDefault(f => f.Name == name);
            if (item != null) StartRenameFile(item);
        } catch { }
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

    private void PopulateInitialSoftware()
    {
        AvailableSoftware.Clear();
        foreach (var opt in ServerCreatorService.AvailableSoftware)
            AvailableSoftware.Add(opt);

        if (SelectedSoftware == null)
            SelectedSoftware = AvailableSoftware.FirstOrDefault() ?? ServerCreatorService.AvailableSoftware[0];
    }

    partial void OnSelectedSoftwareChanged(ServerSoftwareOption value)
    {
        _ = UpdateAvailableVersionsForSelectedSoftwareAsync();
        _ = UpdateLoaderBuildsAsync();
        OnPropertyChanged(nameof(IsLoaderBuildVisible));
        OnPropertyChanged(nameof(CanCreate));
    }

    public async Task UpdateAvailableVersionsForSelectedSoftwareAsync()
    {
        if (SelectedSoftware == null) return;

        // 1. Fast instant display using compatibility heuristic so UI doesn't stutter
        var compat = _allMinecraftVersions.Where(v => ServerCreatorService.IsSoftwareCompatibleWithVersion(SelectedSoftware.Id, v)).ToList();
        if (AvailableVersions.Count == 0 || !AvailableVersions.SequenceEqual(compat))
        {
            AvailableVersions.Clear();
            foreach (var v in compat)
                AvailableVersions.Add(v);

            if (AvailableVersions.Count > 0 && (string.IsNullOrEmpty(SelectedVersion) || !AvailableVersions.Contains(SelectedVersion)))
            {
                SelectedVersion = AvailableVersions[0];
            }
        }

        // 2. Query real official API/manifest for this core (Paper v3, Purpur, Fabric Meta, NeoForge, Forge, etc.)
        try
        {
            var realVersions = await _serverService.GetVersionsForSoftwareAsync(
                SelectedSoftware.Id,
                _host.ShowReleases,
                _host.ShowSnapshots,
                _host.ShowBetas,
                _host.ShowAlphas);

            if (realVersions.Count > 0 && !AvailableVersions.SequenceEqual(realVersions))
            {
                AvailableVersions.Clear();
                foreach (var v in realVersions)
                    AvailableVersions.Add(v);

                if (AvailableVersions.Count > 0 && (string.IsNullOrEmpty(SelectedVersion) || !AvailableVersions.Contains(SelectedVersion)))
                {
                    SelectedVersion = AvailableVersions[0];
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Failed to fetch exact versions for {SelectedSoftware.Id}: {ex.Message}");
        }
        finally
        {
            OnPropertyChanged(nameof(CanCreate));
        }
    }

    public async Task UpdateLoaderBuildsAsync()
    {
        AvailableLoaderBuilds.Clear();
        if (!IsLoaderBuildVisible || string.IsNullOrWhiteSpace(SelectedVersion))
        {
            SelectedLoaderBuild = "Latest (Auto)";
            return;
        }

        AvailableLoaderBuilds.Add("Latest (Auto)");
        try
        {
            var modLoaderService = new ModLoaderService();
            var loaderType = SelectedSoftware.Id switch
            {
                "forge" => "Forge",
                "neoforge" => "NeoForge",
                "fabric" => "Fabric",
                _ => ""
            };
            if (!string.IsNullOrEmpty(loaderType))
            {
                var builds = await modLoaderService.GetLoaderBuildsAsync(SelectedVersion, loaderType);
                foreach (var b in builds.Where(b => !string.IsNullOrWhiteSpace(b) && b != "Latest (Auto)"))
                {
                    AvailableLoaderBuilds.Add(b);
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Failed to load loader builds: {ex.Message}");
        }
        SelectedLoaderBuild = AvailableLoaderBuilds.FirstOrDefault() ?? "Latest (Auto)";
    }

    // --- Server Icon ---
    [RelayCommand]
    public async Task ChangeServerIconAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory) || !Directory.Exists(ActiveServerDirectory)) return;
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Server Icon (PNG / JPEG)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp" }
                }
            }
        });

        if (files.Count > 0)
        {
            try
            {
                var srcPath = files[0].Path.LocalPath;
                var destPath = Path.Combine(ActiveServerDirectory, "server-icon.png");

                // Minecraft server strictly requires 64x64 PNG format (avoids Bad PNG Signature error)
                using (var srcBmp = new Bitmap(srcPath))
                using (var scaled = srcBmp.CreateScaledBitmap(new Avalonia.PixelSize(64, 64), BitmapInterpolationMode.HighQuality))
                {
#pragma warning disable CS0618
                    scaled.Save(destPath);
#pragma warning restore CS0618
                }

                LoadServerIcon();

                var item = ExistingServersList.FirstOrDefault(s => s.Name.Equals(SelectedServerName, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    item.IconBitmap?.Dispose();
                    using var stream = File.OpenRead(destPath);
                    item.IconBitmap = new Bitmap(stream);
                    item.HasIcon = true;
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Error("Failed to update server icon", ex);
            }
        }
    }

    public void LoadServerIcon()
    {
        ServerIconBitmap?.Dispose();
        ServerIconBitmap = null;
        HasServerIcon = false;

        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;
        var iconPath = Path.Combine(ActiveServerDirectory, "server-icon.png");
        if (File.Exists(iconPath))
        {
            try
            {
                using var stream = File.OpenRead(iconPath);
                ServerIconBitmap = new Bitmap(stream);
                HasServerIcon = true;
            }
            catch { }
        }
    }

    // --- Console Commands & Log Streaming ---
    [RelayCommand]
    public void SendConsoleCommand()
    {
        if (string.IsNullOrWhiteSpace(ConsoleCommandText)) return;
        var cmd = ConsoleCommandText.Trim();
        ConsoleCommandText = "";

        AppendConsoleLog($"> {cmd}");

        if (_serverStdin != null && IsServerRunning)
        {
            try
            {
                _serverStdin.WriteLine(cmd);
                _serverStdin.Flush();
            }
            catch (Exception ex)
            {
                AppendConsoleLog($"[Error: {ex.Message}]");
            }
        }
        else
        {
            AppendConsoleLog("[Server is offline. Start the server to execute commands.]");
        }
    }

    [RelayCommand]
    public void ClearConsole()
    {
        ConsoleLogText = "";
    }

    public void AppendConsoleLog(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (string.IsNullOrEmpty(ConsoleLogText))
                ConsoleLogText = line;
            else
            {
                if (ConsoleLogText.Length > 200000)
                    ConsoleLogText = ConsoleLogText.Substring(ConsoleLogText.Length - 100000);
                ConsoleLogText += "\n" + line;
            }
        });
    }

    public void LoadLatestLog()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;
        var logFile = Path.Combine(ActiveServerDirectory, "logs", "latest.log");
        if (File.Exists(logFile))
        {
            try
            {
                using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var text = sr.ReadToEnd();
                if (text.Length > 150000)
                    text = text.Substring(text.Length - 150000);
                ConsoleLogText = text.Replace("\r\n", "\n");
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Failed to read latest.log: {ex.Message}");
            }
        }
        else if (!IsServerRunning)
        {
            ConsoleLogText = "No log file found yet. Start the server to generate logs and view the live console.";
        }
    }

    private void LoadQuickCommands()
    {
        QuickCommands.Clear();
        var defaultCommands = new[] { "help", "list", "save-all", "stop" };
        if (!string.IsNullOrWhiteSpace(ActiveServerDirectory) && Directory.Exists(ActiveServerDirectory))
        {
            var path = Path.Combine(ActiveServerDirectory, "quick-commands.json");
            if (File.Exists(path))
            {
                try
                {
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
                    if (list != null && list.Count > 0)
                    {
                        foreach (var c in list) QuickCommands.Add(c);
                        return;
                    }
                }
                catch { }
            }
        }
        foreach (var c in defaultCommands) QuickCommands.Add(c);
    }

    private void SaveQuickCommands()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory) || !Directory.Exists(ActiveServerDirectory)) return;
        try
        {
            var path = Path.Combine(ActiveServerDirectory, "quick-commands.json");
            var json = System.Text.Json.JsonSerializer.Serialize(QuickCommands.ToList());
            File.WriteAllText(path, json);
        }
        catch { }
    }

    [RelayCommand]
    public void ExecuteQuickCommand(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return;
        ConsoleCommandText = cmd;
        SendConsoleCommand();
    }

    [RelayCommand]
    public void AddQuickCommand()
    {
        if (!string.IsNullOrWhiteSpace(NewQuickCommandText))
        {
            var cmd = NewQuickCommandText.Trim();
            if (!QuickCommands.Contains(cmd))
            {
                QuickCommands.Add(cmd);
                SaveQuickCommands();
            }
            NewQuickCommandText = "";
            IsAddingQuickCommand = false;
        }
    }

    [RelayCommand]
    public void RemoveQuickCommand(string? cmd)
    {
        if (!string.IsNullOrWhiteSpace(cmd))
        {
            QuickCommands.Remove(cmd);
            SaveQuickCommands();
        }
    }

    [RelayCommand]
    public void ToggleAddQuickCommand()
    {
        IsAddingQuickCommand = !IsAddingQuickCommand;
    }

    // --- World Management ---
    public void RefreshWorldInfo()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory) || !Directory.Exists(ActiveServerDirectory)) return;

        WorldNameDisplay = !string.IsNullOrWhiteSpace(PropsLevelName) ? PropsLevelName : "world";
        WorldDifficulty = PropsDifficulty;
        WorldHardcore = PropsHardcore;
        WorldSeed = PropsLevelSeed;
        WorldGenerateStructures = PropsGenerateStructures;

        WorldIconBitmap?.Dispose();
        WorldIconBitmap = null;
        HasWorldIcon = false;

        var worldDir = Path.Combine(ActiveServerDirectory, WorldNameDisplay);
        if (Directory.Exists(worldDir))
        {
            var iconPath = Path.Combine(worldDir, "icon.png");
            if (File.Exists(iconPath))
            {
                try
                {
                    using var fs = File.OpenRead(iconPath);
                    WorldIconBitmap = new Bitmap(fs);
                    HasWorldIcon = true;
                }
                catch { }
            }

            try
            {
                long totalBytes = Directory.GetFiles(worldDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                if (totalBytes > 1024 * 1024 * 1024)
                    WorldSizeDisplay = $"{(totalBytes / (1024.0 * 1024 * 1024)):F2} GB";
                else
                    WorldSizeDisplay = $"{(totalBytes / (1024.0 * 1024)):F1} MB";
            }
            catch
            {
                WorldSizeDisplay = "Unknown";
            }

            EnabledDatapacks.Clear();
            DisabledDatapacks.Clear();
            var dpDir = Path.Combine(worldDir, "datapacks");
            if (Directory.Exists(dpDir))
            {
                foreach (var file in Directory.GetFiles(dpDir, "*.zip"))
                {
                    if (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        DisabledDatapacks.Add(Path.GetFileName(file));
                    else
                        EnabledDatapacks.Add(Path.GetFileName(file));
                }
                foreach (var dir in Directory.GetDirectories(dpDir))
                {
                    if (dir.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        DisabledDatapacks.Add(Path.GetFileName(dir));
                    else
                        EnabledDatapacks.Add(Path.GetFileName(dir));
                }
            }
        }
        else
        {
            WorldSizeDisplay = "Not generated yet";
        }
    }

    [RelayCommand]
    public async Task DownloadWorldAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;
        var worldDir = Path.Combine(ActiveServerDirectory, WorldNameDisplay);
        if (!Directory.Exists(worldDir))
        {
            WorldStatusMessage = "World folder does not exist yet.";
            return;
        }

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
        if (topLevel?.StorageProvider == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save World Backup",
            DefaultExtension = "zip",
            SuggestedFileName = $"{WorldNameDisplay}-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        });

        if (file != null)
        {
            try
            {
                WorldStatusMessage = "Compressing world...";
                var destZip = file.Path.LocalPath;
                if (File.Exists(destZip)) File.Delete(destZip);
                await Task.Run(() => ZipFile.CreateFromDirectory(worldDir, destZip));
                WorldStatusMessage = $"World saved to {Path.GetFileName(destZip)}!";
            }
            catch (Exception ex)
            {
                WorldStatusMessage = $"Failed to export world: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    public async Task UploadWorldAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;
        if (IsServerRunning)
        {
            WorldStatusMessage = "Please stop the server before uploading a new world.";
            return;
        }

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select World ZIP Archive",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ZIP Archive") { Patterns = new[] { "*.zip" } }
            }
        });

        if (files.Count > 0)
        {
            try
            {
                WorldStatusMessage = "Importing world...";
                var srcZip = files[0].Path.LocalPath;
                var worldDir = Path.Combine(ActiveServerDirectory, WorldNameDisplay);
                if (Directory.Exists(worldDir))
                {
                    Directory.Delete(worldDir, recursive: true);
                }
                Directory.CreateDirectory(worldDir);
                await Task.Run(() => ZipFile.ExtractToDirectory(srcZip, worldDir, overwriteFiles: true));
                RefreshWorldInfo();
                WorldStatusMessage = "World uploaded successfully!";
            }
            catch (Exception ex)
            {
                WorldStatusMessage = $"Failed to upload world: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    public void ToggleWorldSettings()
    {
        IsWorldSettingsOpen = !IsWorldSettingsOpen;
    }

    [RelayCommand]
    public void SaveWorldSettings()
    {
        PropsDifficulty = WorldDifficulty;
        PropsHardcore = WorldHardcore;
        PropsLevelSeed = WorldSeed;
        PropsGenerateStructures = WorldGenerateStructures;

        if (!string.IsNullOrWhiteSpace(ActiveServerDirectory) && Directory.Exists(ActiveServerDirectory))
        {
            var dict = new Dictionary<string, string>
            {
                ["difficulty"] = PropsDifficulty,
                ["hardcore"] = PropsHardcore ? "true" : "false",
                ["level-seed"] = PropsLevelSeed,
                ["generate-structures"] = PropsGenerateStructures ? "true" : "false"
            };
            ServerCreatorService.SaveProperties(ActiveServerDirectory, dict);
        }

        WorldStatusMessage = "World settings saved successfully!";
        IsWorldSettingsOpen = false;
    }

}
