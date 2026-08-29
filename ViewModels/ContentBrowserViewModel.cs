using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class ContentBrowserViewModel : ObservableObject
{
    private const int PageSize = 20;

    private readonly string _gameVersion;
    private readonly string _loaderType;
    private readonly string _instancePath;
    private readonly Action<string> _reportStatus;
    private readonly Action _refreshAll;
    private readonly Func<bool> _hasModsSupport;
    private int _offset;
    private int _totalHits;

    public ObservableCollection<ModrinthProject> Results { get; } = new();
    public ObservableCollection<BrowserTag> AvailableTags { get; } = new();

    [ObservableProperty]
    private ContentType _selectedContentType = ContentType.Mod;

    private ContentSortOption _selectedSort = ContentSortOption.Relevance;
    public ContentSortOption SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (_selectedSort == value) return;
            _selectedSort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSortDisplay));
            _ = ReloadAsync();
        }
    }

    [ObservableProperty]
    private string _selectedSortDisplay = "Relevance";

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private string _selectedWorld = "";

    [ObservableProperty]
    private ModrinthProject? _pendingDelete;

    [ObservableProperty]
    private ModrinthProject? _selectedProject;

    public bool IsDetailsOpen => SelectedProject != null;

    /// <summary>Read-only surface the details overlay binds against.</summary>
    public ModrinthProject? DetailsProject => SelectedProject;

    // --- Full project data for details page ---

    [ObservableProperty]
    private Models.ModrinthFullProject? _fullProject;

    [ObservableProperty]
    private bool _fullProjectBusy;

    /// <summary>Body text with raw HTML tags stripped for display.</summary>
    public string CleanBody => FullProject == null ? "" : MarkdownSanitizer.Clean(FullProject.Body);

    [ObservableProperty]
    private string _selectedDetailTab = "description";

    [ObservableProperty]
    private bool _showAllVersions;

    [ObservableProperty]
    private string _versionFilter = "";

    /// <summary>MC version selected in the Compatibility sidebar. Empty = use instance version.</summary>
    [ObservableProperty]
    private string _selectedCompatibilityVersion = "";

    /// <summary>String Id of the currently selected version row (for green highlight binding).</summary>
    [ObservableProperty]
    private string _selectedVersionId = "";

    partial void OnSelectedCompatibilityVersionChanged(string value)
    {
        OnPropertyChanged(nameof(VersionList));
        OnPropertyChanged(nameof(IsCompatibilityFilterActive));
        OnPropertyChanged(nameof(CompatibilityVersions));
    }

    partial void OnSelectedVersionForDetailsChanged(ModrinthProjectVersion? value)
    {
        SelectedVersionId = value?.Id ?? "";
        OnPropertyChanged(nameof(ReinstallText));
    }

    public bool IsCompatibilityFilterActive => !string.IsNullOrWhiteSpace(SelectedCompatibilityVersion);

    /// <summary>
    /// GameVersions from the project + the instance version (if not already present),
    /// sorted newest→oldest. Used by the Compatibility sidebar.
    /// </summary>
    public IReadOnlyList<string> CompatibilityVersions
    {
        get
        {
            var versions = FullProject?.GameVersions ?? SelectedProject?.GameVersions ?? Array.Empty<string>();
            var list = versions.ToList();
            if (!string.IsNullOrWhiteSpace(_gameVersion) && !list.Contains(_gameVersion, StringComparer.OrdinalIgnoreCase))
                list.Add(_gameVersion);
            var sorted = list.OrderByDescending(v => ParseVersionString(v)).ToList();
            if (!string.IsNullOrWhiteSpace(_gameVersion))
            {
                var idx = sorted.FindIndex(v => string.Equals(v, _gameVersion, StringComparison.OrdinalIgnoreCase));
                if (idx > 0)
                {
                    var item = sorted[idx];
                    sorted.RemoveAt(idx);
                    sorted.Insert(0, item);
                }
            }
            return sorted;
        }
    }

    private static double ParseVersionString(string v)
    {
        var digits = new string(v.Where(c => char.IsDigit(c) || c == '.').ToArray());
        var parts = digits.Split('.', StringSplitOptions.RemoveEmptyEntries);
        double result = 0;
        foreach (var p in parts)
            if (int.TryParse(p, out var n)) result = result * 1000 + n;
        return result;
    }

    [RelayCommand]
    private void SelectCompatibilityVersion(string version)
    {
        SelectedCompatibilityVersion = version;
    }

    [RelayCommand]
    private void ClearCompatibilityFilter()
    {
        SelectedCompatibilityVersion = "";
    }

    public bool InstallButtonVisible => SelectedProject != null;

    partial void OnSelectedDetailTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsDescriptionTab));
        OnPropertyChanged(nameof(IsChangelogTab));
        OnPropertyChanged(nameof(IsVersionsTab));
        OnPropertyChanged(nameof(VersionList));
    }

    public bool IsDescriptionTab => SelectedDetailTab.Equals("description", StringComparison.OrdinalIgnoreCase);
    public bool IsChangelogTab => SelectedDetailTab.Equals("changelog", StringComparison.OrdinalIgnoreCase);
    public bool IsVersionsTab => SelectedDetailTab.Equals("versions", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void SelectTab(string tab)
    {
        SelectedDetailTab = tab;
    }

    private List<Models.ModrinthProjectVersion> _allVersions = new();

    public List<Models.ModrinthProjectVersion> VersionList
    {
        get
        {
            if (_allVersions.Count == 0) return _allVersions;
            var filtered = _allVersions;
            // Filter by selected compatibility version (from sidebar), or instance version
            var targetVersion = !string.IsNullOrWhiteSpace(SelectedCompatibilityVersion)
                ? SelectedCompatibilityVersion
                : _gameVersion;
            if (!ShowAllVersions && !string.IsNullOrWhiteSpace(targetVersion))
            {
                filtered = filtered.Where(v =>
                    v.GameVersions.Any(gv => gv.Contains(targetVersion))).ToList();
            }
            if (!string.IsNullOrWhiteSpace(VersionFilter))
            {
                var q = VersionFilter.ToLowerInvariant();
                filtered = filtered.Where(v =>
                    v.Name.ToLowerInvariant().Contains(q) ||
                    v.VersionNumber.ToLowerInvariant().Contains(q)).ToList();
            }
            return filtered;
        }
    }

    partial void OnVersionFilterChanged(string value) => OnPropertyChanged(nameof(VersionList));
    partial void OnShowAllVersionsChanged(bool value) => OnPropertyChanged(nameof(VersionList));

    // --- Version selector modal ---

    [ObservableProperty]
    private bool _isVersionSelectorOpen;

    [ObservableProperty]
    private Models.ModrinthProjectVersion? _selectedVersionToInstall;

    /// <summary>Version row currently selected in the Versions tab.</summary>
    [ObservableProperty]
    private Models.ModrinthProjectVersion? _selectedVersionForDetails;

    public Models.ModrinthFullProject? VersionSelectorProject => FullProject;

    /// <summary>Button text: "Install" for new content, "Reinstall" for already-installed.</summary>
    public string ReinstallText => SelectedProject is { IsInstalled: true }
        ? L10n.T("mi_reinstall")
        : L10n.T("mi_install");

    partial void OnSelectedProjectChanged(ModrinthProject? value)
    {
        OnPropertyChanged(nameof(IsDetailsOpen));
        OnPropertyChanged(nameof(DetailsProject));
        OnPropertyChanged(nameof(InstallButtonVisible));
        OnPropertyChanged(nameof(ReinstallText));
        SelectedVersionForDetails = null;
        SelectedCompatibilityVersion = _gameVersion;
        if (value != null) _ = LoadFullProjectAsync(value);
        else FullProject = null;
    }

    [RelayCommand]
    private void CloseVersionSelector()
    {
        IsVersionSelectorOpen = false;
        SelectedVersionToInstall = null;
    }

    public bool IsDeleteConfirmOpen => PendingDelete != null;

    public string DeleteItemName => PendingDelete?.Title ?? "";

    public string DeleteConfirmText =>
        PendingDelete == null ? "" : string.Format(L10n.T("cb_delete_confirm"), PendingDelete.Title);

    partial void OnPendingDeleteChanged(ModrinthProject? value)
    {
        OnPropertyChanged(nameof(IsDeleteConfirmOpen));
        OnPropertyChanged(nameof(DeleteItemName));
    }

    partial void OnSelectedWorldChanged(string value)
    {
        if (SelectedContentType == ContentType.DataPack)
            UpdateInstalledFlags();
    }

    public ObservableCollection<string> AvailableWorlds { get; } = new();

    public bool HasModsSupport => _hasModsSupport();
    public bool HasShaderSupport => HasModsSupport || _loaderType.Trim().Equals("OptiFine", StringComparison.OrdinalIgnoreCase) || _loaderType.Trim().Equals("Iris", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ContentType> AvailableContentTypes
    {
        get
        {
            var list = new List<ContentType> { ContentType.Mod, ContentType.ResourcePack, ContentType.Shader, ContentType.DataPack };
            if (!HasModsSupport) list.Remove(ContentType.Mod);
            if (!HasShaderSupport) list.Remove(ContentType.Shader);
            return list;
        }
    }

    public IReadOnlyList<string> SortOptions => new[] { "Relevance", "Downloads", "Newest", "Recently Updated" };

    public string FilterLabel
    {
        get
        {
            var loaders = MapLoaders(_loaderType);
            var loaderStr = loaders.Length == 0 ? "any loader" : string.Join(" / ", loaders);
            return $"Minecraft {_gameVersion} • {loaderStr}";
        }
    }

    public string InstanceInfoText => $"{System.IO.Path.GetFileName(_instancePath)} • Minecraft {_gameVersion} • {_loaderType}";
    public string InstanceName => System.IO.Path.GetFileName(_instancePath);
    public string InstanceVersion => _gameVersion;
    public string InstanceLoader => _loaderType;
    public string InstancePath => _instancePath;

    public bool IsEmpty => Results.Count == 0 && !IsBusy && !IsLoadingMore;
    public bool HasMore => _offset < _totalHits;
    public bool IsDataPackSelected => SelectedContentType == ContentType.DataPack;

    public ContentBrowserViewModel(
        string gameVersion,
        string loaderType,
        string instancePath,
        Action<string> reportStatus,
        Action refreshAll,
        Func<bool> hasModsSupport,
        ContentType initialType = ContentType.Mod)
    {
        _gameVersion = gameVersion;
        _loaderType = loaderType;
        _instancePath = instancePath;
        _reportStatus = reportStatus;
        _refreshAll = refreshAll;
        _hasModsSupport = hasModsSupport;

        if (initialType == ContentType.Mod && !HasModsSupport)
            initialType = ContentType.ResourcePack;
        if (initialType == ContentType.Shader && !HasShaderSupport)
            initialType = ContentType.ResourcePack;
        SelectedContentType = initialType;

        RefreshWorlds();
        UpdateAvailableTags();
        _ = ReloadAsync();
    }

    private static string[] MapLoaders(string loader) => loader.Trim().ToLowerInvariant() switch
    {
        "fabric" => new[] { "fabric" },
        "forge" => new[] { "forge" },
        "neoforge" => new[] { "neoforged" },
        "quilt" => new[] { "quilt" },
        _ => Array.Empty<string>()
    };

    private static string MapSortToModrinth(ContentSortOption sort) => sort switch
    {
        ContentSortOption.Downloads => "downloads",
        ContentSortOption.Newest => "newest",
        ContentSortOption.RecentlyUpdated => "updated",
        _ => "relevance"
    };

    public void RefreshWorlds()
    {
        var saves = Path.Combine(_instancePath, "saves");
        AvailableWorlds.Clear();
        if (Directory.Exists(saves))
        {
            foreach (var dir in Directory.GetDirectories(saves))
            {
                var name = Path.GetFileName(dir);
                if (File.Exists(Path.Combine(dir, "level.dat")))
                    AvailableWorlds.Add(name);
            }
        }
        if (AvailableWorlds.Count > 0 && string.IsNullOrWhiteSpace(SelectedWorld))
            SelectedWorld = AvailableWorlds[0];
        OnPropertyChanged(nameof(IsDataPackSelected));
    }

    partial void OnSelectedContentTypeChanged(ContentType value)
    {
        UpdateAvailableTags();
        OnPropertyChanged(nameof(IsDataPackSelected));
        _ = ReloadAsync();
    }

    partial void OnSelectedSortDisplayChanged(string value)
    {
        var mapped = value switch
        {
            "Downloads" => ContentSortOption.Downloads,
            "Newest" => ContentSortOption.Newest,
            "Recently Updated" => ContentSortOption.RecentlyUpdated,
            _ => ContentSortOption.Relevance
        };
        if (_selectedSort != mapped)
        {
            _selectedSort = mapped;
            OnPropertyChanged(nameof(SelectedSort));
            _ = ReloadAsync();
        }
    }

    public void SetContentType(ContentType type)
    {
        if (type == ContentType.Mod && !HasModsSupport) type = ContentType.ResourcePack;
        if (type == ContentType.Shader && !HasShaderSupport) type = ContentType.ResourcePack;
        SelectedContentType = type;
    }

    private void UpdateAvailableTags()
    {
        AvailableTags.Clear();
        // Leading reset chip: clicking it clears the active category filter.
        AvailableTags.Add(new BrowserTag { Name = "All", IsReset = true });
        var tags = SelectedContentType switch
        {
            ContentType.Mod => new[] { "adventure", "magic", "technology", "utility", "library", "optimization", "worldgen", "equipment" },
            ContentType.ResourcePack => new[] { "16x", "32x", "64x", "128x", "realistic", "cartoon", "themed", "vanilla-like" },
            ContentType.Shader => new[] { "realistic", "fantasy", "vanilla", "vibrant", "soft", "performance" },
            ContentType.DataPack => new[] { "utility", "adventure", "magic", "game-mechanics", "mobs", "optimization" },
            _ => Array.Empty<string>()
        };
        foreach (var t in tags) AvailableTags.Add(new BrowserTag { Name = t });
    }

    [RelayCommand]
    private void ApplyTag(BrowserTag? tag)
    {
        if (tag == null) return;
        if (tag.IsReset)
        {
            var changed = false;
            foreach (var t in AvailableTags)
            {
                if (!t.IsReset && t.IsSelected)
                {
                    t.IsSelected = false;
                    changed = true;
                }
            }
            if (changed) _ = ReloadAsync();
            return;
        }
        tag.IsSelected = !tag.IsSelected;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchQuery = "";
        foreach (var t in AvailableTags) t.IsSelected = false;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void SelectSort(string sort)
    {
        SelectedSort = sort switch
        {
            "Downloads" => ContentSortOption.Downloads,
            "Newest" => ContentSortOption.Newest,
            "Recently Updated" => ContentSortOption.RecentlyUpdated,
            _ => ContentSortOption.Relevance
        };
    }

    [RelayCommand]
    private Task Search() => ReloadAsync();

    public async Task ReloadAsync()
    {
        IsBusy = true;
        StatusText = "";
        Results.Clear();
        _offset = 0;
        _totalHits = 0;
        try
        {
            var categoryTags = AvailableTags.Where(t => t.IsSelected && !t.IsReset).Select(t => t.Name).ToList();
            var loaders = SelectedContentType == ContentType.Mod ? MapLoaders(_loaderType) : Array.Empty<string>();
            var projectType = SelectedContentType.ModrinthProjectType();
            var sortIndex = MapSortToModrinth(SelectedSort);
            var page = await ModrinthApiService.SearchAsync(SearchQuery, _gameVersion, loaders, projectType, sortIndex, categoryTags, 0, PageSize);

            if (page == null)
            {
                StatusText = L10n.T("mp_status_unreachable");
                return;
            }
            _totalHits = page.TotalHits;
            _offset = page.Hits.Count;
            foreach (var p in page.Hits)
            {
                Results.Add(p);
                LoadIcon(p);
            }
            UpdateInstalledFlags();
            StatusText = page.TotalHits == 0
                ? string.Format(L10n.T("cb_status_none"), SelectedContentType.DisplayName().ToLower())
                : string.Format(L10n.T("cb_status_count"), page.TotalHits, SelectedContentType.DisplayName().ToLower(), _gameVersion);
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Content search failed", ex);
            StatusText = string.Format(L10n.T("mp_status_failed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasMore));
        }
    }

    public async Task LoadMoreAsync()
    {
        if (IsBusy || IsLoadingMore || !HasMore) return;
        IsLoadingMore = true;
        try
        {
            var categoryTags = AvailableTags.Where(t => t.IsSelected && !t.IsReset).Select(t => t.Name).ToList();
            var loaders = SelectedContentType == ContentType.Mod ? MapLoaders(_loaderType) : Array.Empty<string>();
            var projectType = SelectedContentType.ModrinthProjectType();
            var sortIndex = MapSortToModrinth(SelectedSort);
            var page = await ModrinthApiService.SearchAsync(SearchQuery, _gameVersion, loaders, projectType, sortIndex, categoryTags, _offset, PageSize);
            if (page != null)
            {
                _offset = page.Offset + page.Hits.Count;
                _totalHits = page.TotalHits;
                foreach (var p in page.Hits)
                {
                    Results.Add(p);
                    LoadIcon(p);
                }
                UpdateInstalledFlags();
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Content load-more failed", ex);
        }
        finally
        {
            IsLoadingMore = false;
            OnPropertyChanged(nameof(HasMore));
        }
    }

    private async void LoadIcon(ModrinthProject project)
    {
        if (string.IsNullOrWhiteSpace(project.IconUrl) || project.HasIcon) return;
        var bytes = await ModrinthApiService.GetIconAsync(project.IconUrl);
        project.SetIcon(bytes);
    }

    [RelayCommand]
    private void DeleteOrInstall(ModrinthProject project)
    {
        if (project == null || project.IsInstalling) return;
        if (project.IsInstalled)
        {
            PendingDelete = project;
            return;
        }
        // Open version selector instead of installing directly
        OpenVersionSelector(project);
    }

    [RelayCommand]
    private void InstallSelectedVersion(Models.ModrinthProjectVersion? version)
    {
        if (version == null || SelectedProject == null) return;
        IsVersionSelectorOpen = false;
        _ = InstallSpecificVersionAsync(SelectedProject, version);
    }

    private async Task InstallSpecificVersionAsync(ModrinthProject project, Models.ModrinthProjectVersion version)
    {
        if (project == null || project.IsInstalling) return;
        try
        {
            var targetFolder = SelectedContentType.InstallFolder(_instancePath, SelectedWorld);
            var file = version.Files.FirstOrDefault(f => f.IsPrimary) ?? version.Files.FirstOrDefault();
            if (file == null || string.IsNullOrWhiteSpace(file.Url)) return;

            project.IsInstalling = true;
            StatusText = string.Format(L10n.T("mi_installing"));

            var data = await ModrinthApiService.DownloadAsync(file.Url);
            if (data is not { Length: > 0 })
            {
                StatusText = string.Format(L10n.T("cb_download_failed"), file.Filename);
                return;
            }

            var dest = Path.Combine(targetFolder, SanitizeFileName(file.Filename));
            Directory.CreateDirectory(targetFolder);
            await File.WriteAllBytesAsync(dest, data);

            // Cache icon
            if ((SelectedContentType == ContentType.Shader || SelectedContentType == ContentType.DataPack) && !string.IsNullOrWhiteSpace(project.IconUrl))
            {
                try
                {
                    var iconBytes = await ModrinthApiService.GetIconAsync(project.IconUrl);
                    if (iconBytes != null)
                    {
                        var cacheDir = Path.Combine(ZenithPaths.AppDataDir, "cache", "icons");
                        Directory.CreateDirectory(cacheDir);
                        var cacheFile = Path.Combine(cacheDir, Path.GetFileNameWithoutExtension(dest) + ".png");
                        await File.WriteAllBytesAsync(cacheFile, iconBytes);
                        var cacheById = Path.Combine(cacheDir, $"{project.ProjectId}.png");
                        await File.WriteAllBytesAsync(cacheById, iconBytes);
                    }
                }
                catch { }
            }

            try { MarkInstalled(project.ProjectId, Path.GetFileName(dest)); } catch { }

            StatusText = string.Format(L10n.T("cb_installed_file"), project.Title, file.Filename);
            project.IsInstalled = true;
            project.InstalledVersion = version.VersionNumber;
            UpdateInstalledFlags();
            _refreshAll?.Invoke();
            _reportStatus?.Invoke(StatusText);
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Content install failed for {project.Title}", ex);
            StatusText = string.Format(L10n.T("mp_install_failed"), ex.Message);
        }
        finally
        {
            project.IsInstalling = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task InstallAsync(ModrinthProject project)
    {
        if (project == null || project.IsInstalling) return;

        if (SelectedContentType == ContentType.DataPack && string.IsNullOrWhiteSpace(SelectedWorld))
        {
            StatusText = L10n.T("cb_select_world_first");
            return;
        }

        project.IsInstalling = true;
        Services.DiscordPresenceService.SetInstallingContent(project.Title);
        StatusText = string.Format(L10n.T("cb_resolving_for"), project.Title);
        try
        {
            var loaders = SelectedContentType == ContentType.Mod ? MapLoaders(_loaderType) : Array.Empty<string>();
            var version = await ModrinthApiService.GetLatestVersionAsync(project.ProjectId, _gameVersion, loaders);

            if (version == null)
            {
                StatusText = string.Format(L10n.T("cb_no_match"), project.Title, FilterLabel);
                return;
            }

            var targetFolder = SelectedContentType.InstallFolder(_instancePath, SelectedWorld);
            Directory.CreateDirectory(targetFolder);

            var dest = Path.Combine(targetFolder, SanitizeFileName(version.FileName));
            if (File.Exists(dest))
            {
                StatusText = string.Format(L10n.T("cb_already_installed"), version.FileName);
                project.IsInstalled = true;
                return;
            }

            StatusText = string.Format(L10n.T("mp_downloading"), version.FileName);
            var data = await ModrinthApiService.DownloadAsync(version.FileUrl);
            if (data is not { Length: > 0 })
            {
                StatusText = string.Format(L10n.T("cb_download_failed"), version.FileName);
                return;
            }

            await File.WriteAllBytesAsync(dest, data);

            // Cache preview strictly in %APPDATA%\.zenith\cache\icons\ by project ID (and file name for Enrich lookup)
            if ((SelectedContentType == ContentType.Shader || SelectedContentType == ContentType.DataPack) && !string.IsNullOrWhiteSpace(project.IconUrl))
            {
                try
                {
                    var iconBytes = await ModrinthApiService.GetIconAsync(project.IconUrl);
                    if (iconBytes != null)
                    {
                        var cacheDir = Path.Combine(ZenithPaths.AppDataDir, "cache", "icons");
                        Directory.CreateDirectory(cacheDir);
                        var cacheFile = Path.Combine(cacheDir, Path.GetFileNameWithoutExtension(dest) + ".png");
                        await File.WriteAllBytesAsync(cacheFile, iconBytes);
                        var cacheById = Path.Combine(cacheDir, $"{project.ProjectId}.png");
                        await File.WriteAllBytesAsync(cacheById, iconBytes);
                    }
                }
                catch { }
            }

            // Persist installed marker by project ID for exact Installed check
            try { MarkInstalled(project.ProjectId, Path.GetFileName(dest)); } catch { }

            StatusText = string.Format(L10n.T("cb_installed_file"), project.Title, version.FileName);
            project.IsInstalled = true;
            project.InstalledVersion = version.VersionNumber;
            UpdateInstalledFlags();

            if (SelectedContentType == ContentType.Mod && version.Dependencies.Count > 0)
            {
                var required = version.Dependencies.Where(d => d.DependencyType.Equals("required", StringComparison.OrdinalIgnoreCase)).ToList();
                if (required.Count > 0)
                {
                    StatusText = string.Format(L10n.T("cb_installing_deps"), required.Count);
                    foreach (var dep in required)
                    {
                        await InstallDependencyAsync(dep, targetFolder);
                    }
                    StatusText = string.Format(L10n.T("cb_installed_deps"), project.Title, required.Count);
                    UpdateInstalledFlags();
                }
            }

            _refreshAll?.Invoke();
            _reportStatus?.Invoke(StatusText);
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Content install failed for {project.Title}", ex);
            StatusText = string.Format(L10n.T("mp_install_failed"), ex.Message);
        }
        finally
        {
            project.IsInstalling = false;
        }
    }

    private async Task InstallDependencyAsync(ModrinthDependency dep, string targetFolder)
    {
        try
        {
            var loaders = MapLoaders(_loaderType);
            var depVersion = await ModrinthApiService.GetLatestVersionAsync(dep.ProjectId, _gameVersion, loaders);
            if (depVersion == null) return;
            var depDest = Path.Combine(targetFolder, SanitizeFileName(depVersion.FileName));
            if (File.Exists(depDest)) return;
            var data = await ModrinthApiService.DownloadAsync(depVersion.FileUrl);
            if (data is { Length: > 0 })
                await File.WriteAllBytesAsync(depDest, data);
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to install dependency {dep.ProjectId}", ex);
        }
    }

    private string GetInstalledRegistryPath()
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_instancePath)))[..12];
        return Path.Combine(ZenithPaths.AppDataDir, "cache", "installed", $"{hash}.json");
    }

    private Dictionary<string, string> LoadInstalledMap()
    {
        var path = GetInstalledRegistryPath();
        if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(StringComparer.OrdinalIgnoreCase); } catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    [RelayCommand]
    private void CancelDelete() => PendingDelete = null;

    [RelayCommand]
    private void OpenProjectPage(ModrinthProject project)
    {
        SelectedProject = project;
    }

    /// <summary>Opens the full project details page by Modrinth project ID (used by installed-content double-click).</summary>
    public async Task OpenProjectByIdAsync(string projectId)
    {
        try
        {
            FullProjectBusy = true;
            var full = await ModrinthApiService.GetFullProjectAsync(projectId);
            if (full == null) return;

            var project = new ModrinthProject
            {
                ProjectId = full.Id,
                Title = full.Title,
                Description = full.Description,
                Slug = full.Slug,
                Author = full.Author,
                Downloads = full.Downloads,
                IconUrl = full.IconUrl,
                GameVersions = full.GameVersions,
                Loaders = full.Loaders,
                Categories = full.Categories,
                ProjectType = full.ProjectType,
                Updated = full.DateModified
            };
            // Cache icon
            if (!string.IsNullOrWhiteSpace(full.IconUrl))
                _ = LoadIconAsync(project);

            SelectedProject = project;
            FullProject = full;
            OnPropertyChanged(nameof(CleanBody));
            _allVersions = await ModrinthApiService.GetProjectVersionsAsync(projectId);
            SelectedCompatibilityVersion = _gameVersion;
            OnPropertyChanged(nameof(VersionList));
            OnPropertyChanged(nameof(CompatibilityVersions));
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to open project by ID {projectId}", ex);
        }
        finally
        {
            FullProjectBusy = false;
        }
    }

    private async Task LoadIconAsync(ModrinthProject project)
    {
        if (string.IsNullOrWhiteSpace(project.IconUrl) || project.HasIcon) return;
        var bytes = await ModrinthApiService.GetIconAsync(project.IconUrl);
        project.SetIcon(bytes);
    }

    [RelayCommand]
    private void CloseDetails()
    {
        SelectedProject = null;
        PendingDelete = null;
    }

    [RelayCommand]
    private void OpenExternal(ModrinthProject? project)
    {
        if (string.IsNullOrWhiteSpace(project?.ProjectUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = project!.ProjectUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to open project page for {project.Title}", ex);
        }
    }

    [RelayCommand]
    private void OpenLink(string? linkType)
    {
        if (FullProject == null) return;
        var url = linkType?.ToLowerInvariant() switch
        {
            "source" => FullProject.SourceUrl,
            "issues" => FullProject.IssuesUrl,
            "wiki" => FullProject.WikiUrl,
            "discord" => FullProject.DiscordUrl,
            _ => ""
        };
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to open link: {url}", ex);
        }
    }

    /// <summary>Main action button: install new or reinstall selected/current version.</summary>
    [RelayCommand]
    private void MainAction()
    {
        if (SelectedProject == null) return;
        // If a specific version is selected in the Versions tab, install that
        if (SelectedVersionForDetails != null)
        {
            InstallSelectedVersion(SelectedVersionForDetails);
            return;
        }
        // Otherwise reinstall: open version selector to pick a version
        OpenVersionSelector(SelectedProject);
    }

    [RelayCommand]
    private void OpenVersionSelector(ModrinthProject? project)
    {
        if (project == null) return;
        SelectedProject = project;
        if (FullProject == null || FullProject.Id != project.ProjectId)
            _ = LoadFullProjectAsync(project);
        IsVersionSelectorOpen = true;
    }

    private async Task LoadFullProjectAsync(ModrinthProject project)
    {
        FullProjectBusy = true;
        try
        {
            var full = await ModrinthApiService.GetFullProjectAsync(project.ProjectId);
            FullProject = full;
            OnPropertyChanged(nameof(CleanBody));
            if (full != null)
            {
                _allVersions = await ModrinthApiService.GetProjectVersionsAsync(project.ProjectId);
                // Default: select the instance MC version in Compatibility sidebar
                SelectedCompatibilityVersion = _gameVersion;
                OnPropertyChanged(nameof(VersionList));
                OnPropertyChanged(nameof(VersionSelectorProject));
                OnPropertyChanged(nameof(CompatibilityVersions));
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to load full project for {project.Title}", ex);
        }
        finally
        {
            FullProjectBusy = false;
        }
    }

    [RelayCommand]
    private void ConfirmDelete()
    {
        var project = PendingDelete;
        if (project == null) return;
        PendingDelete = null;
        _ = DeleteAsync(project);
    }

    private async Task DeleteAsync(ModrinthProject project)
    {
        if (project == null) return;
        try
        {
            var targetFolder = SelectedContentType.InstallFolder(_instancePath, SelectedWorld);
            var deleted = false;

            // 1) Exact delete via the project-ID registry (most accurate).
            var map = LoadInstalledMap();
            if (map.TryGetValue(project.ProjectId, out var savedFile))
            {
                if (DeleteResolved(Path.Combine(targetFolder, savedFile), ref deleted)) { }
                map.Remove(project.ProjectId);
                SaveInstalledMap(map);
            }

            // 2) Fallback: delete any file/folder matching the slug.
            if (!deleted)
            {
                var slugNorm = project.Slug?.ToLowerInvariant().Replace("-", "").Replace("_", "") ?? "";
                if (Directory.Exists(targetFolder) && !string.IsNullOrWhiteSpace(slugNorm) && slugNorm.Length > 3)
                {
                    foreach (var f in Directory.GetFiles(targetFolder))
                    {
                        var baseName = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        if (baseName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                            baseName = Path.GetFileNameWithoutExtension(baseName);
                        var fNorm = baseName.Replace("-", "").Replace("_", "");
                        if (fNorm == slugNorm || fNorm.StartsWith(slugNorm)) { SafeDeleteFile(f); deleted = true; }
                    }
                    foreach (var d in Directory.GetDirectories(targetFolder))
                    {
                        var baseName = Path.GetFileName(d).ToLowerInvariant();
                        if (baseName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                            baseName = baseName[..^".disabled".Length];
                        var dNorm = baseName.Replace("-", "").Replace("_", "");
                        if (dNorm == slugNorm || dNorm.StartsWith(slugNorm)) { SafeDeleteDir(d); deleted = true; }
                    }
                }
            }

            project.IsInstalled = deleted;
            if (deleted)
            {
                StatusText = string.Format(L10n.T("cb_deleted"), project.Title);
                UpdateInstalledFlags();
                _refreshAll?.Invoke();
                _reportStatus?.Invoke(StatusText);
            }
            else
            {
                StatusText = string.Format(L10n.T("cb_delete_not_found"), project.Title);
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Content delete failed for {project.Title}", ex);
            StatusText = string.Format(L10n.T("mp_install_failed"), ex.Message);
        }
    }

    private bool DeleteResolved(string path, ref bool deleted)
    {
        if (File.Exists(path)) { SafeDeleteFile(path); deleted = true; return true; }
        if (Directory.Exists(path)) { SafeDeleteDir(path); deleted = true; return true; }
        var disabled = path + ".disabled";
        if (File.Exists(disabled)) { SafeDeleteFile(disabled); deleted = true; return true; }
        if (Directory.Exists(disabled)) { SafeDeleteDir(disabled); deleted = true; return true; }
        return false;
    }

    private static void SafeDeleteFile(string path)
    {
        try { File.Delete(path); } catch (Exception ex) { LauncherLog.Error($"Failed to delete file {path}", ex); }
    }

    private static void SafeDeleteDir(string path)
    {
        try { Directory.Delete(path, true); } catch (Exception ex) { LauncherLog.Error($"Failed to delete directory {path}", ex); }
    }

    private void SaveInstalledMap(Dictionary<string, string> map)
    {
        var path = GetInstalledRegistryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void MarkInstalled(string projectId, string fileName)
    {
        var map = LoadInstalledMap();
        map[projectId] = fileName;
        SaveInstalledMap(map);
    }

    private bool IsMarkedInstalled(string projectId)
    {
        var map = LoadInstalledMap();
        if (!map.TryGetValue(projectId, out var savedFile)) return false;
        var targetFolder = SelectedContentType.InstallFolder(_instancePath, SelectedWorld);
        var fullPath = Path.Combine(targetFolder, savedFile);
        if (File.Exists(fullPath) || Directory.Exists(fullPath)) return true;
        // Also check .disabled variant
        if (File.Exists(fullPath + ".disabled") || Directory.Exists(fullPath + ".disabled")) return true;
        map.Remove(projectId);
        SaveInstalledMap(map);
        return false;
    }

    private void UpdateInstalledFlags()
    {
        var targetFolder = SelectedContentType.InstallFolder(_instancePath, SelectedWorld);
        if (!Directory.Exists(targetFolder))
        {
            foreach (var p in Results) p.IsInstalled = false;
            return;
        }
        HashSet<string> installedLower;
        try
        {
            var files = Directory.GetFiles(targetFolder).Select(f => Path.GetFileName(f).ToLowerInvariant()).ToList();
            var dirs = Directory.GetDirectories(targetFolder).Select(d => Path.GetFileName(d).ToLowerInvariant()).ToList();
            installedLower = new HashSet<string>(files.Concat(dirs), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return;
        }

        var installedMap = LoadInstalledMap();
        foreach (var p in Results)
        {
            // Exact check via project ID registry (most accurate, no false positives)
            if (installedMap.TryGetValue(p.ProjectId, out var savedFile))
            {
                var fullPath = Path.Combine(targetFolder, savedFile);
                if (File.Exists(fullPath) || Directory.Exists(fullPath) || File.Exists(fullPath + ".disabled"))
                {
                    p.IsInstalled = true;
                    continue;
                }
                // Also check by exact file name if saved file was renamed
                var savedLower = savedFile.ToLowerInvariant();
                if (installedLower.Contains(savedLower) || installedLower.Contains(savedLower + ".disabled"))
                {
                    p.IsInstalled = true;
                    continue;
                }
            }

            // Fallback: exact slug check (no first-word contains) — file must equal slug or start with slug
            var slugNorm = p.Slug?.ToLowerInvariant().Replace("-", "").Replace("_", "") ?? "";
            var slugHyphenNorm = p.Slug?.ToLowerInvariant().Replace("-", "").Replace("_", "") ?? "";
            bool isInstalled = false;
            if (!string.IsNullOrWhiteSpace(slugNorm) && slugNorm.Length > 3)
            {
                isInstalled = installedLower.Any(f =>
                {
                    var baseName = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (baseName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        baseName = Path.GetFileNameWithoutExtension(baseName);
                    var fNorm = baseName.Replace("-", "").Replace("_", "");
                    return fNorm == slugNorm || fNorm == slugHyphenNorm || fNorm.StartsWith(slugNorm) || fNorm.StartsWith(slugHyphenNorm);
                });
            }
            p.IsInstalled = isInstalled;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}

public partial class BrowserTag : ObservableObject
{
    public string Name { get; init; } = "";
    public bool IsReset { get; init; }
    [ObservableProperty]
    private bool _isSelected;
}
