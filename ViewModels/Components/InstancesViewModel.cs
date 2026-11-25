using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CmlLib.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Models;
using ZenithLauncher.Services;
using ZenithLauncher.ViewModels.Common;
using ZenithLauncher.ViewModels.Pages;

namespace ZenithLauncher.ViewModels.Components;

public partial class InstanceGroupSection : ObservableObject
{
    public string GroupKey { get; }
    public bool IsUngrouped { get; }
    public string HeaderTitle { get; }
    public ObservableCollection<InstanceModel> Items { get; }

    [ObservableProperty] private bool _isExpanded = true;

    public InstanceGroupSection(string groupKey, bool isUngrouped, string headerTitle, IEnumerable<InstanceModel> items)
    {
        GroupKey = groupKey;
        IsUngrouped = isUngrouped;
        HeaderTitle = headerTitle;
        Items = new ObservableCollection<InstanceModel>(items);
    }

    [RelayCommand]
    public void ToggleExpand() => IsExpanded = !IsExpanded;
}

public partial class InstancesViewModel : ViewModelBase
{
    private readonly IInstanceService _instanceService;
    private readonly ModLoaderService _modLoaderService;
    private readonly Func<LaunchViewModel> _getLaunch;
    private readonly Func<NavigationViewModel> _getNavigation;
    private readonly Func<MainWindowViewModel> _getHost;
    private readonly List<CachedVersionEntry> _cachedVersions = new();

    private string? _preRenameName;
    private InstanceModel? _prevSelectedInstance;
    private int _loaderBuildsRequestId;

    public Action<string>? OnStatusChanged { get; set; }
    public Action? SaveLauncherConfigRequested { get; set; }
    public Action? RefreshStateRequested { get; set; }
    public Action? UpdateJavaDisplayRequested { get; set; }
    public Func<string>? GetCurseForgeApiKey { get; set; }
    public Action? CloseDialogsRequested { get; set; }

    [ObservableProperty] private bool _showReleases = true;
    [ObservableProperty] private bool _showSnapshots = false;
    [ObservableProperty] private bool _showBetas = false;
    [ObservableProperty] private bool _showAlphas = false;

    partial void OnShowReleasesChanged(bool value) => FilterVersions();
    partial void OnShowSnapshotsChanged(bool value) => FilterVersions();
    partial void OnShowBetasChanged(bool value) => FilterVersions();
    partial void OnShowAlphasChanged(bool value) => FilterVersions();

    [ObservableProperty] private bool _useFlatVersionList = false;
    partial void OnUseFlatVersionListChanged(bool value) => SaveLauncherConfigRequested?.Invoke();

    [ObservableProperty]
    private ObservableCollection<string> _directoryModes = new() { "Separate directory for each instance", "Family", "Do not use separate directories" };

    [ObservableProperty]
    private string _selectedDirectoryMode = "Separate directory for each instance";
    partial void OnSelectedDirectoryModeChanged(string value) => SaveLauncherConfigRequested?.Invoke();

    [ObservableProperty]
    private ObservableCollection<string> _javaModes = new() { "Recommended", "System", "Custom" };

    [ObservableProperty]
    private string _selectedJavaMode = "Recommended";

    public ObservableCollection<InstanceModel> Instances { get; } = new();
    public ObservableCollection<InstanceModel> FilteredInstances { get; } = new();
    public ObservableCollection<InstanceGroupSection> GroupedInstances { get; } = new();
    public ObservableCollection<string> ProfileGroups { get; } = new();
    private readonly Dictionary<string, bool> _groupExpandedStates = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private string _selectedProfileGroup = "";
    [ObservableProperty] private bool _hasProfileGroups;

    public bool IsAllGroupsSelected => string.IsNullOrEmpty(SelectedProfileGroup);
    public string SelectedProfileGroupDisplay => IsAllGroupsSelected ? L10n.T("grp_all") : SelectedProfileGroup;

    partial void OnSelectedProfileGroupChanged(string value)
    {
        RebuildFilteredInstances();
        OnPropertyChanged(nameof(IsAllGroupsSelected));
        OnPropertyChanged(nameof(SelectedProfileGroupDisplay));
    }

    [ObservableProperty] private bool _isGroupDialogOpen;
    [ObservableProperty] private InstanceModel? _pendingGroupInstance;
    [ObservableProperty] private string _editingGroupName = "";

    public void RefreshGroupsAndFilter()
    {
        var groups = Instances
            .Select(i => i.Group?.Trim() ?? "")
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!ProfileGroups.SequenceEqual(groups))
        {
            ProfileGroups.Clear();
            foreach (var g in groups)
                ProfileGroups.Add(g);
        }

        HasProfileGroups = ProfileGroups.Count > 0;
        if (!string.IsNullOrEmpty(SelectedProfileGroup) &&
            !ProfileGroups.Contains(SelectedProfileGroup, StringComparer.OrdinalIgnoreCase))
        {
            SelectedProfileGroup = "";
        }
        else
        {
            RebuildFilteredInstances();
        }
    }

    public void RebuildFilteredInstances()
    {
        FilteredInstances.Clear();
        foreach (var inst in Instances)
        {
            if (string.IsNullOrEmpty(SelectedProfileGroup) ||
                string.Equals(inst.Group?.Trim(), SelectedProfileGroup, StringComparison.OrdinalIgnoreCase))
            {
                FilteredInstances.Add(inst);
            }
        }

        foreach (var existing in GroupedInstances)
            _groupExpandedStates[existing.GroupKey] = existing.IsExpanded;

        GroupedInstances.Clear();
        if (!HasProfileGroups)
            return;

        if (string.IsNullOrEmpty(SelectedProfileGroup))
        {
            var ungrouped = FilteredInstances.Where(i => string.IsNullOrWhiteSpace(i.Group)).ToList();
            if (ungrouped.Count > 0)
            {
                var sec = new InstanceGroupSection("", true, L10n.T("grp_ungrouped"), ungrouped);
                if (_groupExpandedStates.TryGetValue("", out var exp))
                    sec.IsExpanded = exp;
                GroupedInstances.Add(sec);
            }

            foreach (var grp in ProfileGroups)
            {
                var items = FilteredInstances
                    .Where(i => string.Equals(i.Group?.Trim(), grp, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (items.Count == 0) continue;
                var sec = new InstanceGroupSection(grp, false, grp, items);
                if (_groupExpandedStates.TryGetValue(grp, out var exp))
                    sec.IsExpanded = exp;
                GroupedInstances.Add(sec);
            }
        }
        else
        {
            var items = FilteredInstances.ToList();
            if (items.Count > 0)
            {
                var sec = new InstanceGroupSection(SelectedProfileGroup, false, SelectedProfileGroup, items);
                if (_groupExpandedStates.TryGetValue(SelectedProfileGroup, out var exp))
                    sec.IsExpanded = exp;
                GroupedInstances.Add(sec);
            }
        }
    }

    [RelayCommand]
    public void SelectProfileGroup(string? group)
    {
        SelectedProfileGroup = group?.Trim() ?? "";
    }

    [RelayCommand]
    public void OpenGroupDialog(InstanceModel? instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        PendingGroupInstance = inst;
        EditingGroupName = inst.Group ?? "";
        IsGroupDialogOpen = true;
    }

    [RelayCommand]
    public void PickGroupSuggestion(string? group)
    {
        EditingGroupName = group?.Trim() ?? "";
    }

    [RelayCommand]
    public async Task SaveGroupDialogAsync()
    {
        var inst = PendingGroupInstance;
        if (inst == null) return;

        inst.Group = EditingGroupName?.Trim() ?? "";
        IsGroupDialogOpen = false;
        PendingGroupInstance = null;

        await _instanceService.SaveInstanceAsync(inst);
        RefreshGroupsAndFilter();
        OnStatusChanged?.Invoke(string.IsNullOrWhiteSpace(inst.Group)
            ? string.Format(L10n.T("grp_cleared_status"), inst.Name)
            : string.Format(L10n.T("grp_saved_status"), inst.Name, inst.Group));
    }

    [RelayCommand]
    public void CloseGroupDialog()
    {
        IsGroupDialogOpen = false;
        PendingGroupInstance = null;
    }

    [ObservableProperty]
    private InstanceModel? _selectedInstance;

    partial void OnSelectedInstanceChanged(InstanceModel? value)
    {
        var launch = _getLaunch();
        launch.IsLaunching = value != null && launch.IsInstanceLaunching(value.Id);
        launch.NotifyInstanceChanged();
        UpdateJavaDisplayRequested?.Invoke();
        _preRenameName = value?.Name;

        if (!ReferenceEquals(_prevSelectedInstance, value))
        {
            if (_prevSelectedInstance != null)
                _prevSelectedInstance.PropertyChanged -= OnInstancePropertyChanged;
            _prevSelectedInstance = value;
            if (value != null)
                value.PropertyChanged += OnInstancePropertyChanged;
        }
    }

    private void OnInstancePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstanceModel.IsRunning) || e.PropertyName == nameof(InstanceModel.IsLaunching))
        {
            var launch = _getLaunch();
            launch.NotifyInstanceChanged();
            if (e.PropertyName == nameof(InstanceModel.IsLaunching) && ReferenceEquals(sender, SelectedInstance))
                launch.IsLaunching = SelectedInstance!.IsLaunching;
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
            OnStatusChanged?.Invoke(error ?? "");
        }
        else
        {
            _preRenameName = SelectedInstance.Name;
            RefreshStateRequested?.Invoke();
        }
    }

    [ObservableProperty] private string _selectedInstanceId = string.Empty;
    [ObservableProperty] private string _selectedInstanceName = "No Profile Selected";
    [ObservableProperty] private string _selectedInstanceDetails = "Click '+ Create Profile' to begin";
    [ObservableProperty] private bool _hasInstances;

    [ObservableProperty] private string _newInstanceName = string.Empty;
    partial void OnNewInstanceNameChanged(string value)
    {
        IsNewInstanceNameDuplicate = !string.IsNullOrWhiteSpace(value) &&
            _instanceService.GetInstances().Any(i => i.Name.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public ObservableCollection<string> AvailableVersions { get; } = new();

    [ObservableProperty] private string _selectedVersion = "";
    partial void OnSelectedVersionChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreateInstance));
        _ = UpdateAvailableLoadersAsync(value);
    }

    [ObservableProperty] private bool _isNewInstanceNameDuplicate;

    public bool CanCreateInstance => !string.IsNullOrWhiteSpace(SelectedVersion) &&
        (SelectedLoader == "Vanilla" || !string.IsNullOrWhiteSpace(SelectedLoaderBuild));

    public ObservableCollection<string> AvailableLoaders { get; } = new();

    [ObservableProperty] private string _selectedLoader = "Vanilla";
    partial void OnSelectedLoaderChanged(string value)
    {
        _ = UpdateLoaderBuildsAsync();
        OnPropertyChanged(nameof(CanSwapLoader));
    }

    [ObservableProperty] private bool _isLoadingLoaders;

    public ObservableCollection<string> AvailableLoaderBuilds { get; } = new();

    [ObservableProperty] private string _selectedLoaderBuild = string.Empty;
    partial void OnSelectedLoaderBuildChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreateInstance));
    }

    [ObservableProperty] private bool _showLoaderBuilds;
    partial void OnShowLoaderBuildsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLoaderTypesVisible));
        OnPropertyChanged(nameof(IsLoaderBuildsVisible));
    }

    [ObservableProperty] private string _loaderError = string.Empty;
    [ObservableProperty] private bool _loaderErrorVisible;

    public bool IsLoaderTypesVisible => !ShowLoaderBuilds;
    public bool IsLoaderBuildsVisible => ShowLoaderBuilds;
    public bool CanSwapLoader => SelectedLoader != "Vanilla";

    [RelayCommand]
    public void ToggleLoaderBuilds()
    {
        if (!ShowLoaderBuilds)
        {
            if (AvailableLoaderBuilds.Count == 0 && SelectedLoader != "Vanilla")
                _ = UpdateLoaderBuildsAsync();
        }
        ShowLoaderBuilds = !ShowLoaderBuilds;
    }

    [ObservableProperty] private bool _isRightSidebarOpen;
    [ObservableProperty] private bool _isVersionPickerOpen;
    [ObservableProperty] private bool _isCreateDialogOpen;
    [ObservableProperty] private bool _isImportPickerOpen;
    [ObservableProperty] private string _effectiveJavaPath = "";

    public Action<bool>? SetIsDeleteConfirmOpen { get; set; }
    public Func<bool>? GetIsDeleteConfirmOpen { get; set; }
    public Action<InstanceModel?>? SetPendingDeleteInstance { get; set; }
    public Func<InstanceModel?>? GetPendingDeleteInstance { get; set; }

    public bool IsDeleteConfirmOpen
    {
        get => GetIsDeleteConfirmOpen?.Invoke() ?? false;
        set => SetIsDeleteConfirmOpen?.Invoke(value);
    }

    public InstanceModel? PendingDeleteInstance
    {
        get => GetPendingDeleteInstance?.Invoke();
        set => SetPendingDeleteInstance?.Invoke(value);
    }

    public InstancesViewModel(
        IInstanceService instanceService,
        ModLoaderService modLoaderService,
        Func<LaunchViewModel> getLaunch,
        Func<NavigationViewModel> getNavigation,
        Func<MainWindowViewModel> getHost)
    {
        _instanceService = instanceService;
        _modLoaderService = modLoaderService;
        _getLaunch = getLaunch;
        _getNavigation = getNavigation;
        _getHost = getHost;
    }

    public async Task UpdateAvailableLoadersAsync(string version)
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

            OnPropertyChanged(nameof(SelectedLoader));
        });
        await UpdateLoaderBuildsAsync();
    }

    public async Task UpdateLoaderBuildsAsync()
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
            if (requestId != _loaderBuildsRequestId) return;
            AvailableLoaderBuilds.Clear();
            foreach (var b in builds)
                AvailableLoaderBuilds.Add(b);

            SelectedLoaderBuild = AvailableLoaderBuilds.FirstOrDefault() ?? "";
            OnPropertyChanged(nameof(SelectedLoaderBuild));
            LoaderErrorVisible = false;
            LoaderError = string.Empty;
        });
    }

    public async Task LoadMojangVersionsAsync()
    {
        var cachePath = Path.Combine(ZenithPaths.AppDataDir, "versions_cache.json");
        if (_cachedVersions.Count == 0 && File.Exists(cachePath))
        {
            try
            {
                var cached = await Task.Run(() =>
                {
                    var json = File.ReadAllText(cachePath);
                    return System.Text.Json.JsonSerializer.Deserialize(json, LauncherJsonContext.Default.ListCachedVersionEntry);
                }).ConfigureAwait(true);
                if (cached != null)
                {
                    var valid = cached.Where(v => !string.IsNullOrWhiteSpace(v.Name)).ToList();
                    if (valid.Count > 0)
                    {
                        _cachedVersions.AddRange(valid);
                        FilterVersions();
                    }
                }
            }
            catch { }
        }

        try
        {
            var path = new MinecraftPath(ZenithPaths.AppDataDir);
            var launcher = new MinecraftLauncher(path);
            var versions = await LaunchService.WithManifestRetryAsync(() => launcher.GetAllVersionsAsync().AsTask()).ConfigureAwait(false);

            var fresh = new List<CachedVersionEntry>();
            foreach (var v in versions)
            {
                if (ModLoaderService.IsLoaderProfileId(v.Name)) continue;
                fresh.Add(new CachedVersionEntry { Name = v.Name, Type = v.Type ?? "" });
            }

            if (fresh.Count > 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _cachedVersions.Clear();
                    _cachedVersions.AddRange(fresh);
                    FilterVersions();
                    if (!AvailableVersions.Contains(SelectedVersion) && AvailableVersions.Count > 0)
                        SelectedVersion = AvailableVersions.First();
                });
                try
                {
                    var jsonOut = System.Text.Json.JsonSerializer.Serialize(fresh, LauncherJsonContext.Compact.ListCachedVersionEntry);
                    await File.WriteAllTextAsync(cachePath, jsonOut).ConfigureAwait(false);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to fetch Mojang version list in background", ex);
            if (_cachedVersions.Count == 0)
            {
                _cachedVersions.Add(new CachedVersionEntry { Name = "1.21.1", Type = "release" });
                _cachedVersions.Add(new CachedVersionEntry { Name = "1.20.4", Type = "release" });
                _cachedVersions.Add(new CachedVersionEntry { Name = "1.20.1", Type = "release" });
                _cachedVersions.Add(new CachedVersionEntry { Name = "1.16.5", Type = "release" });
                _cachedVersions.Add(new CachedVersionEntry { Name = "1.8.9", Type = "release" });
                _cachedVersions.Add(new CachedVersionEntry { Name = "1.7.10", Type = "release" });
                _cachedVersions.Add(new CachedVersionEntry { Name = "1.0", Type = "release" });
                FilterVersions();
            }
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

    public void ReorderInstance(string movedId, string targetId, bool dropAfter, bool save = true)
    {
        try
        {
            _instanceService.ReorderInstances(movedId, targetId, dropAfter, save);
            var moved = Instances.FirstOrDefault(i => i.Id == movedId);
            if (moved == null) return;

            var all = _instanceService.GetInstances().ToList();
            int newIdx = all.FindIndex(i => i.Id == movedId);
            int oldIdx = Instances.IndexOf(moved);

            if (oldIdx < 0 || newIdx < 0) return;
            newIdx = Math.Clamp(newIdx, 0, Instances.Count - 1);
            oldIdx = Math.Clamp(oldIdx, 0, Instances.Count - 1);

            if (oldIdx != newIdx)
            {
                var item = Instances[oldIdx];
                Instances.RemoveAt(oldIdx);
                Instances.Insert(newIdx, item);
                RebuildFilteredInstances();
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("ReorderInstance failed", ex);
        }
    }

    public void PersistInstanceOrder()
    {
        var ids = Instances.Select(i => i.Id).ToList();
        _instanceService.SetInstanceOrder(ids);
    }

    [RelayCommand]
    public void SelectInstance(InstanceModel instance)
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
    public void CloseRightSidebar() => IsRightSidebarOpen = false;

    [RelayCommand]
    public async Task CreateDesktopShortcutAsync(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                OnStatusChanged?.Invoke(L10n.T("mw_status_shortcut_os"));
                return;
            }

            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            string shortcutPath;
            if (topLevel?.StorageProvider != null)
            {
                var desktopDir = await topLevel.StorageProvider.TryGetFolderFromPathAsync(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = L10n.T("btn_shortcut"),
                    SuggestedFileName = $"{inst.Name}.lnk",
                    DefaultExtension = "lnk",
                    SuggestedStartLocation = desktopDir,
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Shortcut (*.lnk)") { Patterns = new[] { "*.lnk" } }
                    }
                });

                if (file == null) return;
                shortcutPath = file.Path.LocalPath;
            }
            else
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                shortcutPath = Path.Combine(desktopPath, $"{inst.Name}.lnk");
            }

            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;
            var workDir = AppDomain.CurrentDomain.BaseDirectory;

            var iconLocation = $"{Esc(exePath)},0";
            var customIco = ShortcutIconService.GetOrCreateInstanceIco(inst);
            if (!string.IsNullOrEmpty(customIco))
                iconLocation = $"{Esc(customIco)},0";

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

                OnStatusChanged?.Invoke(File.Exists(shortcutPath)
                    ? string.Format(L10n.T("mw_status_shortcut_done"), inst.Name)
                    : L10n.T("mw_status_shortcut_failed"));
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to create shortcut", ex);
            OnStatusChanged?.Invoke(L10n.T("mw_status_shortcut_failed"));
        }

        static string Esc(string s) => s.Replace("'", "''");
    }

    [RelayCommand]
    public void EditInstance(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        SelectInstance(inst);
        OpenEditInstanceWindow(inst);
    }

    public void OpenEditInstanceWindow(InstanceModel instance)
    {
        _getNavigation().NavigateToEditInstance(instance);
    }

    public void CloseEditInstanceWindow(EditInstanceViewModel viewModel)
    {
        _getNavigation().GoBack();
    }

    [RelayCommand]
    public async Task OpenCopyDialog(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;

        var existingNames = _instanceService.GetInstances().Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string copyName = $"{inst.Name} (Copy)";
        int counter = 2;
        while (existingNames.Contains(copyName))
        {
            copyName = $"{inst.Name} (Copy {counter})";
            counter++;
        }

        OnStatusChanged?.Invoke($"Cloning {inst.Name}...");
        var created = await _instanceService.CreateInstanceAsync(copyName, inst.Version, inst.LoaderType);
        if (created == null) return;
        created.Group = inst.Group;

        try
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(inst.Path) && Directory.Exists(created.Path))
                    CopyTree(inst.Path, created.Path);
            });
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to copy instance directory during clone: {inst.Name}", ex);
        }

        await _instanceService.SaveInstanceAsync(created);
        Instances.Add(created);
        RefreshGroupsAndFilter();
        SelectedInstance = created;
        OnStatusChanged?.Invoke($"Profile '{copyName}' cloned successfully.");
    }

    private static void CopyTree(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);
        foreach (var file in dir.GetFiles())
        {
            if (file.Name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) continue;
            try { file.CopyTo(Path.Combine(destinationDir, file.Name), true); } catch { }
        }

        foreach (var subDir in dir.GetDirectories())
        {
            if (subDir.Name.Equals(".fabric", StringComparison.OrdinalIgnoreCase) ||
                subDir.Name.Equals(".quilt", StringComparison.OrdinalIgnoreCase) ||
                subDir.Name.Equals("crash-reports", StringComparison.OrdinalIgnoreCase))
                continue;

            try { CopyTree(subDir.FullName, Path.Combine(destinationDir, subDir.Name)); } catch { }
        }
    }

    [RelayCommand]
    public void StartRenameInstance(InstanceModel instance)
    {
        if (instance == null || instance.IsRunning || instance.IsLaunching || _getLaunch().IsInstanceLaunching(instance.Id)) return;
        instance.IsEditingName = true;
    }

    [RelayCommand]
    public async Task FinishRenameInstanceAsync(InstanceModel instance)
    {
        if (instance == null) return;
        instance.IsEditingName = false;

        var allInstances = _instanceService.GetInstances();
        if (allInstances.Any(i => i.Id != instance.Id && i.Name.Equals(instance.Name, StringComparison.OrdinalIgnoreCase)))
        {
            OnStatusChanged?.Invoke(string.Format(L10n.T("mw_status_name_exists"), instance.Name));
            return;
        }

        await _instanceService.SaveInstanceAsync(instance);
        RefreshStateRequested?.Invoke();
    }

    [RelayCommand]
    public async Task ChangeInstanceIconAsync(InstanceModel instance)
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
                RefreshStateRequested?.Invoke();
            }
        }
    }

    [RelayCommand]
    public void OpenProfileFolder(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst != null && Directory.Exists(inst.Path))
        {
            Process.Start(new ProcessStartInfo { FileName = inst.Path, UseShellExecute = true, Verb = "open" });
        }
    }

    [RelayCommand]
    public void OpenProfileBrowseMods(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        SelectInstance(inst);

        var editVm = new EditInstanceViewModel(inst, _getHost(), _instanceService);
        var type = ContentType.Mod;
        if (!editVm.HasModsSupport)
            type = ContentType.ResourcePack;

        var vm = new ContentBrowserViewModel(
            inst.Version,
            inst.LoaderType,
            inst.Path,
            msg => OnStatusChanged?.Invoke(msg),
            () => editVm.RefreshFileLists(),
            () => editVm.HasModsSupport,
            type)
        {
            GetCurseForgeApiKey = () => GetCurseForgeApiKey?.Invoke() ?? ""
        };

        _getNavigation().NavigateToContentBrowser(vm, inst, inst.Name, "Mods", editVm);
    }

    [RelayCommand]
    public void OpenProfileServerManager(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        SelectInstance(inst);

        var editVm = new EditInstanceViewModel(inst, _getHost(), _instanceService);
        var path = Path.Combine(inst.Path, "servers.dat");
        var vm = new ServerManagerViewModel(path);
        _getNavigation().NavigateToServerManager(vm, inst, editVm);
    }

    [RelayCommand]
    public void OpenProfileSectionMods(InstanceModel instance) => OpenProfileWithSection(instance, "Mods");

    [RelayCommand]
    public void OpenProfileSectionResourcePacks(InstanceModel instance) => OpenProfileWithSection(instance, "Resource Packs");

    [RelayCommand]
    public void OpenProfileSectionShaderPacks(InstanceModel instance) => OpenProfileWithSection(instance, "Shader Packs");

    [RelayCommand]
    public void OpenProfileSectionWorlds(InstanceModel instance) => OpenProfileWithSection(instance, "Worlds");

    [RelayCommand]
    public void OpenProfileSectionScreenshots(InstanceModel instance) => OpenProfileWithSection(instance, "Screenshots");

    [RelayCommand]
    public void OpenProfileSectionLogs(InstanceModel instance) => OpenProfileWithSection(instance, "Logs");

    public void OpenProfileWithSection(InstanceModel? instance, string tab)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        SelectInstance(inst);
        _getNavigation().NavigateToEditInstance(inst, tab);
    }

    [RelayCommand]
    public void OpenProfileModsFolder(InstanceModel instance) => OpenInstanceSubfolder(instance, "mods");

    [RelayCommand]
    public void OpenProfileResourcePacksFolder(InstanceModel instance) => OpenInstanceSubfolder(instance, "resourcepacks");

    [RelayCommand]
    public void OpenProfileShaderPacksFolder(InstanceModel instance) => OpenInstanceSubfolder(instance, "shaderpacks");

    [RelayCommand]
    public void OpenProfileWorldsFolder(InstanceModel instance) => OpenInstanceSubfolder(instance, "saves");

    [RelayCommand]
    public void OpenProfileScreenshotsFolder(InstanceModel instance) => OpenInstanceSubfolder(instance, "screenshots");

    [RelayCommand]
    public void OpenProfileLogsFolder(InstanceModel instance) => OpenInstanceSubfolder(instance, "logs");

    public void OpenInstanceSubfolder(InstanceModel? instance, string subfolder)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null || string.IsNullOrWhiteSpace(inst.Path)) return;
        try
        {
            var targetDir = Path.Combine(inst.Path, subfolder);
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);
            Process.Start(new ProcessStartInfo { FileName = targetDir, UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to open subfolder '{subfolder}': {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ExportCurseForgeAsync(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        await ExportZipArchiveAsync(inst, "CurseForge", $"{inst.Name}-CurseForge.zip");
    }

    [RelayCommand]
    public async Task ExportModrinthAsync(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        await ExportZipArchiveAsync(inst, "Modrinth", $"{inst.Name}.mrpack");
    }

    [RelayCommand]
    public async Task ExportPrismAsync(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        await ExportZipArchiveAsync(inst, "Prism", $"{inst.Name}-Prism.zip");
    }

    public async Task ExportZipArchiveAsync(InstanceModel instance, string format, string defaultFileName)
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
                try
                {
                    OnStatusChanged?.Invoke($"Exporting {instance.Name} to {format}...");
                    await InstanceExportService.ExportAsync(instance, format, targetPath);
                    OnStatusChanged?.Invoke($"Exported to {Path.GetFileName(targetPath)} successfully.");
                }
                catch (Exception ex)
                {
                    LauncherLog.Error($"Export to {format} failed for {instance.Name}", ex);
                    OnStatusChanged?.Invoke($"Export to {format} failed.");
                }
            }
        }
    }

    [RelayCommand]
    public async Task ConfirmDeleteProfileAsync()
    {
        var inst = PendingDeleteInstance;
        if (inst == null) return;
        IsDeleteConfirmOpen = false;
        PendingDeleteInstance = null;
        OnStatusChanged?.Invoke($"Deleting {inst.Name}...");

        await _instanceService.DeleteInstanceAsync(inst.Id);
        OnStatusChanged?.Invoke($"Deleted {inst.Name}");
        RefreshStateRequested?.Invoke();
        _getHost().ModpacksBrowser?.UpdateInstalledFlags();
    }

    [RelayCommand]
    public void CancelDeleteProfile()
    {
        IsDeleteConfirmOpen = false;
        PendingDeleteInstance = null;
    }

    [RelayCommand]
    public void RequestDeleteProfile(InstanceModel instance)
    {
        var inst = instance ?? SelectedInstance;
        if (inst == null) return;
        PendingDeleteInstance = inst;
        IsDeleteConfirmOpen = true;
    }

    [RelayCommand]
    public void OpenRootDirectory()
    {
        var dir = ZenithPaths.AppDataDir;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true, Verb = "open" });
    }

    [RelayCommand]
    public void ToggleVersionPicker() => IsVersionPickerOpen = !IsVersionPickerOpen;

    [RelayCommand]
    public void SelectVersion(string version)
    {
        SelectedVersion = version;
        IsVersionPickerOpen = false;
        _getHost().IsElyByErrorOpen = false;
    }

    [RelayCommand]
    public void ClearVersion()
    {
        SelectedVersion = "";
    }

    // ── Prism-Style Multi-Source Import Modal State ─────────────────────────
    [ObservableProperty] private string _importSourceTab = "Single";
    [ObservableProperty] private string _importProfileName = "";
    [ObservableProperty] private string _importProfileGroup = "";
    [ObservableProperty] private string _importCustomIconPath = "";
    [ObservableProperty] private Bitmap? _importCustomIconPreview;
    [ObservableProperty] private string _importSourceInput = "";
    [ObservableProperty] private string _importPreviewText = "";
    [ObservableProperty] private string _importErrorText = "";
    [ObservableProperty] private bool _isImporting;
    private string _lastAutoImportName = "";

    public bool IsImportTabSingle => ImportSourceTab == "Single";
    public bool IsImportTabModrinth => ImportSourceTab == "Modrinth";
    public bool IsImportTabCurseForge => ImportSourceTab == "CurseForge";
    public bool IsImportTabMultiMC => ImportSourceTab == "MultiMC";
    public bool IsImportTabPrism => ImportSourceTab == "Prism";
    public bool HasImportCustomIcon => ImportCustomIconPreview != null;
    public bool HasImportPreview => !string.IsNullOrWhiteSpace(ImportPreviewText);
    public bool HasImportError => !string.IsNullOrWhiteSpace(ImportErrorText);

    public bool IsImportUrlSupportedTab => ImportSourceTab is "Single" or "Modrinth" or "CurseForge";

    public string ImportTabHeading => ImportSourceTab switch
    {
        "Modrinth" => L10n.T("imp_src_modrinth"),
        "CurseForge" => L10n.T("imp_src_curseforge"),
        "MultiMC" => L10n.T("imp_mmc_t"),
        "Prism" => L10n.T("imp_prism_t"),
        _ => L10n.T("imp_single_t")
    };

    public string ImportTabDescription => ImportSourceTab switch
    {
        "Modrinth" => L10n.T("imp_desc_modrinth"),
        "CurseForge" => L10n.T("imp_desc_curseforge"),
        "MultiMC" => L10n.T("imp_mmc_d"),
        "Prism" => L10n.T("imp_prism_d"),
        _ => L10n.T("imp_desc_single")
    };

    public string ImportInputWatermark => ImportSourceTab switch
    {
        "Modrinth" => L10n.T("imp_ph_modrinth"),
        "CurseForge" => L10n.T("imp_ph_curseforge"),
        "MultiMC" => L10n.T("imp_ph_multimc"),
        "Prism" => L10n.T("imp_ph_prism"),
        _ => L10n.T("imp_ph_single")
    };

    public bool CanConfirmImport =>
        !IsImporting &&
        !string.IsNullOrWhiteSpace(ImportSourceInput) &&
        string.IsNullOrWhiteSpace(ImportErrorText);

    partial void OnImportSourceTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsImportTabSingle));
        OnPropertyChanged(nameof(IsImportTabModrinth));
        OnPropertyChanged(nameof(IsImportTabCurseForge));
        OnPropertyChanged(nameof(IsImportTabMultiMC));
        OnPropertyChanged(nameof(IsImportTabPrism));
        OnPropertyChanged(nameof(IsImportUrlSupportedTab));
        OnPropertyChanged(nameof(ImportTabHeading));
        OnPropertyChanged(nameof(ImportTabDescription));
        OnPropertyChanged(nameof(ImportInputWatermark));
        EvaluateImportSourceInput();
    }

    partial void OnImportSourceInputChanged(string value) => EvaluateImportSourceInput();

    partial void OnImportPreviewTextChanged(string value) => OnPropertyChanged(nameof(HasImportPreview));

    partial void OnImportErrorTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasImportError));
        OnPropertyChanged(nameof(CanConfirmImport));
    }

    partial void OnIsImportingChanged(bool value) => OnPropertyChanged(nameof(CanConfirmImport));

    private void EvaluateImportSourceInput()
    {
        ImportErrorText = "";
        ImportPreviewText = "";
        var raw = ImportSourceInput?.Trim() ?? "";
        if (string.IsNullOrEmpty(raw))
        {
            OnPropertyChanged(nameof(CanConfirmImport));
            return;
        }

        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            ImportPreviewText = L10n.T("imp_url_ready");
            OnPropertyChanged(nameof(CanConfirmImport));
            return;
        }

        if (!File.Exists(raw) && !Directory.Exists(raw))
        {
            ImportErrorText = L10n.T("imp_path_missing");
            OnPropertyChanged(nameof(CanConfirmImport));
            return;
        }

        if (File.Exists(raw) && Path.GetExtension(raw).Equals(".rar", StringComparison.OrdinalIgnoreCase))
        {
            ImportErrorText = L10n.T("imp_rar_unsupported");
            OnPropertyChanged(nameof(CanConfirmImport));
            return;
        }

        var found = InstanceImportService.Scan(raw);
        if (found.Count == 0)
        {
            ImportErrorText = L10n.T("imp_not_recognized");
        }
        else if (found.Count == 1)
        {
            var f = found[0];
            var verText = string.IsNullOrWhiteSpace(f.McVersion) ? "Minecraft" : f.McVersion;
            var loaderText = string.IsNullOrWhiteSpace(f.LoaderType) ? "Vanilla" : f.LoaderType;
            ImportPreviewText = $"{f.Name}  •  {verText}  •  {loaderText}";

            if (string.IsNullOrWhiteSpace(ImportProfileName) ||
                string.Equals(ImportProfileName, _lastAutoImportName, StringComparison.Ordinal))
            {
                _lastAutoImportName = f.Name;
                ImportProfileName = f.Name;
            }
        }
        else
        {
            ImportPreviewText = string.Format(L10n.T("imp_found_multiple"), found.Count);
        }

        OnPropertyChanged(nameof(CanConfirmImport));
    }

    private void SetImportCustomIconPreview(Bitmap? next)
    {
        var prev = ImportCustomIconPreview;
        ImportCustomIconPreview = next;
        OnPropertyChanged(nameof(HasImportCustomIcon));
        prev?.Dispose();
    }

    private void ResetImportModalState()
    {
        ImportSourceTab = "Single";
        ImportProfileName = "";
        _lastAutoImportName = "";
        ImportProfileGroup = string.IsNullOrEmpty(SelectedProfileGroup) ? "" : SelectedProfileGroup;
        ImportCustomIconPath = "";
        SetImportCustomIconPreview(null);
        ImportSourceInput = "";
        ImportPreviewText = "";
        ImportErrorText = "";
        IsImporting = false;
    }

    [RelayCommand]
    public async Task ImportInstancesAsync()
    {
        ResetImportModalState();
        IsImportPickerOpen = true;
        await Task.CompletedTask;
    }

    [RelayCommand]
    public void CloseImportPicker()
    {
        if (IsImporting) return;
        IsImportPickerOpen = false;
        SetImportCustomIconPreview(null);
    }

    [RelayCommand]
    public void SelectImportTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab)) return;
        ImportSourceTab = tab;
    }

    [RelayCommand]
    public void PickImportGroup(string? group)
    {
        ImportProfileGroup = group?.Trim() ?? "";
    }

    [RelayCommand]
    public async Task PickImportIconAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L10n.T("imp_pick_icon_title"),
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });
        if (files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        if (!File.Exists(path)) return;

        try
        {
            await using var fs = File.OpenRead(path);
            var bmp = Bitmap.DecodeToWidth(fs, 128, BitmapInterpolationMode.MediumQuality);
            ImportCustomIconPath = path;
            SetImportCustomIconPreview(bmp);
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Failed to load custom import icon: {ex.Message}");
        }
    }

    [RelayCommand]
    public void ClearImportIcon()
    {
        ImportCustomIconPath = "";
        SetImportCustomIconPreview(null);
    }

    [RelayCommand]
    public async Task BrowseImportFileAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (topLevel?.StorageProvider == null) return;

        var patterns = ImportSourceTab == "Modrinth"
            ? new[] { "*.mrpack", "*.zip" }
            : ImportSourceTab == "CurseForge"
                ? new[] { "*.zip", "*.mrpack" }
                : new[] { "*.zip", "*.mrpack" };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L10n.T("imp_browse_file"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Modpack / Instance Archive") { Patterns = patterns },
                FilePickerFileTypes.All
            }
        });
        if (files.Count > 0)
            ImportSourceInput = files[0].Path.LocalPath;
    }

    [RelayCommand]
    public async Task BrowseImportFolderAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (topLevel?.StorageProvider == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = L10n.T("imp_browse_folder"),
            AllowMultiple = false
        });
        if (folders.Count > 0)
            ImportSourceInput = folders[0].Path.LocalPath;
    }

    [RelayCommand]
    public async Task ConfirmImportAsync()
    {
        var source = ImportSourceInput?.Trim() ?? "";
        if (string.IsNullOrEmpty(source) || IsImporting) return;

        var customName = ImportProfileName?.Trim();
        var customIcon = ImportCustomIconPath?.Trim();
        var group = ImportProfileGroup?.Trim();
        var cfApiKey = GetCurseForgeApiKey?.Invoke();

        IsImporting = true;
        ImportErrorText = "";
        OnStatusChanged?.Invoke(L10n.T("profile_importing"));
        try
        {
            ImportResult result;
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                result = await Task.Run(() =>
                    InstanceImportService.ImportFromUrlAsync(_instanceService, source, customName: customName, customIconPath: customIcon, group: group, curseForgeApiKey: cfApiKey));
            }
            else
            {
                var detected = InstanceImportService.Detect(source);
                if (detected == ImportSourceKind.None)
                {
                    ImportErrorText = L10n.T("imp_not_recognized");
                    return;
                }
                result = await Task.Run(() =>
                    InstanceImportService.ImportAsync(_instanceService, source, customName: customName, customIconPath: customIcon, group: group, curseForgeApiKey: cfApiKey));
            }

            if (result.Imported <= 0)
            {
                ImportErrorText = result.Warnings.Count > 0 ? result.Warnings[0] : L10n.T("profile_import_none");
                OnStatusChanged?.Invoke(ImportErrorText);
                return;
            }

            var msg = string.Format(L10n.T("profile_imported"), result.Imported, result.Skipped);
            if (result.Warnings.Count > 0)
                msg += " " + result.Warnings[0];

            IsImportPickerOpen = false;
            SetImportCustomIconPreview(null);
            OnStatusChanged?.Invoke(msg);
            RefreshStateRequested?.Invoke();
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Instance import failed", ex);
            ImportErrorText = ex.Message;
            OnStatusChanged?.Invoke(ex.Message);
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    public async Task PickImportSingleAsync() => await BrowseImportFileAsync();

    [RelayCommand]
    public async Task PickImportFileAsync() => await BrowseImportFileAsync();

    [RelayCommand]
    public async Task PickImportFolderAsync(string? kind) => await BrowseImportFolderAsync();

    public async Task RunImportProcessAsync(string path)
    {
        var detected = InstanceImportService.Detect(path);
        if (detected == ImportSourceKind.None)
        {
            OnStatusChanged?.Invoke(L10n.T("imp_not_recognized"));
            return;
        }

        IsImportPickerOpen = false;
        OnStatusChanged?.Invoke(L10n.T("profile_importing"));
        try
        {
            var result = await Task.Run(() => InstanceImportService.ImportAsync(_instanceService, path));
            var msg = result.Imported > 0
                ? string.Format(L10n.T("profile_imported"), result.Imported, result.Skipped)
                : L10n.T("profile_import_none");
            if (result.Warnings.Count > 0)
                msg += " " + result.Warnings[0];
            OnStatusChanged?.Invoke(msg);
            RefreshStateRequested?.Invoke();
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Instance import failed", ex);
            OnStatusChanged?.Invoke(ex.Message);
        }
    }

    [RelayCommand]
    public void OpenNewInstanceDialog()
    {
        _getNavigation().NavigateToCreateProfile();
    }

    [RelayCommand]
    public async Task ConfirmCreateInstanceAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedVersion))
        {
            OnStatusChanged?.Invoke(L10n.T("mw_status_need_version"));
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
            OnStatusChanged?.Invoke(string.Format(L10n.T("mw_status_name_exists"), finalName));
            return;
        }

        _instanceService.SetSelectedInstance(inst.Id);
        CloseDialogsRequested?.Invoke();
        RefreshStateRequested?.Invoke();

        NewInstanceName = "";
        SelectedVersion = "";
        SelectedLoader = "Vanilla";
        SelectedLoaderBuild = "";
        ShowLoaderBuilds = false;
    }
}
