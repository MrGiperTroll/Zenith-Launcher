using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class CreateServerViewModel : ObservableObject
{
    private readonly ServerCreatorService _serverService = new();
    private readonly MainWindowViewModel _host;
    private CancellationTokenSource? _cts;

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

    // server.properties Visual Editor Fields
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

    public bool IsOverviewTab => SelectedTab == 0;
    public bool IsPropertiesTab => SelectedTab == 1;
    public bool IsPluginsTab => SelectedTab == 2;
    public bool IsWorldTab => SelectedTab == 3;
    public bool IsPlayersTab => SelectedTab == 4;

    public bool IsOpsSubTab => PlayersSubTab == 0;
    public bool IsWhitelistSubTab => PlayersSubTab == 1;
    public bool IsBansSubTab => PlayersSubTab == 2;

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewTab));
        OnPropertyChanged(nameof(IsPropertiesTab));
        OnPropertyChanged(nameof(IsPluginsTab));
        OnPropertyChanged(nameof(IsWorldTab));
        OnPropertyChanged(nameof(IsPlayersTab));
    }

    partial void OnPlayersSubTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsOpsSubTab));
        OnPropertyChanged(nameof(IsWhitelistSubTab));
        OnPropertyChanged(nameof(IsBansSubTab));
    }

    public bool CanCreate => !IsBusy && !string.IsNullOrWhiteSpace(ServerName) && !string.IsNullOrWhiteSpace(SelectedVersion);

    public CreateServerViewModel(IInstanceService instanceService, MainWindowViewModel host)
    {
        _host = host;
        _selectedSoftware = AvailableSoftware[0];
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
    }

    [RelayCommand]
    public void StartCreateNewServer()
    {
        IsCreatingNewServer = true;
        ServerName = $"Server {ExistingServers.Count + 1}";
        IsCreated = false;
        StatusText = "";
    }

    [RelayCommand]
    public void CancelCreateNewServer()
    {
        if (ExistingServers.Count > 0)
        {
            IsCreatingNewServer = false;
            if (string.IsNullOrWhiteSpace(SelectedServerName))
                SelectedServerName = ExistingServers[0];
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
    }

    public void LoadServerDetails()
    {
        if (string.IsNullOrWhiteSpace(ActiveServerDirectory) || !Directory.Exists(ActiveServerDirectory))
            return;

        // Load server.properties
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
            ["level-type"] = PropsLevelType
        };

        ServerCreatorService.SaveProperties(ActiveServerDirectory, dict);
        ConfigStatusMessage = "Settings saved to server.properties!";
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
                // Launch detached external CMD window with interactive command prompt & live output
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"Minecraft Server - {title}\" cmd.exe /k \"title Minecraft Server - {title} && cd /d \"\"{dir}\"\" && run.bat\"",
                    WorkingDirectory = dir,
                    UseShellExecute = true
                });
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
