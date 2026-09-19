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
    private DateTime _serverStartTime;
    private readonly DispatcherTimer _uptimeTimer;

    // Creation Wizard Fields
    [ObservableProperty] private string _serverName = "My Local Server";
    [ObservableProperty] private string _selectedVersion = "1.21.4";
    [ObservableProperty] private ServerSoftwareOption _selectedSoftware;
    [ObservableProperty] private int _ramGb = 4;
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
    [ObservableProperty] private int _selectedTab; // 0=Overview, 1=Properties, 2=Plugins, 3=World, 4=Players
    [ObservableProperty] private int _playersSubTab; // 0=Ops, 1=Whitelist, 2=Bans
    [ObservableProperty] private string _newPlayerName = "";
    [ObservableProperty] private string _configStatusMessage = "";
    [ObservableProperty] private bool _showWorldResetConfirm;
    [ObservableProperty] private string _worldResetStatusMessage = "";
    [ObservableProperty] private bool _showDeleteServerConfirm;

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
    [ObservableProperty] private string _serverSoftwareDisplay = "Vanilla";
    [ObservableProperty] private string _serverAllocatedRamDisplay = "4 GB";
    [ObservableProperty] private string _uptimeText = "00:00:00";
    [ObservableProperty] private bool _isEditingServerName;
    [ObservableProperty] private string _editingServerNameText = "";
    [ObservableProperty] private string _serverIpDisplay = "localhost:25565";
    [ObservableProperty] private bool _isIpCopied;
    public bool CanEditServer => !IsServerRunning;
    partial void OnIsServerRunningChanged(bool value) => OnPropertyChanged(nameof(CanEditServer));

    public ObservableCollection<ServerNetworkEndpoint> NetworkEndpoints { get; } = new();

    public event Action? StateChanged;

    public ObservableCollection<string> AvailableVersions { get; } = new();
    public IReadOnlyList<ServerSoftwareOption> AvailableSoftware => ServerCreatorService.AvailableSoftware;
    public int[] RamOptions { get; } = { 2, 3, 4, 6, 8, 12, 16, 24, 32 };

    public string[] GamemodeOptions { get; } = { "survival", "creative", "adventure", "spectator" };
    public string[] DifficultyOptions { get; } = { "peaceful", "easy", "normal", "hard" };
    public string[] LevelTypeOptions { get; } = { "minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified" };

    public ObservableCollection<string> ExistingServers { get; } = new();
    public ObservableCollection<ServerPluginItem> InstalledPlugins { get; } = new();
    public ObservableCollection<ServerPlayerEntry> OpsList { get; } = new();
    public ObservableCollection<ServerPlayerEntry> WhitelistList { get; } = new();
    public ObservableCollection<ServerPlayerEntry> BansList { get; } = new();
    public ObservableCollection<string> OnlinePlayers { get; } = new();

    [ObservableProperty] private bool _isRefreshingOnlinePlayers;
    [ObservableProperty] private string _onlinePlayersCountText = "0 players online";

    public bool IsOverviewTab => SelectedTab == 0;
    public bool IsPropertiesTab => SelectedTab == 1;
    public bool IsPluginsTab => SelectedTab == 2;
    public bool IsWorldTab => SelectedTab == 3;
    public bool IsPlayersTab => SelectedTab == 4;

    public bool IsOnlinePlayersSubTab => PlayersSubTab == 0;
    public bool IsOpsSubTab => PlayersSubTab == 1;
    public bool IsWhitelistSubTab => PlayersSubTab == 2;
    public bool IsBansSubTab => PlayersSubTab == 3;

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewTab));
        OnPropertyChanged(nameof(IsPropertiesTab));
        OnPropertyChanged(nameof(IsPluginsTab));
        OnPropertyChanged(nameof(IsWorldTab));
        OnPropertyChanged(nameof(IsPlayersTab));
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
        _selectedSoftware = AvailableSoftware[0];
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

    [RelayCommand]
    public void StartCreateNewServer()
    {
        IsCreatingNewServer = true;
        ServerName = $"Server {ExistingServers.Count + 1}";
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

        ServerCreatorService.GetServerMetadata(ActiveServerDirectory, out var soft, out var ram);
        ServerSoftwareDisplay = soft;
        ServerAllocatedRamDisplay = ram;
        RefreshNetworkEndpoints();

        _ = RefreshOnlinePlayersAsync();

        ConfigStatusMessage = "";
        WorldResetStatusMessage = "";
        ShowWorldResetConfirm = false;
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
    public void StopServer()
    {
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try
            {
                _serverProcess.Kill(true);
            }
            catch { }
        }
        IsServerRunning = false;
        _uptimeTimer.Stop();
        UptimeText = "00:00:00";
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
            var versions = await _serverService.GetPopularReleaseVersionsAsync();
            AvailableVersions.Clear();
            foreach (var v in versions)
                AvailableVersions.Add(v);

            if (AvailableVersions.Count > 0)
            {
                SelectedVersion = AvailableVersions[0];
            }
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
    partial void OnSelectedVersionChanged(string value) => OnPropertyChanged(nameof(CanCreate));
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
                RamGb,
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
            var batPath = Path.Combine(dir, "run.bat");
            if (File.Exists(batPath))
            {
                var title = SelectedServerName ?? Path.GetFileName(dir);
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/k title Minecraft Server - {title} && run.bat",
                        WorkingDirectory = dir,
                        UseShellExecute = true
                    },
                    EnableRaisingEvents = true
                };

                p.Exited += (s, e) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        IsServerRunning = false;
                        _uptimeTimer.Stop();
                        UptimeText = "00:00:00";
                    });
                };

                if (p.Start())
                {
                    _serverProcess = p;
                    _serverStartTime = DateTime.Now;
                    IsServerRunning = true;
                    _uptimeTimer.Start();
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to run server in cmd", ex);
        }
    }

    // Plugins & Mods Management
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

    // World Management
    [RelayCommand]
    public void RequestResetWorld()
    {
        ShowWorldResetConfirm = true;
        WorldResetStatusMessage = "";
    }

    [RelayCommand]
    public void CancelResetWorld()
    {
        ShowWorldResetConfirm = false;
    }

    [RelayCommand]
    public void ConfirmResetWorld()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory)) return;

        try
        {
            ServerCreatorService.ResetWorld(ActiveServerDirectory, PropsLevelName);
            ShowWorldResetConfirm = false;
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
