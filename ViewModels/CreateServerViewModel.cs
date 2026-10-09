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

    // Console state (Tab 6). Each server owns an isolated buffer, see _consoleBuffers.
    [ObservableProperty] private string _consoleLogText = "";
    [ObservableProperty] private string _consoleCommandText = "";

    /// <summary>Per-server console buffers keyed by server name (case-insensitive).</summary>
    private readonly Dictionary<string, string> _consoleBuffers = new(StringComparer.OrdinalIgnoreCase);
    private string? _consoleOwner;
    private string? _runningServerName;

    /// <summary>Raised after the visible console text changes so the view can auto-scroll.</summary>
    public event Action? ConsoleUpdated;

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
    [ObservableProperty] private bool _propsGenerateStructures = true;
    [ObservableProperty] private bool _propsSpawnAnimals = true;
    [ObservableProperty] private bool _propsSpawnMonsters = true;
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
    [ObservableProperty] private string _pluginsTabTitle = "Plugins";
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

    public string[] GamemodeOptions { get; } = { "survival", "creative", "adventure", "spectator" };
    public string[] DifficultyOptions { get; } = { "peaceful", "easy", "normal", "hard" };

    // Version-gated server.properties visibility (dynamic per game version).
    [ObservableProperty] private string _serverMinecraftVersion = "";
    [ObservableProperty] private bool _isSimulationDistanceVisible = true;
    [ObservableProperty] private bool _isRequireResourcePackVisible = true;
    [ObservableProperty] private bool _isResourcePackPromptVisible = true;
    [ObservableProperty] private bool _isEntityBroadcastRangeVisible = true;

    public void UpdateVersionGatedVisibility()
    {
        var v = ServerMinecraftVersion;
        IsSimulationDistanceVisible = ServerPropertiesRules.IsSupported(ServerPropertiesRules.SimulationDistance, v);
        IsRequireResourcePackVisible = ServerPropertiesRules.IsSupported(ServerPropertiesRules.RequireResourcePack, v);
        IsResourcePackPromptVisible = ServerPropertiesRules.IsResourcePackPromptSupported(v);
        IsEntityBroadcastRangeVisible = ServerPropertiesRules.IsSupported(ServerPropertiesRules.EntityBroadcastRange, v);
    }

    /// <summary>
    /// "Plugins" by default, "Mods" on modded cores (Forge / NeoForge / Fabric).
    /// </summary>
    public void UpdatePluginsTabTitle()
    {
        var s = ServerSoftwareDisplay ?? "";
        bool modded = s.Contains("forge", StringComparison.OrdinalIgnoreCase)
            || s.Contains("fabric", StringComparison.OrdinalIgnoreCase);
        PluginsTabTitle = L10n.T(modded ? "cs_tab_mods" : "cs_tab_plugins_simple");
    }

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

    // File table sorting (clickable column headers).
    [ObservableProperty] private string _fileSortColumn = "Name";
    [ObservableProperty] private bool _fileSortDescending;
    public string FileSortNameArrow => FileSortColumn == "Name" ? (FileSortDescending ? "▼" : "▲") : "";
    public string FileSortDateArrow => FileSortColumn == "Date" ? (FileSortDescending ? "▼" : "▲") : "";
    public string FileSortSizeArrow => FileSortColumn == "Size" ? (FileSortDescending ? "▼" : "▲") : "";

    [RelayCommand]
    public void SortFilesBy(string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return;
        if (string.Equals(FileSortColumn, column, StringComparison.OrdinalIgnoreCase))
            FileSortDescending = !FileSortDescending;
        else
        {
            FileSortColumn = column;
            FileSortDescending = false;
        }
        OnPropertyChanged(nameof(FileSortNameArrow));
        OnPropertyChanged(nameof(FileSortDateArrow));
        OnPropertyChanged(nameof(FileSortSizeArrow));
        _ = RefreshFilesAsync();
    }

    private IEnumerable<ServerFileItem> ApplyFileSort(List<ServerFileItem> items)
    {
        var ordered = items.OrderByDescending(x => x.IsDirectory);
        return FileSortColumn switch
        {
            "Date" => FileSortDescending
                ? ordered.ThenByDescending(x => x.ModifiedDisplay).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenBy(x => x.ModifiedDisplay).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "Size" => FileSortDescending
                ? ordered.ThenByDescending(x => x.SizeBytes).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenBy(x => x.SizeBytes).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            _ => FileSortDescending
                ? ordered.ThenByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
        };
    }

    // File listing cancellation (overlapping refreshes) and path isolation root guard.
    private CancellationTokenSource? _filesCts;

    /// <summary>Chroot guard: true only if path resolves inside the server folder.</summary>
    public static bool IsPathInsideRoot(string root, string? path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (p.Equals(r, StringComparison.OrdinalIgnoreCase)) return true;
            var rel = Path.GetRelativePath(r, p);
            return !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
        }
        catch { return false; }
    }

    private bool IsInActiveServer(string? path) => IsPathInsideRoot(ActiveServerDirectory, path);

    private static bool IsSafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var t = name.Trim();
        return t.Length > 0 && t != "." && t != ".." && !t.StartsWith("..", StringComparison.Ordinal)
            && t.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !t.Contains('/') && !t.Contains('\\');
    }

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
            _ = RefreshFilesAsync();
        }
        else if (value == 6)
        {
            if (string.IsNullOrEmpty(ConsoleLogText))
                _ = LoadLatestLogAsync();
            else
                ConsoleUpdated?.Invoke();
        }
        else if (value == 7)
        {
            _ = RefreshWorldInfoAsync();
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
        _uptimeTimer.Tick += OnUptimeTick;
        _ = InitializeAsync();
    }

    private void OnUptimeTick(object? sender, EventArgs e)
    {
        if (IsServerRunning)
        {
            var span = DateTime.Now - _serverStartTime;
            UptimeText = span.ToString(@"hh\:mm\:ss");
        }
    }

    /// <summary>
    /// Async startup: heavy disk/network work runs off the UI thread so opening
    /// the Servers tab never freezes the interface.
    /// </summary>
    private async Task InitializeAsync()
    {
        PluginsTabTitle = L10n.T("cs_tab_plugins_simple");
        await RefreshExistingServersAsync();
        if (ExistingServers.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(SelectedServerName))
                SelectedServerName = ExistingServers[0];
            IsDashboardMode = true;
            IsCreatingNewServer = false;
        }
        else
        {
            IsDashboardMode = false;
            IsCreatingNewServer = true;
        }
        await LoadVersionsAsync();
    }

    /// <summary>Stops timers and releases bitmaps/process handles held by this VM.</summary>
    public void Teardown()
    {
        try { _uptimeTimer.Stop(); } catch { }
        _uptimeTimer.Tick -= OnUptimeTick;
        try
        {
            _resetCountdownTimer?.Stop();
            if (_resetCountdownTimer != null)
                _resetCountdownTimer.Tick -= OnResetCountdownTick;
        }
        catch { }
        try { _stopTimeoutCts?.Cancel(); _stopTimeoutCts?.Dispose(); } catch { }
        try { _filesCts?.Cancel(); _filesCts?.Dispose(); } catch { }
        try
        {
            foreach (var entry in ExistingServersList)
                entry.IconBitmap?.Dispose();
            ServerIconBitmap?.Dispose();
            WorldIconBitmap?.Dispose();
        }
        catch { }
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

    public async Task RefreshExistingServersAsync()
    {
        List<(string Name, byte[]? IconBytes)> snapshot;
        try
        {
            snapshot = await Task.Run(() =>
            {
                var result = new List<(string, byte[]?)>();
                foreach (var s in ServerCreatorService.GetExistingServers())
                {
                    byte[]? bytes = null;
                    try
                    {
                        var iconPath = Path.Combine(ServerCreatorService.DefaultServersDirectory, s, "server-icon.png");
                        if (File.Exists(iconPath))
                            bytes = File.ReadAllBytes(iconPath);
                    }
                    catch { }
                    result.Add((s, bytes));
                }
                return result;
            });
        }
        catch { return; }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var entry in ExistingServersList)
                entry.IconBitmap?.Dispose();
            ExistingServers.Clear();
            ExistingServersList.Clear();

            foreach (var (name, bytes) in snapshot)
            {
                ExistingServers.Add(name);
                Bitmap? bmp = null;
                if (bytes != null)
                {
                    try
                    {
                        using var ms = new MemoryStream(bytes);
                        bmp = new Bitmap(ms);
                    }
                    catch { }
                }
                ExistingServersList.Add(new ServerItemEntry
                {
                    Name = name,
                    IconBitmap = bmp,
                    HasIcon = bmp != null
                });
            }
            HasExistingServers = ExistingServersList.Count > 0;
            if (SelectedServerName != null)
            {
                SelectedServerItem = ExistingServersList.FirstOrDefault(x => x.Name == SelectedServerName);
            }
        });
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
    public async Task ConfirmDeleteServer()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;

        try
        {
            var deletedName = SelectedServerName;
            if (deletedName != null)
            {
                _consoleBuffers.Remove(deletedName);
                if (string.Equals(_consoleOwner, deletedName, StringComparison.OrdinalIgnoreCase))
                    _consoleOwner = null;
            }
            await Task.Run(() => ServerCreatorService.DeleteServer(ActiveServerDirectory));
            ShowDeleteServerConfirm = false;
            await RefreshExistingServersAsync();
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
            _ = LoadServerDetailsAsync();
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
        // Clicking another server while the creation form is open leaves the
        // form: without this the click only changes the name behind the form
        // and navigation looks blocked.
        if (value != null && IsCreatingNewServer)
        {
            IsCreatingNewServer = false;
            IsDashboardMode = true;
            StateChanged?.Invoke();
        }
    }

    /// <summary>Flushes the visible console into its owner's buffer.</summary>
    private void FlushConsoleBuffer()
    {
        if (!string.IsNullOrWhiteSpace(_consoleOwner))
            _consoleBuffers[_consoleOwner] = ConsoleLogText ?? "";
    }

    public async Task LoadServerDetailsAsync()
    {
        var dir = ActiveServerDirectory;
        var name = SelectedServerName;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return;

        // Switch isolated console: stash the outgoing buffer, restore the incoming one.
        FlushConsoleBuffer();
        _consoleOwner = name;
        if (!string.IsNullOrWhiteSpace(name) && _consoleBuffers.TryGetValue(name, out var buffered))
            ConsoleLogText = buffered;
        else
            ConsoleLogText = "";

        PropsServerName = name ?? "";

        // server.properties (world-name / world-type / world-seed are intentionally
        // not part of this editor).
        var props = await Task.Run(() => ServerCreatorService.LoadProperties(dir));
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
        if (props.TryGetValue("generate-structures", out var gs) && bool.TryParse(gs, out var bGs)) PropsGenerateStructures = bGs;
        if (props.TryGetValue("spawn-animals", out var sa) && bool.TryParse(sa, out var bSa)) PropsSpawnAnimals = bSa;
        if (props.TryGetValue("spawn-monsters", out var sm) && bool.TryParse(sm, out var bSm)) PropsSpawnMonsters = bSm;
        if (props.TryGetValue("allow-nether", out var an) && bool.TryParse(an, out var bAn)) PropsAllowNether = bAn;
        if (props.TryGetValue("entity-broadcast-range-percentage", out var eb) && int.TryParse(eb, out var iEb)) PropsEntityBroadcastRangePercentage = iEb;

        // Plugins / mods (zip inspection is slow -> background).
        var plugins = await Task.Run(() => ServerCreatorService.GetInstalledPluginsOrMods(dir));
        InstalledPlugins.Clear();
        foreach (var p in plugins)
        {
            InstalledPlugins.Add(p);
        }

        // Load player lists
        OpsList.Clear();
        foreach (var op in ServerCreatorService.LoadPlayerList(dir, "ops.json"))
        {
            OpsList.Add(op);
        }

        WhitelistList.Clear();
        foreach (var w in ServerCreatorService.LoadPlayerList(dir, "whitelist.json"))
        {
            WhitelistList.Add(w);
        }

        BansList.Clear();
        foreach (var b in ServerCreatorService.LoadPlayerList(dir, "banned-players.json"))
        {
            BansList.Add(b);
        }

        var meta = await Task.Run(() =>
        {
            ServerCreatorService.GetServerMetadata(dir, out var soft, out var ramDisplay, out var ramMb, out var javaPath, out var jvmArgs);
            return (soft, ramDisplay, ramMb, javaPath, jvmArgs, version: ServerCreatorService.GetServerVersion(dir));
        });
        ServerSoftwareDisplay = meta.soft;
        ServerAllocatedRamDisplay = meta.ramDisplay;
        ServerMinecraftVersion = meta.version;
        UpdateVersionGatedVisibility();
        UpdatePluginsTabTitle();
        // Set RAM before JVM args: the RAM-changed hook rewrites -Xmx inside the
        // args, and the loaded args assignment right after wins regardless.
        ServerRamMb = meta.ramMb;
        ServerJavaPath = meta.javaPath;
        ServerJvmArgs = ServerPropertiesRules.StripRamFlags(meta.jvmArgs);
        ServerJvmArgs = ServerPropertiesRules.WithRamFlags(ServerJvmArgs, ServerRamMb);
        ServerSettingsStatusMessage = "";
        await RefreshNetworkEndpointsAsync();

        _ = RefreshOnlinePlayersAsync();

        if (IsPathInsideRoot(dir, CurrentBrowsePath) && Directory.Exists(CurrentBrowsePath))
        {
            // stay where the user was inside this server
        }
        else
        {
            CurrentBrowsePath = dir;
        }
        _ = LoadServerIconAsync();
        _ = RefreshWorldInfoAsync();
        if (SelectedTab == 5)
        {
            _ = RefreshFilesAsync();
        }

        ConfigStatusMessage = "";
        WorldResetStatusMessage = "";
        ShowWorldResetConfirm = false;
        IsResetCountdownActive = false;
        CanConfirmResetWorld = false;
        try
        {
            if (_resetCountdownTimer != null)
            {
                _resetCountdownTimer.Stop();
                _resetCountdownTimer.Tick -= OnResetCountdownTick;
            }
        }
        catch { }
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
                });
            });
        }
        catch (OperationCanceledException) { }
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
    public async Task CommitRenameServer()
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
            FlushConsoleBuffer();
            if (!string.IsNullOrWhiteSpace(SelectedServerName) && _consoleBuffers.TryGetValue(SelectedServerName, out var movedBuf))
            {
                _consoleBuffers.Remove(SelectedServerName);
                _consoleBuffers[newName] = movedBuf;
            }
            _consoleOwner = newName;
            SelectedServerName = newName;
            ActiveServerDirectory = Path.Combine(ServerCreatorService.DefaultServersDirectory, newName);
            await RefreshExistingServersAsync();
            ConfigStatusMessage = "Server renamed successfully.";
        }
        else
        {
            ConfigStatusMessage = err ?? "Failed to rename server.";
        }
        IsEditingServerName = false;
    }

    public async Task CommitRenameDirectAsync(string newName)
    {
        if (IsServerRunning) return;
        newName = newName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(newName) || newName == SelectedServerName)
            return;

        if (ServerCreatorService.RenameServer(SelectedServerName!, newName, out var err))
        {
            FlushConsoleBuffer();
            if (!string.IsNullOrWhiteSpace(SelectedServerName) && _consoleBuffers.TryGetValue(SelectedServerName, out var movedBuf))
            {
                _consoleBuffers.Remove(SelectedServerName);
                _consoleBuffers[newName] = movedBuf;
            }
            _consoleOwner = newName;
            SelectedServerName = newName;
            ActiveServerDirectory = Path.Combine(ServerCreatorService.DefaultServersDirectory, newName);
            await RefreshExistingServersAsync();
            ConfigStatusMessage = "Server renamed successfully.";
        }
        else
        {
            ConfigStatusMessage = err ?? "Failed to rename server.";
        }
    }

    public bool CommitRenameDirect(string newName)
    {
        // Sync wrapper for code-behind (header textbox): renames now, refreshes async.
        if (IsServerRunning) return false;
        newName = newName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(newName) || newName == SelectedServerName)
            return false;

        if (ServerCreatorService.RenameServer(SelectedServerName!, newName, out var err))
        {
            FlushConsoleBuffer();
            if (!string.IsNullOrWhiteSpace(SelectedServerName) && _consoleBuffers.TryGetValue(SelectedServerName, out var movedBuf))
            {
                _consoleBuffers.Remove(SelectedServerName);
                _consoleBuffers[newName] = movedBuf;
            }
            _consoleOwner = newName;
            SelectedServerName = newName;
            ActiveServerDirectory = Path.Combine(ServerCreatorService.DefaultServersDirectory, newName);
            _ = RefreshExistingServersAsync();
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

    /// <summary>
    /// Keeps the launch-arguments flags in sync when the RAM slider moves,
    /// so the -Xmx value in the textbox never goes stale.
    /// </summary>
    partial void OnServerRamMbChanged(int value)
    {
        try
        {
            var synced = ServerPropertiesRules.WithRamFlags(ServerJvmArgs, value);
            if (synced != ServerJvmArgs)
                ServerJvmArgs = synced;
        }
        catch { }
    }

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

    public async Task RefreshNetworkEndpointsAsync()
    {
        var port = int.TryParse(PropsPort, out var p) ? p : 25565;
        List<ServerNetworkEndpoint> list;
        try
        {
            list = await Task.Run(() => ServerCreatorService.GetLocalNetworkEndpoints(port));
        }
        catch { return; }
        NetworkEndpoints.Clear();
        foreach (var ep in list)
        {
            NetworkEndpoints.Add(ep);
        }
        ServerIpDisplay = $"localhost:{port}";
    }

    public void RefreshNetworkEndpoints() => _ = RefreshNetworkEndpointsAsync();

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
        try { _stopTimeoutCts?.Dispose(); } catch { }
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
            _runningServerName = null;
            try { _stopTimeoutCts?.Cancel(); _stopTimeoutCts?.Dispose(); } catch { }
            _stopTimeoutCts = null;
            _uptimeTimer.Stop();
            UptimeText = "00:00:00";
            try { _serverStdin?.Dispose(); } catch { }
            try { _serverProcess?.Dispose(); } catch { }
            _serverProcess = null;
            _serverStdin = null;
        }
    }

    [RelayCommand]
    public void OpenLogs()
    {
        SelectedTab = 6;
        if (string.IsNullOrEmpty(ConsoleLogText))
            _ = LoadLatestLogAsync();
        else
            ConsoleUpdated?.Invoke();
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

            await RefreshExistingServersAsync();
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
                        _runningServerName = null;
                        try { _stopTimeoutCts?.Cancel(); _stopTimeoutCts?.Dispose(); } catch { }
                        _stopTimeoutCts = null;
                        _uptimeTimer.Stop();
                        UptimeText = "00:00:00";
                        try { _serverStdin?.Dispose(); } catch { }
                        var proc = _serverProcess;
                        _serverProcess = null;
                        _serverStdin = null;
                        try { proc?.Dispose(); } catch { }
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
                    _runningServerName = SelectedServerName;
                    FlushConsoleBuffer();
                    if (!string.IsNullOrWhiteSpace(_runningServerName))
                        _consoleBuffers[_runningServerName] = ConsoleLogText ?? "";
                    _consoleOwner = _runningServerName;
                    AppendConsoleLog("=== Server started ===");
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
    public async Task RefreshFilesAsync()
    {
        _filesCts?.Cancel();
        try { _filesCts?.Dispose(); } catch { }
        _filesCts = new CancellationTokenSource();
        var ct = _filesCts.Token;

        if (string.IsNullOrWhiteSpace(ActiveServerDirectory) || !Directory.Exists(ActiveServerDirectory))
        {
            ServerFiles.Clear();
            IsFilesEmpty = true;
            return;
        }

        // Chroot: never browse outside the server folder (resolves ".." and symlinks).
        if (string.IsNullOrWhiteSpace(CurrentBrowsePath) || !Directory.Exists(CurrentBrowsePath) ||
            !IsInActiveServer(CurrentBrowsePath))
        {
            CurrentBrowsePath = ActiveServerDirectory;
        }

        try
        {
            var browsePath = CurrentBrowsePath;
            var activeFull = Path.GetFullPath(ActiveServerDirectory).TrimEnd('\\', '/');
            var currFull = Path.GetFullPath(browsePath).TrimEnd('\\', '/');
            var canUp = !string.Equals(activeFull, currFull, StringComparison.OrdinalIgnoreCase);
            var rel = Path.GetRelativePath(activeFull, currFull);
            var relDisplay = string.IsNullOrWhiteSpace(rel) || rel == "." ? "/" : "/" + rel.Replace('\\', '/');

            var items = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var result = new List<ServerFileItem>();

                var dirs = Directory.GetDirectories(browsePath);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                foreach (var d in dirs)
                {
                    ct.ThrowIfCancellationRequested();
                    var dirInfo = new DirectoryInfo(d);
                    result.Add(new ServerFileItem
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

                var files = Directory.GetFiles(browsePath);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
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

                    result.Add(new ServerFileItem
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
                return result;
            }, ct);

            if (ct.IsCancellationRequested) return;
            CanNavigateUp = canUp;
            CurrentBrowseRelativePath = relDisplay;
            ServerFiles.Clear();
            foreach (var it in ApplyFileSort(items)) ServerFiles.Add(it);
            IsFilesEmpty = ServerFiles.Count == 0;
            FileOperationStatus = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            LauncherLog.Error("Failed to refresh server files", ex);
            FileOperationStatus = ex.Message;
        }
    }

    public void RefreshFiles() => _ = RefreshFilesAsync();

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
    public void NavigateUpDirectory()
    {
        if (!CanNavigateUp) return;
        try
        {
            var parent = Directory.GetParent(CurrentBrowsePath)?.FullName;
            if (parent != null && IsInActiveServer(parent))
            {
                CurrentBrowsePath = parent;
                _ = RefreshFilesAsync();
            }
        }
        catch { }
    }

    [RelayCommand]
    public async Task NavigateIntoFileItem(ServerFileItem? item)
    {
        if (item == null || !IsInActiveServer(item.FullPath)) return;
        if (item.IsDirectory)
        {
            CurrentBrowsePath = item.FullPath;
            await RefreshFilesAsync();
        }
        else
        {
            await OpenFileItem(item);
        }
    }

    [RelayCommand]
    public async Task OpenFileItem(ServerFileItem? item)
    {
        if (item == null || !IsInActiveServer(item.FullPath)) return;
        try
        {
            if (item.IsDirectory)
            {
                CurrentBrowsePath = item.FullPath;
                await RefreshFilesAsync();
            }
            else if (File.Exists(item.FullPath))
            {
                var ext = Path.GetExtension(item.FullPath);
                if (TextExtensions.Contains(ext))
                {
                    var info = new FileInfo(item.FullPath);
                    if (info.Length > 2 * 1024 * 1024)
                    {
                        FileOperationStatus = L10n.T("cs_file_too_large");
                        return;
                    }
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
        if (string.IsNullOrWhiteSpace(EditingFileFullPath) || !IsInActiveServer(EditingFileFullPath)) return;
        try
        {
            IsFileEditorSaving = true;
            await File.WriteAllTextAsync(EditingFileFullPath, EditingFileContent ?? "");
            FileEditorStatusMessage = L10n.T("cs_editor_saved");
            await RefreshFilesAsync();
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
    public async Task ConfirmRenameFile()
    {
        if (_renamingFileItem == null || !IsSafeFileName(RenamingFileNewName)) return;
        if (!IsInActiveServer(_renamingFileItem.FullPath)) return;
        try
        {
            var newPath = Path.Combine(CurrentBrowsePath, RenamingFileNewName.Trim());
            if (!IsInActiveServer(newPath)) return;
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
            await RefreshFilesAsync();
        }
        catch (Exception ex)
        {
            FileOperationStatus = $"Rename failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task CreateNewFolder()
    {
        if (string.IsNullOrWhiteSpace(CurrentBrowsePath) || !IsInActiveServer(CurrentBrowsePath)) return;
        try {
            string baseName = "New Folder";
            string name = baseName;
            int counter = 1;
            while (Directory.Exists(Path.Combine(CurrentBrowsePath, name)) || File.Exists(Path.Combine(CurrentBrowsePath, name))) {
                name = $"{baseName} ({counter++})";
            }
            var di = Directory.CreateDirectory(Path.Combine(CurrentBrowsePath, name));
            await RefreshFilesAsync();
            var item = ServerFiles.FirstOrDefault(f => f.Name == name);
            if (item != null) StartRenameFile(item);
        } catch { }
    }

    [RelayCommand]
    public async Task CreateNewFile()
    {
        if (string.IsNullOrWhiteSpace(CurrentBrowsePath) || !IsInActiveServer(CurrentBrowsePath)) return;
        try {
            string baseName = "new_file";
            string ext = ".txt";
            string name = baseName + ext;
            int counter = 1;
            while (Directory.Exists(Path.Combine(CurrentBrowsePath, name)) || File.Exists(Path.Combine(CurrentBrowsePath, name))) {
                name = $"{baseName}_{counter++}{ext}";
            }
            File.WriteAllText(Path.Combine(CurrentBrowsePath, name), "");
            await RefreshFilesAsync();
            var item = ServerFiles.FirstOrDefault(f => f.Name == name);
            if (item != null) StartRenameFile(item);
        } catch { }
    }


    [RelayCommand]
    public async Task DeleteFileItem(ServerFileItem? item)
    {
        if (item == null || !IsInActiveServer(item.FullPath)) return;
        // Never allow deleting the server root itself.
        try
        {
            var rootFull = Path.GetFullPath(ActiveServerDirectory).TrimEnd('\\', '/');
            var targetFull = Path.GetFullPath(item.FullPath).TrimEnd('\\', '/');
            if (string.Equals(rootFull, targetFull, StringComparison.OrdinalIgnoreCase)) return;
            if (item.IsDirectory && Directory.Exists(item.FullPath))
            {
                await Task.Run(() => Directory.Delete(item.FullPath, true));
            }
            else if (File.Exists(item.FullPath))
            {
                File.Delete(item.FullPath);
            }
            await RefreshFilesAsync();
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
        if (!IsInActiveServer(CurrentBrowsePath)) return;

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
                    if (!IsInActiveServer(dest)) continue;
                    File.Copy(local, dest, true);
                }
            }
            await RefreshFilesAsync();
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

        if (_resetCountdownTimer == null)
        {
            _resetCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _resetCountdownTimer.Tick += OnResetCountdownTick;
        }
        else
        {
            _resetCountdownTimer.Stop();
        }
        _resetCountdownTimer.Start();
    }

    private void OnResetCountdownTick(object? sender, EventArgs e)
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

        // PlayersSubTab: 0=Online (read-only), 1=Ops, 2=Whitelist, 3=Bans.
        switch (PlayersSubTab)
        {
            case 1:
                if (!OpsList.Any(p => p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    OpsList.Add(new ServerPlayerEntry { Name = trimmed, Level = 4 });
                    ServerCreatorService.SavePlayerList(ActiveServerDirectory, "ops.json", OpsList);
                }
                break;
            case 2:
                if (!WhitelistList.Any(p => p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    WhitelistList.Add(new ServerPlayerEntry { Name = trimmed });
                    ServerCreatorService.SavePlayerList(ActiveServerDirectory, "whitelist.json", WhitelistList);
                }
                break;
            case 3:
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
            case 1:
                OpsList.Remove(entry);
                ServerCreatorService.SavePlayerList(ActiveServerDirectory, "ops.json", OpsList);
                break;
            case 2:
                WhitelistList.Remove(entry);
                ServerCreatorService.SavePlayerList(ActiveServerDirectory, "whitelist.json", WhitelistList);
                break;
            case 3:
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

    public async Task LoadServerIconAsync()
    {
        var dir = ActiveServerDirectory;
        if (string.IsNullOrWhiteSpace(dir)) return;
        byte[]? bytes = null;
        try
        {
            var iconPath = Path.Combine(dir, "server-icon.png");
            if (File.Exists(iconPath))
                bytes = await Task.Run(() => File.ReadAllBytes(iconPath));
        }
        catch { }
        ServerIconBitmap?.Dispose();
        ServerIconBitmap = null;
        HasServerIcon = false;
        if (bytes != null)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                ServerIconBitmap = new Bitmap(ms);
                HasServerIcon = true;
            }
            catch { }
        }
    }

    public void LoadServerIcon() => _ = LoadServerIconAsync();

    // --- Console Commands & Log Streaming (isolated per server) ---
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
        if (!string.IsNullOrWhiteSpace(_consoleOwner))
            _consoleBuffers[_consoleOwner] = "";
    }

    public void AppendConsoleLog(string line)
    {
        // Route live process output to the running server's buffer when the
        // user is looking at a different server.
        if (IsServerRunning && !string.IsNullOrWhiteSpace(_runningServerName) &&
            !string.Equals(_runningServerName, SelectedServerName, StringComparison.OrdinalIgnoreCase))
        {
            var key = _runningServerName;
            _consoleBuffers.TryGetValue(key, out var existing);
            var next = string.IsNullOrEmpty(existing) ? line : existing + "\n" + line;
            if (next.Length > 200000)
                next = next.Substring(next.Length - 100000);
            _consoleBuffers[key] = next;
            return;
        }

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
            if (!string.IsNullOrWhiteSpace(_consoleOwner))
                _consoleBuffers[_consoleOwner] = ConsoleLogText;
            ConsoleUpdated?.Invoke();
        });
    }

    public async Task LoadLatestLogAsync()
    {
        var dir = ActiveServerDirectory;
        if (string.IsNullOrWhiteSpace(dir)) return;
        var logFile = Path.Combine(dir, "logs", "latest.log");
        if (File.Exists(logFile))
        {
            try
            {
                string text;
                using (var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    text = await sr.ReadToEndAsync();
                }
                if (text.Length > 150000)
                    text = text.Substring(text.Length - 150000);
                ConsoleLogText = text.Replace("\r\n", "\n");
                if (!string.IsNullOrWhiteSpace(_consoleOwner))
                    _consoleBuffers[_consoleOwner] = ConsoleLogText;
                ConsoleUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Failed to read latest.log: {ex.Message}");
            }
        }
        else if (!IsServerRunning)
        {
            ConsoleLogText = L10n.T("cs_console_empty");
            ConsoleUpdated?.Invoke();
        }
    }

    public void LoadLatestLog() => _ = LoadLatestLogAsync();

    // --- World Management ---
    public async Task RefreshWorldInfoAsync()
    {
        var dir = ActiveServerDirectory;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

        WorldNameDisplay = !string.IsNullOrWhiteSpace(PropsLevelName) ? PropsLevelName : "world";
        WorldDifficulty = PropsDifficulty;
        WorldHardcore = PropsHardcore;
        WorldGenerateStructures = PropsGenerateStructures;

        var worldDir = Path.Combine(dir, WorldNameDisplay);
        if (!Directory.Exists(worldDir))
        {
            WorldIconBitmap?.Dispose();
            WorldIconBitmap = null;
            HasWorldIcon = false;
            WorldSizeDisplay = "Not generated yet";
            EnabledDatapacks.Clear();
            DisabledDatapacks.Clear();
            return;
        }

        // Heavy disk work (recursive size + datapack scan) off the UI thread.
        // The world icon is the real icon.png from the world save folder.
        var snapshot = await Task.Run(() =>
        {
            byte[]? iconBytes = null;
            try
            {
                var iconPath = Path.Combine(worldDir, "icon.png");
                if (File.Exists(iconPath))
                    iconBytes = File.ReadAllBytes(iconPath);
            }
            catch { }

            string sizeDisplay;
            try
            {
                long totalBytes = Directory.GetFiles(worldDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                sizeDisplay = totalBytes > 1024L * 1024 * 1024
                    ? $"{(totalBytes / (1024.0 * 1024 * 1024)):F2} GB"
                    : $"{(totalBytes / (1024.0 * 1024)):F1} MB";
            }
            catch { sizeDisplay = "Unknown"; }

            var enabled = new List<string>();
            var disabled = new List<string>();
            try
            {
                var dpDir = Path.Combine(worldDir, "datapacks");
                if (Directory.Exists(dpDir))
                {
                    foreach (var file in Directory.GetFiles(dpDir, "*.zip"))
                    {
                        if (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                            disabled.Add(Path.GetFileName(file));
                        else
                            enabled.Add(Path.GetFileName(file));
                    }
                    foreach (var d in Directory.GetDirectories(dpDir))
                    {
                        if (d.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                            disabled.Add(Path.GetFileName(d));
                        else
                            enabled.Add(Path.GetFileName(d));
                    }
                }
            }
            catch { }
            return (iconBytes, sizeDisplay, enabled, disabled);
        });

        WorldIconBitmap?.Dispose();
        WorldIconBitmap = null;
        HasWorldIcon = false;
        if (snapshot.iconBytes != null)
        {
            try
            {
                using var ms = new MemoryStream(snapshot.iconBytes);
                WorldIconBitmap = new Bitmap(ms);
                HasWorldIcon = true;
            }
            catch { }
        }
        WorldSizeDisplay = snapshot.sizeDisplay;
        EnabledDatapacks.Clear();
        DisabledDatapacks.Clear();
        foreach (var e in snapshot.enabled) EnabledDatapacks.Add(e);
        foreach (var d in snapshot.disabled) DisabledDatapacks.Add(d);
    }

    public void RefreshWorldInfo() => _ = RefreshWorldInfoAsync();

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
                await RefreshWorldInfoAsync();
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
        // world-name / world-type / world-seed are not edited here by design:
        // only live world rules are persisted.
        PropsDifficulty = WorldDifficulty;
        PropsHardcore = WorldHardcore;
        PropsGenerateStructures = WorldGenerateStructures;

        if (!string.IsNullOrWhiteSpace(ActiveServerDirectory) && Directory.Exists(ActiveServerDirectory))
        {
            var dict = new Dictionary<string, string>
            {
                ["difficulty"] = PropsDifficulty,
                ["hardcore"] = PropsHardcore ? "true" : "false",
                ["generate-structures"] = PropsGenerateStructures ? "true" : "false"
            };
            ServerCreatorService.SaveProperties(ActiveServerDirectory, dict);
        }

        WorldStatusMessage = "World settings saved successfully!";
        IsWorldSettingsOpen = false;
    }

}
