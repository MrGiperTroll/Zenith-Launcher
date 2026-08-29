using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class EditInstanceViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _host;
    private readonly IInstanceService _instanceService;
    private readonly DispatcherTimer _logTimer;
    private readonly LauncherConfigData _globalConfig;

    public InstanceModel Instance { get; }

    public EditInstanceViewModel(InstanceModel instance, MainWindowViewModel host, IInstanceService instanceService)
    {
        Instance = instance;
        _host = host;
        _instanceService = instanceService;
        _globalConfig = host.GetLauncherConfigSnapshot() ?? new LauncherConfigData();

        MaxRamMb = _host.MaxRamMb > 0 ? _host.MaxRamMb : 16384;

        SelectedJavaMode = instance.JavaMode ?? _globalConfig.JavaMode ?? "Recommended";
        CustomJavaPath = instance.CustomJavaPath ?? _globalConfig.CustomJavaPath ?? "";
        JvmArgs = instance.JvmArgs ?? _globalConfig.JvmArgs ?? "";
        RamMb = instance.RamMb ?? EffectiveGlobalRamMb(_globalConfig);
        GameWidth = instance.GameWidth ?? (_globalConfig.GameWidth > 0 ? _globalConfig.GameWidth : 1280);
        GameHeight = instance.GameHeight ?? (_globalConfig.GameHeight > 0 ? _globalConfig.GameHeight : 720);
        IsFullscreen = instance.IsFullscreen ?? _globalConfig.IsFullscreen;

        LogsFollowOutput = instance.LogsFollowOutput;
        LogsWrapLines = instance.LogsWrapLines;
        LogsShowSystemMessages = instance.LogsShowSystemMessages;
        LogsFullJava = instance.LogsFullJava;

        UpdateJavaDisplay();

        // Per-instance override tracking. Flags are recomputed AFTER the initial value
        // assignments: assigning an inherited (global) value must not mark the block as
        // customized, otherwise opening the editor would persist overrides for everything.
        _javaDirty = !string.IsNullOrEmpty(instance.JavaMode) || !string.IsNullOrEmpty(instance.CustomJavaPath);
        _jvmArgsDirty = !string.IsNullOrEmpty(instance.JvmArgs);
        _memoryDirty = instance.RamMb.HasValue;
        _windowDirty = instance.GameWidth.HasValue || instance.GameHeight.HasValue || instance.IsFullscreen.HasValue;

        ModList = new FileListViewModel("Mods", Path.Combine(instance.Path, "mods"), true,
            L10n.T("fl_empty_mods"), ReportError, new[] { ".jar" }, kind: InstanceFileKind.Mod, instancePath: instance.Path);
        ResourcePackList = new FileListViewModel("Resource Packs", Path.Combine(instance.Path, "resourcepacks"), true,
            L10n.T("fl_empty_resourcepacks"), ReportError, new[] { ".zip", ".rar", ".7z" }, kind: InstanceFileKind.ResourcePack, instancePath: instance.Path);
        ShaderList = new FileListViewModel("Shader Packs", Path.Combine(instance.Path, "shaderpacks"), true,
            L10n.T("fl_empty_shaders"), ReportError, new[] { ".zip", ".rar", ".7z" }, kind: InstanceFileKind.ShaderPack, instancePath: instance.Path);
        WorldList = new FileListViewModel("Worlds", Path.Combine(instance.Path, "saves"), false,
            L10n.T("fl_empty_worlds"), ReportError, new[] { ".zip" }, worldTarget: true, kind: InstanceFileKind.World, instancePath: instance.Path);
        ScreenshotList = new FileListViewModel("Screenshots", Path.Combine(instance.Path, "screenshots"), false,
            L10n.T("fl_empty_screenshots"), ReportError, new[] { ".png", ".jpg", ".jpeg" }, kind: InstanceFileKind.Screenshot, instancePath: instance.Path);

        ModList.OpenProjectDetails = OnOpenProjectDetails;
        ResourcePackList.OpenProjectDetails = OnOpenProjectDetails;
        ShaderList.OpenProjectDetails = OnOpenProjectDetails;

        HasModsSupport = IsLoaderWithModsSupport(instance.LoaderType);
        HasShaderSupport = HasShaderSupportFor(instance.LoaderType);
        RefreshAvailableWorlds();
        RefreshDataPacks();

        _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _logTimer.Tick += (_, _) => RefreshLogs();
        _logTimer.Start();

        // Mirror launch/download progress from the host (the actual launch runs on MainWindow's service)
        _host.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainWindowViewModel.LaunchProgress):
                    OnPropertyChanged(nameof(LaunchProgress));
                    OnPropertyChanged(nameof(IsDownloading));
                    break;
                case nameof(MainWindowViewModel.CurrentDownloadFile):
                    OnPropertyChanged(nameof(CurrentDownloadFile));
                    OnPropertyChanged(nameof(DownloadStatus));
                    break;
                case nameof(MainWindowViewModel.IsLaunching):
                    OnPropertyChanged(nameof(IsLaunching));
                    OnPropertyChanged(nameof(IsDownloading));
                    break;
            }
        };

        RefreshFileLists();
        RefreshLogs();
    }

    public void RefreshAllContent() => RefreshFileLists();

    public bool IsRunning => Instance.IsRunning || Instance.IsLaunching;

    public bool CanLaunch => !IsRunning;

    // ----- Tabs -----
    [ObservableProperty]
    private string _selectedTab = "Logs";

    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsLogsTab));
        OnPropertyChanged(nameof(IsModsTab));
        OnPropertyChanged(nameof(IsDataPacksTab));
        OnPropertyChanged(nameof(IsResourcePacksTab));
        OnPropertyChanged(nameof(IsShaderPacksTab));
        OnPropertyChanged(nameof(IsWorldsTab));
        OnPropertyChanged(nameof(IsServersTab));
        OnPropertyChanged(nameof(IsScreenshotsTab));
        OnPropertyChanged(nameof(IsSettingsTab));
        // Only refresh what the newly visible tab shows — rescanning every folder
        // on each switch was the source of the noticeable tab-switch lag.
        RefreshTabContent(value);
    }

    private void RefreshTabContent(string? tab)
    {
        switch (tab)
        {
            case "Mods":
                if (HasModsSupport) _ = ModList.RefreshAsync();
                break;
            case "Resource Packs":
                _ = ResourcePackList.RefreshAsync();
                break;
            case "Shader Packs":
                _ = ShaderList.RefreshAsync();
                break;
            case "Worlds":
                _ = WorldList.RefreshAsync();
                break;
            case "Screenshots":
                _ = ScreenshotList.RefreshAsync();
                break;
            case "Data Packs":
                RefreshAvailableWorlds();
                RefreshDataPacks();
                break;
            case "Servers":
                RefreshServers();
                break;
        }
    }

    public bool HasModsSupport { get; }
    public bool HasShaderSupport { get; }

    private static bool IsLoaderWithModsSupport(string loader)
    {
        var l = loader?.Trim().ToLowerInvariant() ?? "";
        return l is "fabric" or "forge" or "neoforge" or "quilt";
    }

    private static bool HasShaderSupportFor(string loader)
    {
        var l = loader?.Trim() ?? "";
        if (l.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)) return false;
        // OptiFine, Fabric, Forge, NeoForge, Quilt all support shaders via Iris/OptiFine
        return true;
    }

    public bool IsLogsTab => SelectedTab == "Logs";
    public bool IsModsTab => SelectedTab == "Mods" && HasModsSupport;
    public bool IsDataPacksTab => SelectedTab == "Data Packs";
    public bool IsResourcePacksTab => SelectedTab == "Resource Packs";
    public bool IsShaderPacksTab => SelectedTab == "Shader Packs" && HasShaderSupport;
    public bool IsWorldsTab => SelectedTab == "Worlds";
    public bool IsServersTab => SelectedTab == "Servers";
    public bool IsScreenshotsTab => SelectedTab == "Screenshots";
    public bool IsSettingsTab => SelectedTab == "Settings";

    [RelayCommand]
    private void SelectTab(string tab)
    {
        if (tab == "Mods" && !HasModsSupport) return;
        if (tab == "Shader Packs" && !HasShaderSupport) return;
        SelectedTab = tab;
    }

    [RelayCommand]
    private void OpenContentBrowser(string? contentType)
    {
        try
        {
            var type = contentType switch
            {
                "Mods" => ContentType.Mod,
                "Resource Packs" => ContentType.ResourcePack,
                "Shader Packs" => ContentType.Shader,
                "Data Packs" => ContentType.DataPack,
                _ => ContentType.Mod
            };
            if (type == ContentType.Mod && !HasModsSupport)
                type = ContentType.ResourcePack;

            var vm = new ContentBrowserViewModel(
                Instance.Version,
                Instance.LoaderType,
                Instance.Path,
                msg => ReportError(msg),
                () => RefreshFileLists(),
                () => HasModsSupport,
                type);

            // Pass selected world for datapacks
            if (!string.IsNullOrWhiteSpace(SelectedWorld))
                vm.SelectedWorld = SelectedWorld;

            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var win = new Views.ContentBrowserWindow { DataContext = vm };
                win.Closed += (_, _) =>
                {
                    RefreshFileLists();
                    Services.DiscordPresenceService.SetEditingInstance(Instance.Name);
                };
                switch (type)
                {
                    case ContentType.Mod:
                        Services.DiscordPresenceService.SetBrowsingMods();
                        break;
                    case ContentType.Shader:
                        Services.DiscordPresenceService.SetBrowsingShaders();
                        break;
                }
                win.ShowDialog(desktop.MainWindow);
            }
        }
        catch (Exception ex)
        {
            ReportError($"Failed to open browser: {ex.Message}");
        }
    }

    private void OnOpenProjectDetails(string projectId, InstanceFileKind kind)
    {
        try
        {
            var type = kind switch
            {
                InstanceFileKind.Mod => ContentType.Mod,
                InstanceFileKind.ResourcePack => ContentType.ResourcePack,
                InstanceFileKind.ShaderPack => ContentType.Shader,
                InstanceFileKind.DataPack => ContentType.DataPack,
                _ => ContentType.Mod
            };
            if (type == ContentType.Mod && !HasModsSupport)
                type = ContentType.ResourcePack;

            var vm = new ContentBrowserViewModel(
                Instance.Version,
                Instance.LoaderType,
                Instance.Path,
                msg => ReportError(msg),
                () => RefreshFileLists(),
                () => HasModsSupport,
                type);

            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var win = new Views.ContentBrowserWindow { DataContext = vm };
                win.Closed += (_, _) =>
                {
                    RefreshFileLists();
                    Services.DiscordPresenceService.SetEditingInstance(Instance.Name);
                };
                win.Show(desktop.MainWindow);
                if (!string.IsNullOrWhiteSpace(projectId))
                    _ = vm.OpenProjectByIdAsync(projectId);
            }
        }
        catch (Exception ex)
        {
            ReportError($"Failed to open project details: {ex.Message}");
        }
    }

    // ----- Logs -----
    [ObservableProperty]
    private string _logText = "No logs yet. Launch the instance to see output here.";

    [ObservableProperty]
    private bool _logHasContent;

    [ObservableProperty]
    private bool _logsFollowOutput = true;

    [ObservableProperty]
    private bool _logsWrapLines = true;

    [ObservableProperty]
    private bool _logsShowSystemMessages = true;

    [ObservableProperty]
    private bool _logsFullJava;

    partial void OnLogsFollowOutputChanged(bool value) => RefreshLogs();
    partial void OnLogsWrapLinesChanged(bool value) => OnPropertyChanged(nameof(LogTextWrapping));
    partial void OnLogsShowSystemMessagesChanged(bool value) => RefreshLogs();
    partial void OnLogsFullJavaChanged(bool value) => RefreshLogs();

    public Avalonia.Media.TextWrapping LogTextWrapping =>
        LogsWrapLines ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap;

    private string LogFilePath =>
        Path.Combine(ZenithPaths.AppDataDir, "logs", $"instance_{Instance.Id}.log");

    private void RefreshLogs()
    {
        try
        {
            var sb = new StringBuilder();

            if (File.Exists(LogFilePath))
            {
                var lines = File.ReadAllLines(LogFilePath);
                if (!LogsShowSystemMessages)
                    lines = lines.Where(l => !IsSystemMessage(l)).ToArray();
                if (lines.Length > 0)
                {
                    var tail = LogsFullJava
                        ? lines
                        : lines.Skip(Math.Max(0, lines.Length - 1000)).ToArray();
                    foreach (var line in tail)
                        sb.AppendLine(line);
                }
            }

            // Detailed Java logs written by the game itself (latest.log / debug.log)
            if (LogsFullJava)
                AppendGameJavaLogs(sb);

            var text = sb.ToString().TrimEnd('\r', '\n');
            LogText = text.Length == 0 ? "No logs yet. Launch the instance to see output here." : text;
            LogHasContent = text.Length > 0;
        }
        catch { }
    }

    private static bool IsSystemMessage(string line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        return line.StartsWith("--- ", StringComparison.Ordinal) ||
               line.StartsWith("[DEBUG]", StringComparison.Ordinal) ||
               line.StartsWith("[ERROR]", StringComparison.Ordinal) ||
               line.StartsWith("[FATAL]", StringComparison.Ordinal) ||
               line.StartsWith("[CRITICAL", StringComparison.Ordinal);
    }

    private void AppendGameJavaLogs(StringBuilder sb)
    {
        foreach (var fileName in new[] { "debug.log", "latest.log" })
        {
            var path = Path.Combine(Instance.Path, "logs", fileName);
            if (!File.Exists(path)) continue;
            var allLines = File.ReadAllLines(path);
            if (allLines.Length == 0) continue;

            sb.AppendLine();
            sb.AppendLine($"----- {fileName} ({allLines.Length} lines) -----");
            var tail = allLines.Skip(Math.Max(0, allLines.Length - 5000));
            foreach (var line in tail)
                sb.AppendLine(line);
        }
    }

    [RelayCommand]
    private async Task CopyLogsAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(window)?.Clipboard;
            if (clipboard != null)
            {
                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.CreateText(LogText));
                await clipboard.SetDataAsync(transfer);
            }
        }
    }

    // ----- File lists (unified content management) -----
    public FileListViewModel ModList { get; }
    public FileListViewModel ResourcePackList { get; }
    public FileListViewModel ShaderList { get; }
    public FileListViewModel WorldList { get; }
    public FileListViewModel ScreenshotList { get; }
    public ObservableCollection<InstanceFileEntry> Servers { get; } = new();

    // Data Packs (per-world)
    public ObservableCollection<string> AvailableWorlds { get; } = new();
    public ObservableCollection<InstanceFileEntry> DataPacks { get; } = new();

    [ObservableProperty]
    private string _selectedWorld = "";

    partial void OnSelectedWorldChanged(string value) => RefreshDataPacks();

    // ----- Launch / download progress (mirrored from MainWindow host) -----
    public double LaunchProgress => _host.LaunchProgress;
    public string CurrentDownloadFile => _host.CurrentDownloadFile;
    public bool IsLaunching => _host.IsLaunching;
    public bool IsDownloading => _host.IsDownloading;
    public string DownloadStatus =>
        string.IsNullOrEmpty(CurrentDownloadFile) ? "Launching..." : $"Downloading: {CurrentDownloadFile}";

    private void ReportError(string message) => _host.StatusText = message;

    private void RefreshFileLists()
    {
        // All folder scans run off the UI thread (see FileListViewModel.RefreshAsync).
        if (HasModsSupport) _ = ModList.RefreshAsync();
        _ = ResourcePackList.RefreshAsync();
        _ = ShaderList.RefreshAsync();
        _ = WorldList.RefreshAsync();
        _ = ScreenshotList.RefreshAsync();
        RefreshAvailableWorlds();
        RefreshDataPacks();
        RefreshServers();
    }

    private void RefreshAvailableWorlds()
    {
        var saves = Path.Combine(Instance.Path, "saves");
        var worlds = new List<string>();
        if (Directory.Exists(saves))
        {
            foreach (var dir in Directory.GetDirectories(saves))
            {
                if (File.Exists(Path.Combine(dir, "level.dat")))
                    worlds.Add(Path.GetFileName(dir));
            }
        }
        var prev = SelectedWorld;
        AvailableWorlds.Clear();
        foreach (var w in worlds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            AvailableWorlds.Add(w);
        if (!string.IsNullOrWhiteSpace(prev) && AvailableWorlds.Contains(prev))
            SelectedWorld = prev;
        else if (AvailableWorlds.Count > 0 && string.IsNullOrWhiteSpace(SelectedWorld))
            SelectedWorld = AvailableWorlds[0];
        else if (AvailableWorlds.Count == 0)
            SelectedWorld = "";
    }

    private void RefreshDataPacks()
    {
        DataPacks.Clear();
        if (string.IsNullOrWhiteSpace(SelectedWorld)) return;
        var dpFolder = Path.Combine(Instance.Path, "saves", SelectedWorld, "datapacks");
        if (!Directory.Exists(dpFolder)) return;
        try
        {
            foreach (var f in Directory.GetFiles(dpFolder))
            {
                var ext = Path.GetExtension(f);
                var isDisabled = ext.Equals(".disabled", StringComparison.OrdinalIgnoreCase);
                var effectiveExt = Path.GetExtension(isDisabled ? f[..^9] : f);
                if (!effectiveExt.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                var entry = new InstanceFileEntry
                {
                    Name = Path.GetFileName(f),
                    FullPath = f,
                    IsDirectory = false,
                    IsEnabled = !isDisabled,
                    Kind = InstanceFileKind.DataPack,
                    Title = Path.GetFileNameWithoutExtension(isDisabled ? f[..^9] : f),
                    ModifiedAt = File.GetLastWriteTime(f)
                };
                try { ContentMetadataReader.Enrich(entry); } catch { }
                DataPacks.Add(entry);
            }
            foreach (var d in Directory.GetDirectories(dpFolder))
            {
                var entry = new InstanceFileEntry
                {
                    Name = Path.GetFileName(d),
                    FullPath = d,
                    IsDirectory = true,
                    IsEnabled = true,
                    Kind = InstanceFileKind.DataPack,
                    Title = Path.GetFileName(d),
                    ModifiedAt = Directory.GetLastWriteTime(d)
                };
                try { ContentMetadataReader.Enrich(entry); } catch { }
                DataPacks.Add(entry);
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to read datapacks", ex);
        }
    }

    private void RefreshServers()
    {
        Servers.Clear();
        var file = Path.Combine(Instance.Path, "servers.dat");
        if (!File.Exists(file)) return;

        try
        {
            foreach (var s in ServersDatService.Read(file))
            {
                var entry = new InstanceFileEntry
                {
                    Name = string.IsNullOrWhiteSpace(s.Name) ? s.Ip : s.Name,
                    FullPath = file,
                    IsDirectory = false,
                    IsEnabled = true,
                    Kind = InstanceFileKind.Server,
                    Title = string.IsNullOrWhiteSpace(s.Name) ? s.Ip : s.Name,
                    Subtitle = s.Ip,
                    Meta = s.HideAddress ? "hidden address" : (s.AcceptTextures ? "accepts resource packs" : ""),
                    Description = s.Motd
                };
                if (!string.IsNullOrWhiteSpace(s.Ip)) entry.Subtitle = s.Ip;
                entry.SetPreview(s.IconBytes, 64);
                Servers.Add(entry);
                PingServerEntry(entry, s.Ip);
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to read servers.dat", ex);
        }
    }

    /// <summary>Queries the server for its real name / MOTD / player count and patches the card asynchronously.</summary>
    private void PingServerEntry(InstanceFileEntry entry, string host)
    {
        _ = Task.Run(async () =>
        {
            var result = await MinecraftServerPinger.PingAsync(host);
            if (result == null) return;

            var lines = (result.Motd ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var title = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));

            void Apply()
            {
                if (!string.IsNullOrWhiteSpace(title)) entry.Title = title;
                if (lines.Length > 1) entry.Description = string.Join('\n', lines.Skip(1));
                if (result.Online > 0 || result.Max > 0)
                    entry.Meta = $"{result.Online}/{result.Max} online";
                if (!entry.HasPreview && result.IconBytes is { Length: > 0 })
                    entry.SetPreview(result.IconBytes, 64);
            }

            try { Dispatcher.UIThread.Post(Apply); }
            catch { Apply(); }
        });
    }

    [RelayCommand]
    private void OpenInstanceFolder()
    {
        try
        {
            Directory.CreateDirectory(Instance.Path);
            Process.Start(new ProcessStartInfo { FileName = Instance.Path, UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex)
        {
            ReportError($"Failed to open instance folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenServerManager()
    {
        var path = Path.Combine(Instance.Path, "servers.dat");
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var vm = new ServerManagerViewModel(path);
            var win = new Views.ServerManagerWindow { DataContext = vm };
            win.Closed += (_, _) => RefreshServers();
            win.ShowDialog(desktop.MainWindow);
        }
    }

    [RelayCommand]
    private void OpenDataPackFolder()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SelectedWorld))
            {
                ReportError("Select a world first.");
                return;
            }
            var folder = Path.Combine(Instance.Path, "saves", SelectedWorld, "datapacks");
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex)
        {
            ReportError($"Failed to open datapacks folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteDataPack(InstanceFileEntry entry)
    {
        if (entry == null) return;
        try
        {
            if (Directory.Exists(entry.FullPath)) Directory.Delete(entry.FullPath, true);
            else if (File.Exists(entry.FullPath)) File.Delete(entry.FullPath);
        }
        catch (Exception ex)
        {
            ReportError($"Failed to delete {entry.Name}: {ex.Message}");
        }
        RefreshDataPacks();
    }

    [RelayCommand]
    private void OpenDataPack(InstanceFileEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.FullPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = entry.FullPath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ReportError($"Failed to open {entry.Name}: {ex.Message}");
        }
    }

    public void ShowDataPackDetails(InstanceFileEntry entry)
    {
        if (entry == null) return;
        OnOpenProjectDetails(entry.ModrinthProjectId ?? "", InstanceFileKind.DataPack);
    }

    // ----- Settings -----
    public ObservableCollection<string> JavaModes { get; } = new() { "Recommended", "System", "Custom" };

    private bool _javaDirty;
    private bool _jvmArgsDirty;
    private bool _memoryDirty;
    private bool _windowDirty;

    public bool CanResetJava => _javaDirty;
    public bool IsJavaInherited => !_javaDirty;
    public bool CanResetJvmArgs => _jvmArgsDirty;
    public bool IsJvmArgsInherited => !_jvmArgsDirty;
    public bool CanResetMemory => _memoryDirty;
    public bool IsMemoryInherited => !_memoryDirty;
    public bool CanResetWindow => _windowDirty;
    public bool IsWindowInherited => !_windowDirty;

    private void MarkJavaDirty() { _javaDirty = true; NotifyResetState(nameof(CanResetJava), nameof(IsJavaInherited)); }
    private void MarkJvmArgsDirty() { _jvmArgsDirty = true; NotifyResetState(nameof(CanResetJvmArgs), nameof(IsJvmArgsInherited)); }
    private void MarkMemoryDirty() { _memoryDirty = true; NotifyResetState(nameof(CanResetMemory), nameof(IsMemoryInherited)); }
    private void MarkWindowDirty() { _windowDirty = true; NotifyResetState(nameof(CanResetWindow), nameof(IsWindowInherited)); }

    private void NotifyResetState(params string[] names)
    {
        foreach (var n in names) OnPropertyChanged(n);
    }

    [RelayCommand]
    private void ResetJava()
    {
        _javaDirty = false;
        SelectedJavaMode = _globalConfig.JavaMode ?? "Recommended";
        CustomJavaPath = _globalConfig.CustomJavaPath ?? "";
        NotifyResetState(nameof(CanResetJava), nameof(IsJavaInherited));
    }

    [RelayCommand]
    private void ResetJvmArgs()
    {
        _jvmArgsDirty = false;
        JvmArgs = _globalConfig.JvmArgs ?? "";
        NotifyResetState(nameof(CanResetJvmArgs), nameof(IsJvmArgsInherited));
    }

    [RelayCommand]
    private void ResetMemory()
    {
        _memoryDirty = false;
        RamMb = EffectiveGlobalRamMb(_globalConfig);
        NotifyResetState(nameof(CanResetMemory), nameof(IsMemoryInherited));
    }

    [RelayCommand]
    private void ResetWindow()
    {
        _windowDirty = false;
        GameWidth = _globalConfig.GameWidth > 0 ? _globalConfig.GameWidth : 1280;
        GameHeight = _globalConfig.GameHeight > 0 ? _globalConfig.GameHeight : 720;
        IsFullscreen = _globalConfig.IsFullscreen;
        NotifyResetState(nameof(CanResetWindow), nameof(IsWindowInherited));
    }

    [ObservableProperty]
    private string _selectedJavaMode = "Recommended";

    [ObservableProperty]
    private string _customJavaPath = "";

    [ObservableProperty]
    private string _jvmArgs = "";

    [ObservableProperty]
    private int _ramMb = 4096;

    [ObservableProperty]
    private int _gameWidth = 1280;

    [ObservableProperty]
    private int _gameHeight = 720;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private string _javaDisplayPath = "Recommended";

    partial void OnSelectedJavaModeChanged(string value)
    {
        UpdateJavaDisplay();
        MarkJavaDirty();
    }

    partial void OnCustomJavaPathChanged(string value)
    {
        UpdateJavaDisplay();
        MarkJavaDirty();
    }

    partial void OnJvmArgsChanged(string value) => MarkJvmArgsDirty();

    partial void OnRamMbChanged(int value) => MarkMemoryDirty();

    partial void OnGameWidthChanged(int value) => MarkWindowDirty();

    partial void OnGameHeightChanged(int value) => MarkWindowDirty();

    partial void OnIsFullscreenChanged(bool value) => MarkWindowDirty();

    private void UpdateJavaDisplay()
    {
        switch (SelectedJavaMode)
        {
            case "Custom":
                JavaDisplayPath = JavaVersionHelper.CustomDisplay(CustomJavaPath);
                break;

            case "System":
                var sys = JavaVersionHelper.FindSystemJavaPath();
                JavaDisplayPath = string.IsNullOrEmpty(sys) ? "(system PATH)" : sys;
                break;

            default: // Recommended - resolve asynchronously like a launch would
                JavaDisplayPath = JavaVersionHelper.RecommendedDisplay(Instance.Version);
                _ = ResolveJavaPathAsync();
                break;
        }
    }

    /// <summary>
    /// Replaces the version hint with the concrete javaw.exe path once the
    /// read-only lookup completes. A stale result after a mode switch (or an
    /// empty one when nothing is installed) keeps the current hint text.
    /// </summary>
    private async Task ResolveJavaPathAsync()
    {
        try
        {
            var resolved = await _host.ResolveJavaForEditorAsync(Instance.Version);
            if (SelectedJavaMode == "Recommended" && !string.IsNullOrEmpty(resolved))
                JavaDisplayPath = resolved;
        }
        catch { /* display-only lookup - keep the hint on failure */ }
    }

    private int EffectiveGlobalRamMb(LauncherConfigData config)
    {
        // The only RAM setting the launcher UI exposes is the global slider (RamMb),
        // so an instance without an override must inherit exactly that number.
        if (config.RamMb > 0) return config.RamMb;
        return Instance.Version.StartsWith("1.7") || Instance.Version.StartsWith("1.8") ? 2048 : 4096;
    }

    [ObservableProperty]
    private int _maxRamMb = 16384;

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
                Title = "Select Java Executable",
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
            }
        }
    }

    // ----- Bottom bar -----
    [RelayCommand]
    private async Task LaunchAsync()
    {
        SaveSettings();
        _host.LaunchSpecificInstanceCommand.Execute(Instance);
        await Task.CompletedTask;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CanLaunch));
    }

    [RelayCommand]
    private void Kill()
    {
        SaveSettings();
        _host.KillInstanceCommand.Execute(Instance);
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CanLaunch));
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        SaveSettings();
        _logTimer.Stop();
        _host.CloseEditInstanceWindow(this);
        await Task.CompletedTask;
    }

    private async void SaveSettings()
    {
        // Only store an override if the user explicitly enabled it for that block;
        // otherwise keep null so the launcher inherits the global setting.
        Instance.JavaMode = _javaDirty ? SelectedJavaMode : null;
        Instance.CustomJavaPath = _javaDirty && SelectedJavaMode == "Custom" && !string.IsNullOrWhiteSpace(CustomJavaPath)
            ? CustomJavaPath
            : null;
        Instance.JvmArgs = _jvmArgsDirty ? (string.IsNullOrWhiteSpace(JvmArgs) ? null : JvmArgs) : null;
        Instance.RamMb = _memoryDirty ? RamMb : null;
        Instance.GameWidth = _windowDirty ? GameWidth : null;
        Instance.GameHeight = _windowDirty ? GameHeight : null;
        Instance.IsFullscreen = _windowDirty ? IsFullscreen : null;

        Instance.LogsFollowOutput = LogsFollowOutput;
        Instance.LogsWrapLines = LogsWrapLines;
        Instance.LogsShowSystemMessages = LogsShowSystemMessages;
        Instance.LogsFullJava = LogsFullJava;

        await _instanceService.SaveInstanceAsync(Instance);
    }
}