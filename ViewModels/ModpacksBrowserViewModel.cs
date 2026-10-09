using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CmlLib.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Common.Utils;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;
using CustomMcLauncher.ViewModels.Items;

namespace CustomMcLauncher.ViewModels;

public partial class ModpacksBrowserViewModel : ObservableObject
{
    private const int PageSize = 20;
    private readonly IInstanceService _instanceService;
    private readonly ModLoaderService _modLoaderService;
    private readonly Action? _onInstalled;
    private int _offset;
    private int _totalHits;
    private bool _showInstallFeedback;

    public ObservableCollection<ModrinthProjectItemViewModel> Results { get; } = new();
    public ObservableCollection<string> AvailableVersions { get; } = new();
    [ObservableProperty] private ObservableCollection<string> _availableCategories = new();
    [ObservableProperty] private ObservableCollection<string> _availableLoaders = new();
    [ObservableProperty] private ObservableCollection<string> _availableSortOptions = new() { "Relevance", "Downloads", "Newest", "Recently Updated" };

    [ObservableProperty] private string _searchQuery = "";

    private CancellationTokenSource? _searchCts;
    private int _searchGeneration;

    public Action<ModrinthProjectItemViewModel?>? OnProjectSelectedChanged { get; set; }

    partial void OnSearchQueryChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        _ = DebouncedSearchAsync(token);
    }

    private async Task DebouncedSearchAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token);
            if (token.IsCancellationRequested) return;
            await ReloadAsync(token);
        }
        catch (OperationCanceledException) { }
    }
    [ObservableProperty] private string _selectedVersion = "";
    [ObservableProperty] private string _selectedCategory = "";
    [ObservableProperty] private string _selectedLoader = "";
    [ObservableProperty] private string _selectedSort = "Relevance";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isLoadingMore;

    [ObservableProperty] private ModrinthProjectItemViewModel? _pendingDelete;

    [ObservableProperty] private ModrinthProjectItemViewModel? _selectedProject;

    public bool IsDetailsOpen => SelectedProject != null;

    public ModrinthProjectItemViewModel? DetailsProject => SelectedProject;

    public bool InstallButtonVisible => SelectedProject != null;

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

    partial void OnSelectedVersionForDetailsChanged(Models.ModrinthProjectVersion? value)
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
            if (!string.IsNullOrWhiteSpace(SelectedVersion) && !list.Contains(SelectedVersion, StringComparer.OrdinalIgnoreCase))
                list.Add(SelectedVersion);
            return list.OrderByDescending(v => ParseVersionString(v)).ToList();
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
                : SelectedVersion;
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

    // --- Reinstall confirmation modal ---
    [ObservableProperty]
    private bool _isReinstallConfirmOpen;

    [ObservableProperty]
    private bool _canConfirmReinstall;

    [ObservableProperty]
    private int _reinstallCountdownSeconds;

    [ObservableProperty]
    private string _reinstallConfirmTitle = "";

    [ObservableProperty]
    private string _reinstallConfirmMessage = "";

    private CancellationTokenSource? _reinstallCts;

    public string ReinstallConfirmButtonText => ReinstallCountdownSeconds > 0
        ? $"{L10n.T("mi_reinstall")} ({ReinstallCountdownSeconds})"
        : L10n.T("mi_reinstall");

    public Models.ModrinthFullProject? VersionSelectorProject => FullProject;

    [ObservableProperty]
    private bool _isDetailsInstallButtonHovered;

    partial void OnIsDetailsInstallButtonHoveredChanged(bool value)
    {
        OnPropertyChanged(nameof(ReinstallText));
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _installCts = new();

    public void CancelInstall(ModrinthProjectItemViewModel project)
    {
        if (_installCts.TryRemove(project.ProjectId, out var cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { }
        }
        project.IsInstalling = false;
        StatusText = "Installation cancelled.";
        OnPropertyChanged(nameof(ReinstallText));
    }

    /// <summary>Button text: "Install" for new content or different version, "Reinstall" only if the selected version is already installed.</summary>
    public string ReinstallText
    {
        get
        {
            if (_showInstallFeedback) return "Installed";
            if (SelectedProject is { IsInstalling: true })
            {
                return IsDetailsInstallButtonHovered ? L10n.T("btn_cancel") : L10n.T("mi_installing");
            }
            if (SelectedProject == null || !SelectedProject.IsInstalled) return L10n.T("mi_install");

            var instances = _instanceService.GetInstances();
            var slug = SelectedProject.Slug?.ToLowerInvariant() ?? "";
            var title = SelectedProject.Title.ToLowerInvariant();
            var projectId = SelectedProject.ProjectId;

            bool IsMatchingInstance(Models.InstanceModel inst)
            {
                return (!string.IsNullOrEmpty(inst.ModpackId) && (inst.ModpackId.Equals(projectId, StringComparison.OrdinalIgnoreCase) || inst.ModpackId.Equals(slug, StringComparison.OrdinalIgnoreCase)))
                    || inst.Name.Equals(title, StringComparison.OrdinalIgnoreCase)
                    || inst.Name.Replace(" ", "-").Equals(slug, StringComparison.OrdinalIgnoreCase)
                    || (inst.Name.ToLowerInvariant().Contains(slug) && slug.Length > 3);
            }

            if (SelectedVersionForDetails != null)
            {
                var verNum = SelectedVersionForDetails.VersionNumber;
                var verId = SelectedVersionForDetails.Id;
                bool isVersionInstalled = instances.Any(i => IsMatchingInstance(i) &&
                    (!string.IsNullOrEmpty(i.ModpackVersion) &&
                     (string.Equals(i.ModpackVersion, verNum, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(i.ModpackVersion, verId, StringComparison.OrdinalIgnoreCase))));

                return isVersionInstalled ? L10n.T("mi_reinstall") : L10n.T("mi_install");
            }

            // If no specific version selected, check if any instance has an installed version recorded
            if (!string.IsNullOrEmpty(SelectedProject.InstalledVersion))
            {
                return L10n.T("mi_reinstall");
            }

            return L10n.T("mi_reinstall");
        }
    }

    private ModrinthProjectItemViewModel? _prevSelectedProject;
    private System.ComponentModel.PropertyChangedEventHandler? _selectedProjectPropChanged;

    partial void OnSelectedProjectChanged(ModrinthProjectItemViewModel? value)
    {
        if (_prevSelectedProject != null && _selectedProjectPropChanged != null)
        {
            _prevSelectedProject.PropertyChanged -= _selectedProjectPropChanged;
        }
        _prevSelectedProject = value;
        if (value != null)
        {
            _selectedProjectPropChanged = (s, e) =>
            {
                if (e.PropertyName is nameof(ModrinthProjectItemViewModel.IsInstalling) or nameof(ModrinthProjectItemViewModel.IsInstalled))
                {
                    OnPropertyChanged(nameof(ReinstallText));
                }
            };
            value.PropertyChanged += _selectedProjectPropChanged;
        }

        OnPropertyChanged(nameof(IsDetailsOpen));
        OnPropertyChanged(nameof(DetailsProject));
        OnPropertyChanged(nameof(InstallButtonVisible));
        OnPropertyChanged(nameof(ReinstallText));
        SelectedVersionForDetails = null;
        SelectedCompatibilityVersion = "";
        if (value != null) _ = LoadFullProjectAsync(value);
        else FullProject = null;
        OnProjectSelectedChanged?.Invoke(value);
    }

    [RelayCommand]
    private void CloseVersionSelector()
    {
        IsVersionSelectorOpen = false;
        SelectedVersionToInstall = null;
    }

    private void FlashInstalledFeedback()
    {
        _showInstallFeedback = true;
        OnPropertyChanged(nameof(ReinstallText));
        _ = Task.Delay(2000).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _showInstallFeedback = false;
                OnPropertyChanged(nameof(ReinstallText));
            });
        });
    }

    public bool IsDeleteConfirmOpen => PendingDelete != null;

    public string DeleteItemName => PendingDelete?.Title ?? "";

    public string DeleteConfirmText =>
        PendingDelete == null ? "" : string.Format(L10n.T("cb_delete_confirm"), PendingDelete.Title);

    partial void OnPendingDeleteChanged(ModrinthProjectItemViewModel? value)
    {
        OnPropertyChanged(nameof(IsDeleteConfirmOpen));
        OnPropertyChanged(nameof(DeleteItemName));
    }

    public bool IsEmpty => Results.Count == 0 && !IsBusy && !IsLoadingMore;
    public bool HasMore => _offset < _totalHits;

    public ModpacksBrowserViewModel(IInstanceService instanceService, ModLoaderService modLoaderService, Action? onInstalled = null)
    {
        _instanceService = instanceService;
        _modLoaderService = modLoaderService;
        _onInstalled = onInstalled;
        AvailableCategories = new ObservableCollection<string>(new[] { "", "adventure", "cursed", "magic", "technology", "exploration", "optimization" });
        RefreshAvailableLoaders();
        _ = LoadVersionsAsync();
        _ = ReloadAsync();
    }

    /// <summary>
    /// Rebuilds converter-driven collections so ComboBox items re-evaluate
    /// their display converters with the current language.
    /// Called by MainWindowViewModel.OnSelectedLanguageChanged.
    /// </summary>
    public void RefreshTranslations()
    {
        AvailableCategories = new ObservableCollection<string>(new[] { "", "adventure", "cursed", "magic", "technology", "exploration", "optimization" });
        AvailableSortOptions = new ObservableCollection<string>(new[] { "Relevance", "Downloads", "Newest", "Recently Updated" });
        RefreshAvailableLoaders();
    }

    /// <summary>
    /// Builds the version list from real Modrinth modpacks: unions the "versions"
    /// arrays of the most-downloaded modpacks so the dropdown only contains
    /// stable releases that actually have at least one modpack (no snapshots,
    /// pre-releases or empty versions).
    /// </summary>
    private async Task LoadVersionsAsync()
    {
        try
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Top pages by downloads cover virtually every version that has a modpack.
            foreach (var offset in new[] { 0, 100, 200 })
            {
                var page = await ModrinthApiService.SearchAsync("", "", Array.Empty<string>(), "modpack", "downloads", offset, 100);
                if (page == null || page.Hits.Count == 0) break;
                foreach (var hit in page.Hits)
                    foreach (var v in hit.GameVersions)
                        if (IsStableRelease(v))
                            found.Add(v);
                if (page.Offset + page.Hits.Count >= page.TotalHits) break;
            }

            if (found.Count == 0) throw new Exception("empty");

            var sorted = found.OrderByDescending(ParseVersion).ToList();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AvailableVersions.Clear();
                AvailableVersions.Add("");
                foreach (var v in sorted)
                    AvailableVersions.Add(v);
                if (string.IsNullOrEmpty(SelectedVersion) && AvailableVersions.Count > 1)
                    SelectedVersion = "";
            });
        }
        catch
        {
            try
            {
                var path = new MinecraftPath(ZenithPaths.AppDataDir);
                var launcher = new MinecraftLauncher(path);
                var versions = await LaunchService.WithManifestRetryAsync(() => launcher.GetAllVersionsAsync().AsTask());
                var releases = versions
                    .Where(v => v.Type == "release")
                    .Select(v => v.Name)
                    .Where(IsStableRelease)
                    .Distinct()
                    .OrderByDescending(ParseVersion)
                    .ToList();
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    AvailableVersions.Clear();
                    AvailableVersions.Add("");
                    foreach (var v in releases) AvailableVersions.Add(v);
                });
            }
            catch
            {
                AvailableVersions.Clear();
                AvailableVersions.Add("");
                AvailableVersions.Add("1.21.1");
                AvailableVersions.Add("1.20.1");
                AvailableVersions.Add("1.19.4");
                AvailableVersions.Add("1.18.2");
                AvailableVersions.Add("1.16.5");
                AvailableVersions.Add("1.12.2");
                AvailableVersions.Add("1.7.10");
            }
        }
    }

    private static bool IsStableRelease(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        // Only numeric releases ("1.x", "1.x.y" and the new "26.x"/"26.x.y" scheme) —
        // excludes snapshots (24w14a), pre-releases and rc builds.
        return System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+(\.\d+)?$");
    }

    private static long ParseVersion(string version)
    {
        // Packs to a comparable number: 1.21.4 -> 1_021_004 style key.
        var parts = version.Split('.');
        long key = 0;
        for (int i = 0; i < 3; i++)
        {
            key *= 1000;
            if (i < parts.Length && long.TryParse(parts[i], out var n))
                key += Math.Min(n, 999);
        }
        return key;
    }

    partial void OnSelectedVersionChanged(string value)
    {
        RefreshAvailableLoaders();
        _ = ReloadAsync();
    }
    partial void OnSelectedCategoryChanged(string value) => _ = ReloadAsync();
    partial void OnSelectedLoaderChanged(string value) => _ = ReloadAsync();
    partial void OnSelectedSortChanged(string value) => _ = ReloadAsync();

    /// <summary>
    /// Loader availability follows Minecraft history so ancient versions never
    /// offer loaders that did not exist yet (e.g. no Fabric for 1.5.2).
    /// Forge exists since 1.1, Fabric/Quilt since 1.14, NeoForge since 1.20.
    /// </summary>
    private void RefreshAvailableLoaders()
    {
        long key = string.IsNullOrWhiteSpace(SelectedVersion) ? long.MaxValue : ParseVersion(SelectedVersion);
        var loaders = new List<string> { "" };
        if (key >= ParseVersion("1.1")) loaders.Add("forge");
        if (key >= ParseVersion("1.14")) { loaders.Add("fabric"); loaders.Add("quilt"); }
        if (key >= ParseVersion("1.20")) loaders.Add("neoforge");
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AvailableLoaders.Clear();
            foreach (var l in loaders) AvailableLoaders.Add(l);
            if (!AvailableLoaders.Contains(SelectedLoader))
                SelectedLoader = "";
            // Re-push after the ItemsSource reset or the closed combo renders blank.
            OnPropertyChanged(nameof(SelectedLoader));
        });
    }

    private string[] LoaderFacets =>
        string.IsNullOrWhiteSpace(SelectedLoader) ? Array.Empty<string>() : new[] { SelectedLoader.Trim().ToLowerInvariant() };

    [RelayCommand]
    public void ClearSearch()
    {
        SearchQuery = "";
    }

    [RelayCommand]
    public void ResetFilters()
    {
        SearchQuery = "";
        SelectedVersion = "";
        SelectedCategory = "";
        SelectedLoader = "";
        SelectedSort = "Relevance";
        _ = ReloadAsync();
    }

    [RelayCommand]
    public Task Search()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        return ReloadAsync(_searchCts.Token);
    }

    public async Task ReloadAsync(CancellationToken token = default)
    {
        var gen = Interlocked.Increment(ref _searchGeneration);
        IsBusy = true;
        StatusText = "";
        foreach (var r in Results) { try { r.Icon?.Dispose(); } catch { } } Results.Clear();
        _offset = 0;
        _totalHits = 0;
        try
        {
            var sort = SelectedSort switch
            {
                "Downloads" => "downloads",
                "Newest" => "newest",
                "Recently Updated" => "updated",
                _ => "relevance"
            };
            var catSlug = SelectedCategory == "exploration" ? "adventure" : SelectedCategory;
            var categoryFilter = string.IsNullOrWhiteSpace(catSlug) ? Array.Empty<string>() : new[] { catSlug };
            var page = await ModrinthApiService.SearchAsync(SearchQuery, SelectedVersion, LoaderFacets, "modpack", sort, categoryFilter, 0, PageSize, token);
            if (token.IsCancellationRequested || gen != _searchGeneration) return;
            if (page == null)
            {
                StatusText = L10n.T("mp_status_unreachable");
                return;
            }
            _totalHits = page.TotalHits;
            _offset = page.Limit;
            foreach (var p in page.Hits)
            {
                Results.Add(p);
                LoadIcon(p, gen);
            }
            UpdateInstalledFlags();
            StatusText = page.TotalHits == 0 ? L10n.T("mp_status_none") : string.Format(L10n.T("mp_status_count"), page.TotalHits);
        }
        catch (OperationCanceledException)
        {
            // Cancelled cleanly
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && gen == _searchGeneration)
            {
                LauncherLog.Error("Modpack search failed", ex);
                StatusText = string.Format(L10n.T("mp_status_failed"), ex.Message);
            }
        }
        finally
        {
            if (gen == _searchGeneration)
            {
                IsBusy = false;
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(HasMore));
            }
        }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (IsBusy || IsLoadingMore || !HasMore) return;
        IsLoadingMore = true;
        var gen = _searchGeneration;
        try
        {
            var catSlug = SelectedCategory == "exploration" ? "adventure" : SelectedCategory;
            var categoryFilter = string.IsNullOrWhiteSpace(catSlug) ? Array.Empty<string>() : new[] { catSlug };
            var sort = SelectedSort switch
            {
                "Downloads" => "downloads",
                "Newest" => "newest",
                "Recently Updated" => "updated",
                _ => "relevance"
            };
            var page = await ModrinthApiService.SearchAsync(SearchQuery, SelectedVersion, LoaderFacets, "modpack", sort, categoryFilter, _offset, PageSize);
            if (page != null && gen == _searchGeneration)
            {
                if (page.Hits.Count == 0)
                {
                    _offset = _totalHits;
                }
                else
                {
                    _offset += page.Limit;
                    _totalHits = page.TotalHits;
                    var existing = Results.Select(r => r.ProjectId).ToHashSet();
                    foreach (var p in page.Hits)
                    {
                        if (existing.Add(p.ProjectId))
                        {
                            Results.Add(p);
                            LoadIcon(p, gen);
                        }
                    }
                    UpdateInstalledFlags();
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Modpack load-more failed", ex);
        }
        finally
        {
            if (gen == _searchGeneration)
            {
                IsLoadingMore = false;
                OnPropertyChanged(nameof(HasMore));
            }
        }
    }

    private async void LoadIcon(ModrinthProjectItemViewModel project, int generation)
    {
        if (string.IsNullOrWhiteSpace(project.IconUrl) || project.HasIcon) return;
        var bytes = await ModrinthApiService.GetIconAsync(project.IconUrl);
        if (generation == _searchGeneration)
            project.SetIcon(bytes);
    }

    public void UpdateInstalledFlags()
    {
        var instances = _instanceService.GetInstances();
        var instanceNames = instances.Select(i => i.Name.ToLowerInvariant()).ToHashSet();
        var instanceSlugs = instances.Select(i => i.Name.ToLowerInvariant().Replace(" ", "-")).ToHashSet();
        foreach (var p in Results)
        {
            var slug = p.Slug?.ToLowerInvariant() ?? "";
            var title = p.Title.ToLowerInvariant();
            bool isInstalled = instanceNames.Contains(title) || instanceSlugs.Contains(slug) || instanceNames.Any(n => n == slug) || instanceNames.Any(n => n.Replace(" ", "-") == slug);
            // Also check exact project ID if we had mapping, but for modpacks we use name/slug
            // Fallback: check if any instance was created from this modpack via icon or name
            if (!isInstalled)
            {
                // Check if any instance has icon that matches modpack icon (not reliable)
                // For now, check if any instance name contains slug
                isInstalled = instanceNames.Any(n => n.Contains(slug) && slug.Length > 3);
                if (isInstalled && slug.Length < 5) isInstalled = false; // avoid false positive for short slugs
            }
            if (isInstalled)
            {
                var matching = instances.FirstOrDefault(i =>
                    (!string.IsNullOrEmpty(i.ModpackId) && (i.ModpackId.Equals(p.ProjectId, StringComparison.OrdinalIgnoreCase) || i.ModpackId.Equals(slug, StringComparison.OrdinalIgnoreCase)))
                    || i.Name.Equals(title, StringComparison.OrdinalIgnoreCase)
                    || i.Name.Replace(" ", "-").Equals(slug, StringComparison.OrdinalIgnoreCase)
                    || (i.Name.ToLowerInvariant().Contains(slug) && slug.Length > 3));

                if (matching != null)
                {
                    p.InstalledGameVersion = matching.Version;
                    p.InstalledVersion = matching.ModpackVersion;
                }
            }
            p.IsInstalled = isInstalled;
        }
    }

    [RelayCommand]
    private void DeleteOrInstall(ModrinthProjectItemViewModel project)
    {
        if (project == null) return;
        if (project.IsInstalling)
        {
            CancelInstall(project);
            return;
        }
        if (project.IsInstalled)
        {
            PendingDelete = project;
            return;
        }
        // Install directly with the latest version (no page navigation)
        _ = InstallLatestAsync(project);
    }

    private async Task InstallLatestAsync(ModrinthProjectItemViewModel project)
    {
        if (project == null || project.IsInstalling) return;
        var cts = new CancellationTokenSource();
        _installCts[project.ProjectId] = cts;
        var token = cts.Token;
        try
        {
            var version = await ModrinthApiService.GetLatestVersionAsync(project.ProjectId, SelectedVersion, Array.Empty<string>(), token);
            if (version == null)
            {
                StatusText = $"No compatible version found for {project.Title}";
                return;
            }

            var file = version.FileUrl;
            if (string.IsNullOrWhiteSpace(file)) return;

            project.IsInstalling = true;
            OnPropertyChanged(nameof(ReinstallText));
            StatusText = "Installing...";

            var tempDir = Path.Combine(Path.GetTempPath(), $"zenith_modpack_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, version.FileName);
            var data = await ModrinthApiService.DownloadAsync(file, token);
            if (token.IsCancellationRequested) return;
            if (data is not { Length: > 0 })
            {
                StatusText = "Download failed.";
                return;
            }
            await File.WriteAllBytesAsync(zipPath, data, token);
            var instance = await InstallModpackFromZipAsync(project, zipPath, tempDir, version.VersionNumber, token);
            if (instance != null)
            {
                project.IsInstalled = true;
                StatusText = $"Installed {project.Title}";
                UpdateInstalledFlags();
                _onInstalled?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Installation cancelled.";
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to quick-install modpack {project.Title}", ex);
            StatusText = $"Failed to install: {ex.Message}";
        }
        finally
        {
            if (_installCts.TryRemove(project.ProjectId, out var removedCts))
            {
                removedCts.Dispose();
            }
            project.IsInstalling = false;
            OnPropertyChanged(nameof(ReinstallText));
        }
    }

    [RelayCommand]
    private void CancelDelete() => PendingDelete = null;

    [RelayCommand]
    private void OpenProjectPage(ModrinthProjectItemViewModel project)
    {
        SelectedProject = project;
    }

    [RelayCommand]
    private void CloseDetails()
    {
        SelectedProject = null;
        PendingDelete = null;
    }

    [RelayCommand]
    private void OpenVersionSelector(ModrinthProjectItemViewModel? project)
    {
        if (project == null) return;
        SelectedProject = project;
        if (FullProject == null || FullProject.Id != project.ProjectId)
            _ = LoadFullProjectAsync(project);
        IsVersionSelectorOpen = true;
    }

    private async Task LoadFullProjectAsync(ModrinthProjectItemViewModel project)
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
                SelectedCompatibilityVersion = SelectedVersion;
                OnPropertyChanged(nameof(VersionList));
                OnPropertyChanged(nameof(VersionSelectorProject));
                OnPropertyChanged(nameof(CompatibilityVersions));

                if (SelectedVersionForDetails == null && VersionList.Count > 0)
                    SelectedVersionForDetails = VersionList[0];
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
    private void InstallSelectedVersion(Models.ModrinthProjectVersion? version)
    {
        if (version == null || SelectedProject == null) return;
        IsVersionSelectorOpen = false;
        _ = InstallSpecificVersionAsync(SelectedProject, version);
    }

    private async Task InstallSpecificVersionAsync(ModrinthProjectItemViewModel project, Models.ModrinthProjectVersion version)
    {
        if (project == null || project.IsInstalling) return;
        var file = version.Files.FirstOrDefault(f => f.IsPrimary) ?? version.Files.FirstOrDefault();
        if (file == null || string.IsNullOrWhiteSpace(file.Url)) return;
        var cts = new CancellationTokenSource();
        _installCts[project.ProjectId] = cts;
        var token = cts.Token;
        try
        {
            project.IsInstalling = true;
            OnPropertyChanged(nameof(ReinstallText));
            StatusText = "Installing...";
            var tempDir = Path.Combine(Path.GetTempPath(), $"zenith_modpack_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, file.Filename);
            var data = await ModrinthApiService.DownloadAsync(file.Url, token);
            if (token.IsCancellationRequested) return;
            if (data is not { Length: > 0 })
            {
                StatusText = "Download failed.";
                return;
            }
            await File.WriteAllBytesAsync(zipPath, data, token);
            var instance = await InstallModpackFromZipAsync(project, zipPath, tempDir, version.VersionNumber, token);
            if (instance != null)
            {
                project.IsInstalled = true;
                StatusText = $"Installed {project.Title}";
                UpdateInstalledFlags();
                _onInstalled?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Installation cancelled.";
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to install modpack {project.Title}", ex);
            StatusText = $"Failed to install: {ex.Message}";
        }
        finally
        {
            if (_installCts.TryRemove(project.ProjectId, out var removedCts))
            {
                removedCts.Dispose();
            }
            project.IsInstalling = false;
            OnPropertyChanged(nameof(ReinstallText));
        }
    }

    private async Task<Models.InstanceModel?> InstallModpackFromZipAsync(ModrinthProjectItemViewModel project, string zipPath, string tempDir, string installedVersion = "", CancellationToken token = default)
    {
        try
        {
            var extractDir = Path.Combine(tempDir, "extracted");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);

            // 1. Check for Modrinth index (modrinth.index.json)
            var modrinthIndexPath = Path.Combine(extractDir, "modrinth.index.json");
            if (!File.Exists(modrinthIndexPath))
            {
                var indexFiles = Directory.GetFiles(extractDir, "modrinth.index.json", SearchOption.AllDirectories);
                if (indexFiles.Length > 0) modrinthIndexPath = indexFiles[0];
            }

            // 2. Check for CurseForge manifest (manifest.json)
            var curseforgeManifestPath = Path.Combine(extractDir, "manifest.json");
            if (!File.Exists(curseforgeManifestPath))
            {
                var manifestFiles = Directory.GetFiles(extractDir, "manifest.json", SearchOption.AllDirectories);
                if (manifestFiles.Length > 0) curseforgeManifestPath = manifestFiles[0];
            }

            if (File.Exists(modrinthIndexPath))
            {
                return await InstallModrinthMrpackAsync(project, modrinthIndexPath, extractDir, installedVersion, token);
            }
            else if (File.Exists(curseforgeManifestPath))
            {
                return await InstallCurseforgePackAsync(project, curseforgeManifestPath, extractDir, installedVersion, token);
            }
            else
            {
                StatusText = "Invalid modpack: neither modrinth.index.json nor manifest.json found.";
                return null;
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to install modpack from zip", ex);
            StatusText = $"Failed to install: {ex.Message}";
            return null;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private async Task<Models.InstanceModel?> InstallModrinthMrpackAsync(ModrinthProjectItemViewModel project, string indexPath, string extractDir, string installedVersion = "", CancellationToken token = default)
    {
        var jsonText = await File.ReadAllTextAsync(indexPath, token);
        using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        // Name
        var packName = project.Title;
        if (root.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString()))
            packName = nameProp.GetString()!;
        if (string.IsNullOrWhiteSpace(packName))
            packName = project.Slug ?? "Modpack";

        // Dependencies: minecraft, loaders
        string mcVersion = "";
        string loaderType = "Fabric";
        string loaderVersion = "";

        if (root.TryGetProperty("dependencies", out var deps))
        {
            if (deps.TryGetProperty("minecraft", out var mv))
                mcVersion = mv.GetString() ?? "";

            if (deps.TryGetProperty("fabric-loader", out var fl))
            {
                loaderType = "Fabric";
                loaderVersion = fl.GetString() ?? "";
            }
            else if (deps.TryGetProperty("quilt-loader", out var ql))
            {
                loaderType = "Quilt";
                loaderVersion = ql.GetString() ?? "";
            }
            else if (deps.TryGetProperty("forge", out var fg))
            {
                loaderType = "Forge";
                loaderVersion = fg.GetString() ?? "";
            }
            else if (deps.TryGetProperty("neoforge", out var nf))
            {
                loaderType = "NeoForge";
                loaderVersion = nf.GetString() ?? "";
            }
        }

        if (string.IsNullOrWhiteSpace(mcVersion))
            mcVersion = !string.IsNullOrWhiteSpace(SelectedVersion) ? SelectedVersion : "1.20.1";

        // Unique instance name
        var safeName = string.Join("_", packName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Modpack";
        var allNames = _instanceService.GetInstances().Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var finalName = safeName;
        int num = 1;
        while (allNames.Contains(finalName))
        {
            finalName = $"{safeName} ({num++})";
        }

        StatusText = $"Creating instance {finalName}...";
        var inst = await _instanceService.CreateInstanceAsync(finalName, mcVersion, loaderType, loaderVersion);
        if (inst == null)
        {
            StatusText = $"Failed to create instance for {packName}";
            return null;
        }

        inst.ModpackId = project.ProjectId;
        inst.ModpackVersion = !string.IsNullOrWhiteSpace(installedVersion)
            ? installedVersion
            : (root.TryGetProperty("versionId", out var vidProp) ? vidProp.GetString() ?? "" : "");

        // Copy icon
        if (!string.IsNullOrWhiteSpace(project.IconUrl))
        {
            try
            {
                var iconBytes = await ModrinthApiService.GetIconAsync(project.IconUrl);
                if (iconBytes is { Length: > 0 })
                {
                    var iconPath = Path.Combine(inst.Path, "icon.png");
                    await File.WriteAllBytesAsync(iconPath, iconBytes, token);
                    inst.IconPath = iconPath;
                    await _instanceService.SaveInstanceAsync(inst);
                }
            }
            catch { }
        }

        // Copy overrides
        var baseDir = Path.GetDirectoryName(indexPath) ?? extractDir;
        var overridesDir = Path.Combine(baseDir, "overrides");
        if (Directory.Exists(overridesDir))
            CopyDirectoryRecursive(overridesDir, inst.Path);

        var clientOverridesDir = Path.Combine(baseDir, "client-overrides");
        if (Directory.Exists(clientOverridesDir))
            CopyDirectoryRecursive(clientOverridesDir, inst.Path);

        // Download files
        if (root.TryGetProperty("files", out var filesProp) && filesProp.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var files = filesProp.EnumerateArray().ToList();
            int total = files.Count;
            int done = 0;
            StatusText = $"Installing files (0/{total})...";

            using var sem = new System.Threading.SemaphoreSlim(6);
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ZenithLauncher/1.0");

            var tasks = files.Select(async file =>
            {
                await sem.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    // Check env client
                    if (file.TryGetProperty("env", out var env) && env.TryGetProperty("client", out var envClient))
                    {
                        if (envClient.GetString() == "unsupported") return;
                    }

                    if (!file.TryGetProperty("path", out var pathEl)) return;
                    var relPath = pathEl.GetString();
                    if (string.IsNullOrWhiteSpace(relPath)) return;

                    var destPath = Path.Combine(inst.Path, relPath.Replace('/', Path.DirectorySeparatorChar));
                    var dir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    if (!file.TryGetProperty("downloads", out var downloads) || downloads.GetArrayLength() == 0) return;

                    foreach (var dl in downloads.EnumerateArray())
                    {
                        token.ThrowIfCancellationRequested();
                        var url = dl.GetString();
                        if (string.IsNullOrWhiteSpace(url)) continue;
                        try
                        {
                            var data = await http.GetByteArrayAsync(url, token);
                            if (data.Length > 0)
                            {
                                await File.WriteAllBytesAsync(destPath, data, token);
                                break;
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch
                        {
                            // Try next fallback URL if available
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LauncherLog.Error($"Failed to download modpack file for {packName}", ex);
                }
                finally
                {
                    var count = System.Threading.Interlocked.Increment(ref done);
                    StatusText = $"Installing files ({count}/{total})...";
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        await _instanceService.SaveInstanceAsync(inst);
        return inst;
    }

    private async Task<Models.InstanceModel?> InstallCurseforgePackAsync(ModrinthProjectItemViewModel project, string manifestPath, string extractDir, string installedVersion = "", CancellationToken token = default)
    {
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        using var manifestDoc = System.Text.Json.JsonDocument.Parse(manifestJson);
        var manifest = manifestDoc.RootElement;

        var mcVersion = "";
        if (manifest.TryGetProperty("minecraft", out var mc) && mc.TryGetProperty("version", out var mv))
            mcVersion = mv.GetString() ?? "";

        if (string.IsNullOrWhiteSpace(mcVersion))
        {
            StatusText = "Invalid modpack: no Minecraft version found.";
            return null;
        }

        var packName = project.Title;
        if (manifest.TryGetProperty("name", out var np) && !string.IsNullOrWhiteSpace(np.GetString()))
            packName = np.GetString()!;
        if (string.IsNullOrWhiteSpace(packName))
            packName = project.Slug ?? "Modpack";

        var safeName = string.Join("_", packName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Modpack";
        var allNames = _instanceService.GetInstances().Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var finalName = safeName;
        int num = 1;
        while (allNames.Contains(finalName))
        {
            finalName = $"{safeName} ({num++})";
        }

        string loaderType = "Forge";
        string loaderVersion = "";
        if (manifest.TryGetProperty("minecraft", out var mc2) && mc2.TryGetProperty("modLoaders", out var mlList) && mlList.GetArrayLength() > 0)
        {
            var primaryLoader = mlList.EnumerateArray().FirstOrDefault(l => l.TryGetProperty("primary", out var prim) && prim.GetBoolean());
            if (primaryLoader.ValueKind == System.Text.Json.JsonValueKind.Undefined)
                primaryLoader = mlList.EnumerateArray().FirstOrDefault();

            if (primaryLoader.TryGetProperty("id", out var idEl))
            {
                var idStr = idEl.GetString() ?? "";
                if (idStr.Contains("fabric", StringComparison.OrdinalIgnoreCase))
                {
                    loaderType = "Fabric";
                    loaderVersion = idStr.Replace("fabric-", "", StringComparison.OrdinalIgnoreCase);
                }
                else if (idStr.Contains("quilt", StringComparison.OrdinalIgnoreCase))
                {
                    loaderType = "Quilt";
                    loaderVersion = idStr.Replace("quilt-", "", StringComparison.OrdinalIgnoreCase);
                }
                else if (idStr.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
                {
                    loaderType = "NeoForge";
                    loaderVersion = idStr.Replace("neoforge-", "", StringComparison.OrdinalIgnoreCase);
                }
                else if (idStr.Contains("forge", StringComparison.OrdinalIgnoreCase))
                {
                    loaderType = "Forge";
                    loaderVersion = idStr.Replace("forge-", "", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        StatusText = $"Creating instance {finalName}...";
        var inst = await _instanceService.CreateInstanceAsync(finalName, mcVersion, loaderType, loaderVersion);
        if (inst == null)
        {
            StatusText = $"Failed to create instance for {packName}";
            return null;
        }

        inst.ModpackId = project.ProjectId;
        inst.ModpackVersion = !string.IsNullOrWhiteSpace(installedVersion)
            ? installedVersion
            : (manifest.TryGetProperty("version", out var vProp) ? vProp.GetString() ?? "" : "");

        var baseDir = Path.GetDirectoryName(manifestPath) ?? extractDir;
        var overridesDir = Path.Combine(baseDir, "overrides");
        if (manifest.TryGetProperty("overrides", out var ovProp) && !string.IsNullOrWhiteSpace(ovProp.GetString()))
            overridesDir = Path.Combine(baseDir, ovProp.GetString()!);

        if (Directory.Exists(overridesDir))
            CopyDirectoryRecursive(overridesDir, inst.Path);

        await _instanceService.SaveInstanceAsync(inst);
        return inst;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    [RelayCommand]
    private void OpenExternal(ModrinthProjectItemViewModel? project)
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

        if (SelectedProject.IsInstalling)
        {
            CancelInstall(SelectedProject);
            return;
        }

        // Check if the current action is Reinstall
        bool isReinstall = ReinstallText == L10n.T("mi_reinstall");
        if (isReinstall)
        {
            StartReinstallConfirmation();
            return;
        }

        if (SelectedVersionForDetails != null)
        {
            InstallSelectedVersion(SelectedVersionForDetails);
            return;
        }
        OpenVersionSelector(SelectedProject);
    }

    private void StartReinstallConfirmation()
    {
        _reinstallCts?.Cancel();
        _reinstallCts?.Dispose();
        _reinstallCts = new CancellationTokenSource();
        var token = _reinstallCts.Token;

        var projTitle = SelectedProject?.Title ?? "Modpack";
        var verStr = SelectedVersionForDetails?.VersionNumber ?? SelectedProject?.InstalledVersion ?? "";
        ReinstallConfirmTitle = $"{L10n.T("mi_reinstall")}: {projTitle}";
        ReinstallConfirmMessage = !string.IsNullOrWhiteSpace(verStr)
            ? $"Files for {projTitle} ({verStr}) will be overwritten. Are you sure you want to reinstall?"
            : $"Files for {projTitle} will be overwritten. Are you sure you want to reinstall?";

        CanConfirmReinstall = false;
        ReinstallCountdownSeconds = 3;
        OnPropertyChanged(nameof(ReinstallConfirmButtonText));
        IsReinstallConfirmOpen = true;

        _ = Task.Run(async () =>
        {
            try
            {
                for (int s = 3; s > 0; s--)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ReinstallCountdownSeconds = s;
                        OnPropertyChanged(nameof(ReinstallConfirmButtonText));
                    });
                    await Task.Delay(1000, token);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ReinstallCountdownSeconds = 0;
                    CanConfirmReinstall = true;
                    OnPropertyChanged(nameof(ReinstallConfirmButtonText));
                });
            }
            catch (OperationCanceledException)
            {
                // Cancelled by user
            }
        }, token);
    }

    [RelayCommand]
    private void CancelReinstall()
    {
        _reinstallCts?.Cancel();
        _reinstallCts?.Dispose();
        _reinstallCts = null;
        IsReinstallConfirmOpen = false;
        CanConfirmReinstall = false;
        ReinstallCountdownSeconds = 0;
    }

    [RelayCommand]
    private void ConfirmReinstall()
    {
        if (!CanConfirmReinstall || SelectedProject == null) return;
        CancelReinstall();

        if (SelectedVersionForDetails != null)
        {
            InstallSelectedVersion(SelectedVersionForDetails);
            return;
        }
        OpenVersionSelector(SelectedProject);
    }

    [RelayCommand]
    private void ConfirmDelete()
    {
        var project = PendingDelete;
        if (project == null) return;
        PendingDelete = null;
        DeleteModpack(project);
    }

    private void DeleteModpack(ModrinthProjectItemViewModel project)
    {
        if (project == null) return;
        try
        {
            var slug = project.Slug?.ToLowerInvariant() ?? "";
            var title = project.Title.ToLowerInvariant();
            var instances = _instanceService.GetInstances();
            var matches = instances
                .Where(i =>
                {
                    var name = i.Name.ToLowerInvariant();
                    var nameSlug = name.Replace(" ", "-");
                    if (name == title || nameSlug == slug || name == slug) return true;
                    return name.Contains(slug) && slug.Length > 3;
                })
                .ToList();

            if (matches.Count == 0)
            {
                StatusText = string.Format(L10n.T("cb_delete_not_found"), project.Title);
                return;
            }

            foreach (var inst in matches)
                _instanceService.DeleteInstance(inst.Id);

            project.IsInstalled = false;
            StatusText = string.Format(L10n.T("cb_deleted"), project.Title);
            UpdateInstalledFlags();
            _onInstalled?.Invoke();
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Modpack delete failed for {project.Title}", ex);
            StatusText = string.Format(L10n.T("mp_install_failed"), ex.Message);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task InstallAsync(ModrinthProjectItemViewModel project)
    {
        await InstallLatestAsync(project);
    }
}




