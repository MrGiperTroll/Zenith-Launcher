using CustomMcLauncher.ViewModels.Dialogs;
using System.Text.RegularExpressions;
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

public partial class ModpacksBrowserViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private AuthorProfileViewModel? _currentAuthorProfileVm;

    partial void OnCurrentAuthorProfileVmChanging(AuthorProfileViewModel? value)
    {
        _currentAuthorProfileVm?.Dispose();
    }

    public bool IsAuthorProfileOpen => CurrentAuthorProfileVm != null;

    public Action<bool>? OnAuthorProfileChanged { get; set; }
    public Func<string>? GetCurseForgeApiKey { get; set; }

    [RelayCommand]
    public void OpenAuthorProfile(string? author)
    {
        if (string.IsNullOrWhiteSpace(author)) return;
        var vm = new AuthorProfileViewModel();
        vm.OnCloseRequested = () =>
        {
            CurrentAuthorProfileVm = null;
            OnPropertyChanged(nameof(IsAuthorProfileOpen));
            OnAuthorProfileChanged?.Invoke(false);
        };
        vm.OnOpenProjectRequested = project =>
        {
            CurrentAuthorProfileVm = null;
            OnPropertyChanged(nameof(IsAuthorProfileOpen));
            OnAuthorProfileChanged?.Invoke(false);
            OpenProjectPage(project);
        };
        CurrentAuthorProfileVm = vm;
        OnPropertyChanged(nameof(IsAuthorProfileOpen));
        OnAuthorProfileChanged?.Invoke(true);
        _ = vm.LoadAuthorAsync(author);
    }

    [RelayCommand]
    public void CloseAuthorProfile()
    {
        CurrentAuthorProfileVm?.Dispose();
        CurrentAuthorProfileVm = null;
        OnPropertyChanged(nameof(IsAuthorProfileOpen));
        OnAuthorProfileChanged?.Invoke(false);
    }

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

    [ObservableProperty] private string _selectedProjectType = "modpack";

    public bool IsModpacksTab => SelectedProjectType == "modpack";
    public bool IsModsTab => SelectedProjectType == "mod";
    public bool IsResourcePacksTab => SelectedProjectType == "resourcepack";
    public bool IsDataPacksTab => SelectedProjectType == "datapack";
    public bool IsShadersTab => SelectedProjectType == "shader";
    public bool IsServersTab => SelectedProjectType == "plugin";
    public bool IsWorldsTab => SelectedProjectType == "world";

    public string SearchPlaceholder => SelectedProjectType switch
    {
        "mod" => L10n.T("ph_search_mods"),
        "resourcepack" => L10n.T("ph_search_resourcepacks"),
        "datapack" => L10n.T("ph_search_datapacks"),
        "shader" => L10n.T("ph_search_shaders"),
        "plugin" => L10n.T("ph_search_plugins"),
        "world" => L10n.T("ph_search_worlds"),
        _ => L10n.T("ph_search_modpacks")
    };

    [RelayCommand]
    public void SelectProjectType(string type)
    {
        if (string.IsNullOrWhiteSpace(type) || SelectedProjectType == type) return;
        SelectedProjectType = type;
        OnPropertyChanged(nameof(IsModpacksTab));
        OnPropertyChanged(nameof(IsModsTab));
        OnPropertyChanged(nameof(IsResourcePacksTab));
        OnPropertyChanged(nameof(IsDataPacksTab));
        OnPropertyChanged(nameof(IsShadersTab));
        OnPropertyChanged(nameof(IsServersTab));
        OnPropertyChanged(nameof(IsWorldsTab));
        OnPropertyChanged(nameof(SearchPlaceholder));
        _ = Search();
    }

    [ObservableProperty] private string _searchQuery = "";

    [ObservableProperty]
    private ContentSource _selectedSource = ContentSource.Modrinth;

    public bool IsAllSource => SelectedSource == ContentSource.All;
    public bool IsModrinthSource => SelectedSource == ContentSource.Modrinth;
    public bool IsCurseForgeSource => SelectedSource == ContentSource.CurseForge;
    public string SourceButtonText => SelectedSource switch
    {
        ContentSource.All => L10n.T("source_all"),
        ContentSource.CurseForge => "CurseForge",
        _ => "Modrinth"
    };

    public SourceFilterViewModel BrowserSourceFilter { get; }

    [RelayCommand]
    public void SelectSource(string source)
    {
        var newSource = source?.ToLowerInvariant() switch
        {
            "all" or "both" => ContentSource.All,
            "curseforge" => ContentSource.CurseForge,
            _ => ContentSource.Modrinth
        };

        if (SelectedSource == newSource) return;
        SelectedSource = newSource;
        BrowserSourceFilter?.SetSource(newSource);
        OnPropertyChanged(nameof(IsAllSource));
        OnPropertyChanged(nameof(IsModrinthSource));
        OnPropertyChanged(nameof(IsCurseForgeSource));
        OnPropertyChanged(nameof(SourceButtonText));
        _ = ReloadAsync();
    }

    private static List<ModrinthProjectItemViewModel> MergeProjectResults(
        IReadOnlyList<ModrinthProjectItemViewModel>? mrHits,
        IReadOnlyList<ModrinthProjectItemViewModel>? cfHits,
        string sortOption)
    {
        var mrList = mrHits ?? Array.Empty<ModrinthProjectItemViewModel>();
        var cfList = cfHits ?? Array.Empty<ModrinthProjectItemViewModel>();

        if (string.Equals(sortOption, "Downloads", StringComparison.OrdinalIgnoreCase))
        {
            return mrList.Concat(cfList).OrderByDescending(p => p.Downloads).ToList();
        }
        if (string.Equals(sortOption, "Newest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sortOption, "Recently Updated", StringComparison.OrdinalIgnoreCase))
        {
            return mrList.Concat(cfList).OrderByDescending(p => p.Updated).ToList();
        }

        var result = new List<ModrinthProjectItemViewModel>();
        int max = Math.Max(mrList.Count, cfList.Count);
        for (int i = 0; i < max; i++)
        {
            if (i < mrList.Count) result.Add(mrList[i]);
            if (i < cfList.Count) result.Add(cfList[i]);
        }
        return result;
    }

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

    partial void OnFullProjectChanging(Models.ModrinthFullProject? value)
    {
        try { _fullProject?.Dispose(); } catch { }
        try { HeaderImage?.Dispose(); HeaderImage = null; } catch { }
    }

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _headerImage;

    public IReadOnlyList<Models.ModrinthGalleryItem> GalleryItems =>
        FullProject?.Gallery ?? (IReadOnlyList<Models.ModrinthGalleryItem>)Array.Empty<Models.ModrinthGalleryItem>();

    public bool HasGalleryItems => GalleryItems.Count > 0;

    [ObservableProperty]
    private bool _fullProjectBusy;

    /// <summary>Body text with raw HTML tags stripped for display.</summary>
    public string CleanBody => FullProject == null ? "" : MarkdownSanitizer.Clean(FullProject.Body);

    [ObservableProperty]
    private string _selectedDetailTab = "description";

    [ObservableProperty]
    private bool _showAllVersions = true;

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
        SelectedDetailVersion = null;
        OnPropertyChanged(nameof(IsVersionDetailActive));
        OnPropertyChanged(nameof(IsDescriptionTab));
        OnPropertyChanged(nameof(IsGalleryTab));
        OnPropertyChanged(nameof(IsChangelogTab));
        OnPropertyChanged(nameof(IsVersionsTab));
        OnPropertyChanged(nameof(VersionList));
    }

    public bool IsDescriptionTab => SelectedDetailTab.Equals("description", StringComparison.OrdinalIgnoreCase);
    public bool IsGalleryTab => SelectedDetailTab.Equals("gallery", StringComparison.OrdinalIgnoreCase);
    public bool IsChangelogTab => SelectedDetailTab.Equals("changelog", StringComparison.OrdinalIgnoreCase);
    public bool IsVersionsTab => SelectedDetailTab.Equals("versions", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void SelectTab(string tab)
    {
        SelectedDetailVersion = null;
        OnPropertyChanged(nameof(IsVersionDetailActive));
        SelectedDetailTab = tab;
    }

    private List<Models.ModrinthProjectVersion> _allVersions = new();

        // --- Gallery Lightbox & Version Detail View ---
    [ObservableProperty]
    private Models.ModrinthGalleryItem? _activeGalleryModalItem;

    [ObservableProperty]
    private double _galleryZoom = 1.0;

    public bool IsGalleryModalOpen => ActiveGalleryModalItem != null;

    [ObservableProperty]
    private Models.ModrinthProjectVersion? _selectedDetailVersion;

    public bool IsVersionDetailActive => SelectedDetailVersion != null;

    public GameVersionFilterViewModel GameVersionFilter { get; } = new();
    public GameVersionFilterViewModel BrowserVersionFilter { get; } = new();
    public LoaderFilterViewModel BrowserLoaderFilter { get; } = new();

    public SortFilterViewModel BrowserSortFilter { get; } = new();

    [ObservableProperty]
    private string _selectedFilterGameVersion = "All versions";

    [ObservableProperty]
    private string _selectedFilterPlatform = "All platforms";

    [ObservableProperty]
    private IReadOnlyList<string> _filterGameVersions = new[] { "All versions" };

    [ObservableProperty]
    private IReadOnlyList<string> _filterPlatforms = new[] { "All platforms" };

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<Models.FilterCheckItem> _platformFilterItems = new();

    public string PlatformFilterButtonText
    {
        get
        {
            var selected = PlatformFilterItems.Where(p => p.IsSelected).Select(p => p.Name).ToList();
            if (selected.Count == 0 || selected.Count == PlatformFilterItems.Count)
                return "All platforms";
            if (selected.Count == 1)
                return selected[0];
            return $"Platforms ({selected.Count})";
        }
    }

    public bool IsAllPlatformsSelected => PlatformFilterItems.Count == 0 || PlatformFilterItems.All(p => !p.IsSelected) || PlatformFilterItems.All(p => p.IsSelected);

    [RelayCommand]
    public void SelectAllPlatforms()
    {
        foreach (var item in PlatformFilterItems)
            item.IsSelected = false;
        OnPropertyChanged(nameof(PlatformFilterButtonText));
        OnPropertyChanged(nameof(IsAllPlatformsSelected));
        CurrentVersionPage = 1;
        RefreshPagination();
    }

    public static List<string> SortGameVersionsDesc(IEnumerable<string>? rawVersions)
    {
        if (rawVersions == null) return new() { "All versions" };
        var distinct = rawVersions
            .Where(v => !string.IsNullOrWhiteSpace(v) && !v.Equals("All versions", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        distinct.Sort((a, b) =>
        {
            var keyA = GetVersionSortKey(a);
            var keyB = GetVersionSortKey(b);
            if (keyA.rank != keyB.rank) return keyB.rank.CompareTo(keyA.rank);

            int max = Math.Min(keyA.numbers.Count, keyB.numbers.Count);
            for (int i = 0; i < max; i++)
            {
                if (keyA.numbers[i] != keyB.numbers[i])
                    return keyB.numbers[i].CompareTo(keyA.numbers[i]);
            }
            return keyB.numbers.Count.CompareTo(keyA.numbers.Count);
        });

        var result = new List<string> { "All versions" };
        result.AddRange(distinct);
        return result;
    }

    private static (int rank, List<int> numbers) GetVersionSortKey(string v)
    {
        var clean = v.Trim().ToLowerInvariant();
        int rank = 2;
        if (clean.StartsWith("b") || clean.Contains("beta"))
        {
            rank = 1;
            clean = Regex.Replace(clean, @"^[b_]+", "");
        }
        else if (clean.StartsWith("a") || clean.Contains("alpha"))
        {
            rank = 0;
            clean = Regex.Replace(clean, @"^[a_]+", "");
        }

        var nums = new List<int>();
        var parts = Regex.Split(clean, @"[^0-9]+");
        foreach (var p in parts)
        {
            if (int.TryParse(p, out int n))
                nums.Add(n);
        }
        return (rank, nums);
    }

    [RelayCommand]
    public void OpenGalleryModal(Models.ModrinthGalleryItem? item)
    {
        if (item == null) return;
        ActiveGalleryModalItem = item;
        GalleryZoom = 1.0;
        OnPropertyChanged(nameof(IsGalleryModalOpen));
    }

    [RelayCommand]
    public void CloseGalleryModal()
    {
        ActiveGalleryModalItem = null;
        OnPropertyChanged(nameof(IsGalleryModalOpen));
    }

    [RelayCommand]
    public void GalleryZoomIn()
    {
        GalleryZoom = Math.Min(4.0, Math.Round(GalleryZoom + 0.25, 2));
    }

    [RelayCommand]
    public void GalleryZoomOut()
    {
        GalleryZoom = Math.Max(0.25, Math.Round(GalleryZoom - 0.25, 2));
    }

    [RelayCommand]
    public void GalleryZoomReset()
    {
        GalleryZoom = 1.0;
    }

    [ObservableProperty]
    private string _sourceTabBeforeDetail = "versions";

    [RelayCommand]
    public void OpenVersionDetail(Models.ModrinthProjectVersion? version)
    {
        if (version == null) return;
        SourceTabBeforeDetail = SelectedDetailTab;
        SelectedDetailVersion = version;
        OnPropertyChanged(nameof(IsVersionDetailActive));
    }

    [RelayCommand]
    public void CloseVersionDetail()
    {
        SelectedDetailVersion = null;
        OnPropertyChanged(nameof(IsVersionDetailActive));
        if (!string.IsNullOrWhiteSpace(SourceTabBeforeDetail))
        {
            SelectedDetailTab = SourceTabBeforeDetail;
        }
    }

    [RelayCommand]
    public void DownloadVersion(Models.ModrinthProjectVersion? version)
    {
        InstallSelectedVersion(version);
    }

    private const int DetailVersionsPageSize = 15;

    [ObservableProperty]
    private int _currentVersionPage = 1;

    public int TotalVersionPages
    {
        get
        {
            var count = AllFilteredVersions.Count;
            return Math.Max(1, (int)Math.Ceiling(count / (double)DetailVersionsPageSize));
        }
    }

    public bool HasMultipleVersionPages => TotalVersionPages > 1;
    public bool CanGoNextVersionPage => CurrentVersionPage < TotalVersionPages;
    public bool CanGoPrevVersionPage => CurrentVersionPage > 1;

    private void RefreshPagination()
    {
        OnPropertyChanged(nameof(TotalVersionPages));
        OnPropertyChanged(nameof(HasMultipleVersionPages));
        OnPropertyChanged(nameof(CanGoNextVersionPage));
        OnPropertyChanged(nameof(CanGoPrevVersionPage));
        OnPropertyChanged(nameof(VersionPageItems));
        OnPropertyChanged(nameof(VersionList));
    }

    partial void OnSelectedFilterGameVersionChanged(string value)
    {
        CurrentVersionPage = 1;
        RefreshPagination();
    }

    partial void OnSelectedFilterPlatformChanged(string value)
    {
        CurrentVersionPage = 1;
        RefreshPagination();
    }

    partial void OnVersionFilterChanged(string value)
    {
        CurrentVersionPage = 1;
        RefreshPagination();
    }

    partial void OnShowAllVersionsChanged(bool value)
    {
        CurrentVersionPage = 1;
        RefreshPagination();
    }

    public List<Models.ModrinthProjectVersion> AllFilteredVersions
    {
        get
        {
            if (_allVersions == null || _allVersions.Count == 0) return new();
            var filtered = (IEnumerable<Models.ModrinthProjectVersion>)_allVersions;

            var selectedVersions = GameVersionFilter.SelectedVersions;
            if (selectedVersions.Count > 0)
            {
                filtered = filtered.Where(v => v.GameVersions != null && v.GameVersions.Any(gv =>
                    selectedVersions.Contains(gv, StringComparer.OrdinalIgnoreCase)));
            }
            else if (!string.IsNullOrWhiteSpace(SelectedFilterGameVersion) && !SelectedFilterGameVersion.Equals("All versions", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(v => v.GameVersions != null && v.GameVersions.Any(gv => gv != null && gv.Equals(SelectedFilterGameVersion, StringComparison.OrdinalIgnoreCase)));
            }
            else if (!GameVersionFilter.ShowAllVersions && !ShowAllVersions && !string.IsNullOrWhiteSpace(SelectedVersion))
            {
                filtered = filtered.Where(v => v.GameVersions != null && v.GameVersions.Any(gv => gv != null && gv.Contains(SelectedVersion, StringComparison.OrdinalIgnoreCase)));
            }

            var selectedPlatforms = PlatformFilterItems.Where(p => p.IsSelected).Select(p => p.Name).ToList();
            if (selectedPlatforms.Count > 0)
            {
                filtered = filtered.Where(v => v.Loaders != null && v.Loaders.Any(l =>
                    selectedPlatforms.Any(sp => sp.Equals(Models.ModrinthProjectVersion.FormatPlatformName(l), StringComparison.OrdinalIgnoreCase))));
            }
            else if (!string.IsNullOrWhiteSpace(SelectedFilterPlatform) && !SelectedFilterPlatform.Equals("All platforms", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(v => v.Loaders != null && v.Loaders.Any(l => l != null &&
                    Models.ModrinthProjectVersion.FormatPlatformName(l).Equals(SelectedFilterPlatform, StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrWhiteSpace(VersionFilter))
            {
                var q = VersionFilter.ToLowerInvariant();
                filtered = filtered.Where(v =>
                    (v.Name != null && v.Name.ToLowerInvariant().Contains(q)) ||
                    (v.VersionNumber != null && v.VersionNumber.ToLowerInvariant().Contains(q)));
            }
            return filtered.ToList();
        }
    }

    public List<Models.ModrinthProjectVersion> VersionList
    {
        get
        {
            var all = AllFilteredVersions;
            int total = Math.Max(1, (int)Math.Ceiling(all.Count / (double)DetailVersionsPageSize));
            int cur = CurrentVersionPage;
            if (cur > total) cur = total;
            if (cur < 1) cur = 1;

            var pageList = all.Skip((cur - 1) * DetailVersionsPageSize)
                              .Take(DetailVersionsPageSize)
                              .ToList();
            for (int i = 0; i < pageList.Count; i++)
            {
                pageList[i].IsFirstInTimeline = (i == 0);
                pageList[i].IsLastInTimeline = (i == pageList.Count - 1);
            }
            return pageList;
        }
    }

    public List<Models.PaginationPageItem> VersionPageItems
    {
        get
        {
            var list = new List<Models.PaginationPageItem>();
            int total = TotalVersionPages;
            int cur = CurrentVersionPage;

            if (total <= 7)
            {
                for (int i = 1; i <= total; i++)
                {
                    list.Add(new Models.PaginationPageItem
                    {
                        PageNumber = i,
                        DisplayText = i.ToString(),
                        IsActive = (i == cur),
                        IsEllipsis = false
                    });
                }
            }
            else
            {
                if (cur <= 4)
                {
                    for (int i = 1; i <= 5; i++)
                    {
                        list.Add(new Models.PaginationPageItem { PageNumber = i, DisplayText = i.ToString(), IsActive = (i == cur) });
                    }
                    list.Add(new Models.PaginationPageItem { PageNumber = -1, DisplayText = "...", IsEllipsis = true });
                    list.Add(new Models.PaginationPageItem { PageNumber = total, DisplayText = total.ToString(), IsActive = (total == cur) });
                }
                else if (cur >= total - 3)
                {
                    list.Add(new Models.PaginationPageItem { PageNumber = 1, DisplayText = "1", IsActive = (1 == cur) });
                    list.Add(new Models.PaginationPageItem { PageNumber = -1, DisplayText = "...", IsEllipsis = true });
                    for (int i = total - 4; i <= total; i++)
                    {
                        list.Add(new Models.PaginationPageItem { PageNumber = i, DisplayText = i.ToString(), IsActive = (i == cur) });
                    }
                }
                else
                {
                    list.Add(new Models.PaginationPageItem { PageNumber = 1, DisplayText = "1", IsActive = (1 == cur) });
                    list.Add(new Models.PaginationPageItem { PageNumber = -1, DisplayText = "...", IsEllipsis = true });
                    list.Add(new Models.PaginationPageItem { PageNumber = cur - 1, DisplayText = (cur - 1).ToString(), IsActive = false });
                    list.Add(new Models.PaginationPageItem { PageNumber = cur, DisplayText = cur.ToString(), IsActive = true });
                    list.Add(new Models.PaginationPageItem { PageNumber = cur + 1, DisplayText = (cur + 1).ToString(), IsActive = false });
                    list.Add(new Models.PaginationPageItem { PageNumber = -1, DisplayText = "...", IsEllipsis = true });
                    list.Add(new Models.PaginationPageItem { PageNumber = total, DisplayText = total.ToString(), IsActive = (total == cur) });
                }
            }
            return list;
        }
    }

    [RelayCommand]
    public void GoToVersionPage(int page)
    {
        if (page < 1 || page > TotalVersionPages || page == CurrentVersionPage) return;
        CurrentVersionPage = page;
        RefreshPagination();
    }

    [RelayCommand]
    public void NextVersionPage()
    {
        if (CanGoNextVersionPage)
            GoToVersionPage(CurrentVersionPage + 1);
    }

    [RelayCommand]
    public void PrevVersionPage()
    {
        if (CanGoPrevVersionPage)
            GoToVersionPage(CurrentVersionPage - 1);
    }

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
        BrowserSourceFilter = new SourceFilterViewModel(SelectedSource, s =>
        {
            SelectSource(s switch { ContentSource.All => "all", ContentSource.CurseForge => "curseforge", _ => "modrinth" });
        });
        AvailableCategories = new ObservableCollection<string>(new[] { "", "adventure", "cursed", "magic", "technology", "exploration", "optimization" });
        RefreshAvailableLoaders();
        GameVersionFilter.FilterChanged += () =>
        {
            CurrentVersionPage = 1;
            RefreshPagination();
            OnPropertyChanged(nameof(VersionList));
        };
        BrowserVersionFilter.FilterChanged += () =>
        {
            _ = ReloadAsync();
        };
        BrowserLoaderFilter.FilterChanged += () =>
        {
            _ = ReloadAsync();
        };
        BrowserSortFilter.SortChanged += (newSort) =>
        {
            SelectedSort = newSort;
        };
        L10n.LanguageChanged += () => OnPropertyChanged(nameof(SearchPlaceholder));
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

        BrowserSortFilter.SetOptions(AvailableSortOptions, SelectedSort);
        RefreshAvailableLoaders();
        OnPropertyChanged(nameof(SearchPlaceholder));
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
                BrowserVersionFilter.SetVersions(sorted);
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
                    BrowserVersionFilter.SetVersions(releases);
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
    partial void OnSelectedSortChanged(string value)
    {
        BrowserSortFilter.SetSelected(value, notifyEvent: false);
        _ = ReloadAsync();
    }

    /// <summary>
    /// Loader availability follows Minecraft history so ancient versions never
    /// offer loaders that did not exist yet (e.g. no Fabric for 1.5.2).
    /// Forge exists since 1.1, Fabric/Quilt since 1.14, NeoForge since 1.20.
    /// </summary>
    private void RefreshAvailableLoaders()
    {
        long key = string.IsNullOrWhiteSpace(SelectedVersion) ? long.MaxValue : ParseVersion(SelectedVersion);
        var defs = new List<(string Id, string DisplayName)>();
        if (key >= ParseVersion("1.14")) defs.Add(("fabric", "Fabric"));
        if (key >= ParseVersion("1.1")) defs.Add(("forge", "Forge"));
        if (key >= ParseVersion("1.20")) defs.Add(("neoforge", "NeoForge"));
        if (key >= ParseVersion("1.14")) defs.Add(("quilt", "Quilt"));

        var loaders = new List<string> { "" };
        foreach (var d in defs) loaders.Add(d.Id);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AvailableLoaders.Clear();
            foreach (var l in loaders) AvailableLoaders.Add(l);
            if (!AvailableLoaders.Contains(SelectedLoader))
                SelectedLoader = "";
            OnPropertyChanged(nameof(SelectedLoader));

            BrowserLoaderFilter.SetAvailableLoaders(defs);
        });
    }

    private string[] LoaderFacets =>
        BrowserLoaderFilter.SelectedLoaders.Count > 0
            ? BrowserLoaderFilter.SelectedLoaders.ToArray()
            : (string.IsNullOrWhiteSpace(SelectedLoader) ? Array.Empty<string>() : new[] { SelectedLoader.Trim().ToLowerInvariant() });

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
        BrowserVersionFilter.ClearCommand.Execute(null);
        BrowserLoaderFilter.ClearCommand.Execute(null);
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
            if (SelectedSource == ContentSource.All)
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var cfTask = CurseForgeApiService.SearchProjectsAsync(
                    SearchQuery,
                    SelectedProjectType,
                    SelectedVersion,
                    SelectedLoader,
                    0,
                    PageSize,
                    SelectedSort,
                    apiKey,
                    token);

                var mrTask = ModrinthApiService.SearchAsync(
                    SearchQuery,
                    SelectedVersion,
                    LoaderFacets,
                    SelectedProjectType,
                    sort,
                    categoryFilter,
                    0,
                    PageSize,
                    token,
                    BrowserVersionFilter.SelectedVersions);

                await Task.WhenAll(cfTask, mrTask);
                if (token.IsCancellationRequested || gen != _searchGeneration) return;

                var cfRes = cfTask.Result;
                var mrPage = mrTask.Result;

                var merged = MergeProjectResults(mrPage?.Hits, cfRes?.Hits, SelectedSort);
                _totalHits = (mrPage?.TotalHits ?? 0) + (cfRes?.TotalHits ?? 0);
                _offset = merged.Count;

                foreach (var p in merged)
                {
                    Results.Add(p);
                    LoadIcon(p, gen);
                }
                UpdateInstalledFlags();
                StatusText = _totalHits == 0 ? L10n.T("mp_status_none") : "";
                return;
            }

            if (SelectedSource == ContentSource.All)
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var cfTask = CurseForgeApiService.SearchProjectsAsync(
                    SearchQuery,
                    SelectedProjectType,
                    SelectedVersion,
                    SelectedLoader,
                    _offset / 2,
                    PageSize,
                    SelectedSort,
                    apiKey);

                var mrTask = ModrinthApiService.SearchAsync(
                    SearchQuery,
                    SelectedVersion,
                    LoaderFacets,
                    SelectedProjectType,
                    sort,
                    categoryFilter,
                    _offset / 2,
                    PageSize);

                await Task.WhenAll(cfTask, mrTask);
                if (gen != _searchGeneration) return;

                var cfRes = cfTask.Result;
                var mrPage = mrTask.Result;

                var merged = MergeProjectResults(mrPage?.Hits, cfRes?.Hits, SelectedSort);
                if (merged.Count == 0)
                {
                    _offset = _totalHits;
                }
                else
                {
                    _offset += merged.Count;
                    _totalHits = (mrPage?.TotalHits ?? 0) + (cfRes?.TotalHits ?? 0);
                    var existing = Results.Select(r => r.ProjectId).ToHashSet();
                    foreach (var p in merged)
                    {
                        if (existing.Add(p.ProjectId))
                        {
                            Results.Add(p);
                            LoadIcon(p, gen);
                        }
                    }
                    UpdateInstalledFlags();
                }
                return;
            }

            if (SelectedSource == ContentSource.CurseForge || SelectedProjectType == "world")
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var res = await CurseForgeApiService.SearchProjectsAsync(
                    SearchQuery,
                    SelectedProjectType,
                    SelectedVersion,
                    SelectedLoader,
                    0,
                    PageSize,
                    SelectedSort,
                    apiKey,
                    token);
                if (token.IsCancellationRequested || gen != _searchGeneration) return;
                if (res == null)
                {
                    StatusText = L10n.T("mp_status_unreachable");
                    return;
                }
                _totalHits = res.TotalHits;
                _offset = res.Limit;
                foreach (var p in res.Hits)
                {
                    Results.Add(p);
                    LoadIcon(p, gen);
                }
                UpdateInstalledFlags();
                StatusText = res.TotalHits == 0 ? L10n.T("mp_status_none") : "";
                return;
            }

            var page = await ModrinthApiService.SearchAsync(SearchQuery, SelectedVersion, LoaderFacets, SelectedProjectType, sort, categoryFilter, 0, PageSize, token, BrowserVersionFilter.SelectedVersions);
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
            StatusText = page.TotalHits == 0 ? L10n.T("mp_status_none") : "";
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
            if (SelectedSource == ContentSource.All)
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var cfTask = CurseForgeApiService.SearchProjectsAsync(
                    SearchQuery,
                    SelectedProjectType,
                    SelectedVersion,
                    SelectedLoader,
                    _offset / 2,
                    PageSize,
                    SelectedSort,
                    apiKey);

                var mrTask = ModrinthApiService.SearchAsync(
                    SearchQuery,
                    SelectedVersion,
                    LoaderFacets,
                    SelectedProjectType,
                    sort,
                    categoryFilter,
                    _offset / 2,
                    PageSize);

                await Task.WhenAll(cfTask, mrTask);
                if (gen != _searchGeneration) return;

                var cfRes = cfTask.Result;
                var mrPage = mrTask.Result;

                var merged = MergeProjectResults(mrPage?.Hits, cfRes?.Hits, SelectedSort);
                if (merged.Count == 0)
                {
                    _offset = _totalHits;
                }
                else
                {
                    _offset += merged.Count;
                    _totalHits = (mrPage?.TotalHits ?? 0) + (cfRes?.TotalHits ?? 0);
                    var existing = Results.Select(r => r.ProjectId).ToHashSet();
                    foreach (var p in merged)
                    {
                        if (existing.Add(p.ProjectId))
                        {
                            Results.Add(p);
                            LoadIcon(p, gen);
                        }
                    }
                    UpdateInstalledFlags();
                }
                return;
            }

            if (SelectedSource == ContentSource.CurseForge || SelectedProjectType == "world")
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var res = await CurseForgeApiService.SearchProjectsAsync(
                    SearchQuery,
                    SelectedProjectType,
                    SelectedVersion,
                    SelectedLoader,
                    _offset,
                    PageSize,
                    SelectedSort,
                    apiKey);
                if (res != null && gen == _searchGeneration)
                {
                    _totalHits = res.TotalHits;
                    if (res.Hits.Count == 0) _offset = _totalHits;
                    else
                    {
                        _offset += res.Limit;
                        var existing = Results.Select(r => r.ProjectId).ToHashSet();
                        foreach (var p in res.Hits)
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
                return;
            }

            var page = await ModrinthApiService.SearchAsync(SearchQuery, SelectedVersion, LoaderFacets, SelectedProjectType, sort, categoryFilter, _offset, PageSize);
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
        if (project.ProjectType != "modpack" && SelectedProjectType != "modpack")
        {
            OpenAddToInstanceModal(project);
            return;
        }
        // Install directly with the latest version (no page navigation)
        _ = InstallLatestAsync(project);
    }

    [ObservableProperty] private bool _isAddToInstanceOpen;
    [ObservableProperty] private bool _isSelectWorldStep;
    [ObservableProperty] private InstanceModel? _selectedTargetInstanceForWorld;
    [ObservableProperty] private ObservableCollection<WorldTargetItem> _targetWorlds = new();
    [ObservableProperty] private bool _hasNoWorldsFound;
    [ObservableProperty] private string _instanceSearchQuery = "";
    [ObservableProperty] private ModrinthProjectItemViewModel? _projectForAddToInstance;
    [ObservableProperty] private ObservableCollection<InstanceTargetItem> _targetInstances = new();

    public string TargetInstanceNameForWorld => SelectedTargetInstanceForWorld?.Name ?? "";
    public string AddToInstanceModalTitle => IsSelectWorldStep ? "Select World" : "Add to Instance";
    public string SelectWorldSubtitle => $"Select which world in '{TargetInstanceNameForWorld}' to add '{ProjectForAddToInstance?.Title}' to:";
    public int CompatibleInstanceCount => TargetInstances.Count(i => !i.IsInstalled);

    partial void OnInstanceSearchQueryChanged(string value) => RefreshTargetInstances();

    private void ClearTargetWorlds()
    {
        foreach (var w in TargetWorlds)
        {
            w.Dispose();
        }
        TargetWorlds.Clear();
    }

    [RelayCommand]
    public void BackToInstances()
    {
        IsSelectWorldStep = false;
        SelectedTargetInstanceForWorld = null;
        ClearTargetWorlds();
        OnPropertyChanged(nameof(AddToInstanceModalTitle));
        OnPropertyChanged(nameof(IsSelectWorldStep));
    }

    [RelayCommand]
    public void CloseAddToInstance()
    {
        IsAddToInstanceOpen = false;
        IsSelectWorldStep = false;
        SelectedTargetInstanceForWorld = null;
        ClearTargetWorlds();
        ProjectForAddToInstance = null;
        TargetInstances.Clear();
        OnPropertyChanged(nameof(AddToInstanceModalTitle));
        OnPropertyChanged(nameof(IsSelectWorldStep));
    }

    [RelayCommand]
    public void OpenAddToInstanceModal(ModrinthProjectItemViewModel? project)
    {
        if (project == null) return;
        ProjectForAddToInstance = project;
        InstanceSearchQuery = "";
        IsSelectWorldStep = false;
        SelectedTargetInstanceForWorld = null;
        ClearTargetWorlds();
        RefreshTargetInstances();
        OnPropertyChanged(nameof(AddToInstanceModalTitle));
        OnPropertyChanged(nameof(IsSelectWorldStep));
        IsAddToInstanceOpen = true;
    }

    private void RefreshTargetInstances()
    {
        if (ProjectForAddToInstance == null) return;
        TargetInstances.Clear();
        var allInstances = _instanceService.GetInstances();
        var q = InstanceSearchQuery?.Trim().ToLowerInvariant() ?? "";
        bool isDatapack = SelectedProjectType == "datapack" ||
                          string.Equals(ProjectForAddToInstance.ProjectType, "datapack", StringComparison.OrdinalIgnoreCase);

        foreach (var inst in allInstances)
        {
            if (!string.IsNullOrEmpty(q) &&
                !inst.Name.ToLowerInvariant().Contains(q) &&
                !(inst.Version ?? "").ToLowerInvariant().Contains(q))
                continue;

            bool isInst = isDatapack ? false : CheckIfProjectInstalledInInstance(inst, ProjectForAddToInstance);
            TargetInstances.Add(new InstanceTargetItem(inst, isInst, isDatapack, async (target) =>
            {
                if (isDatapack)
                {
                    OpenSelectWorldStep(target.Instance);
                }
                else
                {
                    await InstallProjectToInstanceAsync(ProjectForAddToInstance, target.Instance);
                }
            }));
        }
        OnPropertyChanged(nameof(CompatibleInstanceCount));
    }

    public void OpenSelectWorldStep(InstanceModel instance)
    {
        SelectedTargetInstanceForWorld = instance;
        ClearTargetWorlds();

        var savesDir = Path.Combine(instance.Path, "saves");
        if (Directory.Exists(savesDir))
        {
            var slug = ProjectForAddToInstance?.Slug?.ToLowerInvariant() ?? "";
            var title = ProjectForAddToInstance?.Title?.ToLowerInvariant() ?? "";
            var id = ProjectForAddToInstance?.ProjectId?.ToLowerInvariant() ?? "";

            foreach (var worldDir in Directory.GetDirectories(savesDir))
            {
                var folderName = Path.GetFileName(worldDir);
                string worldDisplayName = folderName;
                try
                {
                    var levelDat = Path.Combine(worldDir, "level.dat");
                    if (File.Exists(levelDat))
                    {
                        var data = ServersDatService.ReadNbtFile(levelDat);
                        if (data != null)
                        {
                            var level = data;
                            if (level.TryGetValue("Data", out var inner) && inner is Dictionary<string, object?> innerDict)
                                level = innerDict;
                            if (level.TryGetValue("LevelName", out var ln) && ln is string lns && !string.IsNullOrWhiteSpace(lns))
                                worldDisplayName = lns;
                        }
                    }
                }
                catch { }

                bool isInstalledInWorld = false;
                var dpFolder = Path.Combine(worldDir, "datapacks");
                if (Directory.Exists(dpFolder))
                {
                    foreach (var file in Directory.GetFiles(dpFolder))
                    {
                        var fn = Path.GetFileName(file).ToLowerInvariant();
                        if (!string.IsNullOrEmpty(slug) && fn.Contains(slug)) { isInstalledInWorld = true; break; }
                        if (!string.IsNullOrEmpty(id) && fn.Contains(id)) { isInstalledInWorld = true; break; }
                        if (!string.IsNullOrEmpty(title) && fn.Contains(title.Replace(" ", "-"))) { isInstalledInWorld = true; break; }
                    }
                }

                var worldItem = new WorldTargetItem(folderName, worldDisplayName, worldDir, isInstalledInWorld, async (w) =>
                {
                    if (ProjectForAddToInstance != null)
                    {
                        await InstallDatapackToWorldAsync(ProjectForAddToInstance, instance, w.FolderName);
                    }
                });
                TargetWorlds.Add(worldItem);
            }
        }

        HasNoWorldsFound = TargetWorlds.Count == 0;
        IsSelectWorldStep = true;
        OnPropertyChanged(nameof(TargetInstanceNameForWorld));
        OnPropertyChanged(nameof(AddToInstanceModalTitle));
        OnPropertyChanged(nameof(SelectWorldSubtitle));
        OnPropertyChanged(nameof(IsSelectWorldStep));
    }

    private async Task InstallDatapackToWorldAsync(ModrinthProjectItemViewModel project, InstanceModel instance, string worldName)
    {
        if (project.Source == ContentSource.CurseForge)
        {
            var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
            var fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, instance.Version, null, apiKey);
            if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, null, null, apiKey);
            if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                throw new Exception("No downloadable file found on CurseForge.");

            var cfData = await CurseForgeApiService.DownloadAsync(fileInfo.DownloadUrl);
            if (cfData == null || cfData.Length == 0)
                throw new Exception("Download failed.");

            var cfTargetFolder = Path.Combine(instance.Path, "saves", worldName, "datapacks");
            Directory.CreateDirectory(cfTargetFolder);
            var cfDest = Path.Combine(cfTargetFolder, fileInfo.FileName);
            await File.WriteAllBytesAsync(cfDest, cfData);

            StatusText = $"Added datapack '{project.Title}' to world '{worldName}' ({instance.Name})";
            project.IsInstalled = true;
            return;
        }

        var mcVer = instance.Version;
        var loader = (instance.LoaderType ?? "").ToLowerInvariant();

        var versions = await ModrinthApiService.GetProjectVersionsAsync(
            project.ProjectId,
            !string.IsNullOrWhiteSpace(loader) && loader != "vanilla" ? new[] { loader } : null,
            !string.IsNullOrWhiteSpace(mcVer) ? new[] { mcVer } : null);

        if (versions.Count == 0)
        {
            versions = await ModrinthApiService.GetProjectVersionsAsync(project.ProjectId);
        }

        var targetVer = versions.FirstOrDefault(v => v.GameVersions.Contains(mcVer)) ?? versions.FirstOrDefault();
        if (targetVer == null || targetVer.Files == null || targetVer.Files.Count == 0)
            throw new Exception("No compatible version found.");

        var primaryFile = targetVer.Files.FirstOrDefault(f => f.IsPrimary) ?? targetVer.Files[0];
        if (string.IsNullOrWhiteSpace(primaryFile.Url))
            throw new Exception("No file URL.");

        var data = await ModrinthApiService.DownloadAsync(primaryFile.Url);
        if (data == null || data.Length == 0)
            throw new Exception("Download failed.");

        var targetFolder = Path.Combine(instance.Path, "saves", worldName, "datapacks");
        Directory.CreateDirectory(targetFolder);
        var dest = Path.Combine(targetFolder, primaryFile.Filename);
        await File.WriteAllBytesAsync(dest, data);

        StatusText = $"Added datapack '{project.Title}' to world '{worldName}' ({instance.Name})";
        project.IsInstalled = true;
    }

    private async Task InstallProjectToInstanceAsync(ModrinthProjectItemViewModel project, InstanceModel instance)
    {
        if (project.Source == ContentSource.CurseForge)
        {
            var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
            var fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, instance.Version, instance.LoaderType, apiKey);
            if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, instance.Version, null, apiKey);
            if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, null, null, apiKey);
            if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                throw new Exception($"No downloadable file found on CurseForge for {project.Title}.");

            var cfData = await CurseForgeApiService.DownloadAsync(fileInfo.DownloadUrl);
            if (cfData == null || cfData.Length == 0)
                throw new Exception("Download failed.");

            var cfSubDir = (project.ProjectType ?? SelectedProjectType) switch
            {
                "resourcepack" => "resourcepacks",
                "shader" => "shaderpacks",
                "datapack" => Path.Combine("saves", "datapacks"),
                _ => "mods"
            };

            var cfTargetFolder = Path.Combine(instance.Path, cfSubDir);
            Directory.CreateDirectory(cfTargetFolder);
            var cfDest = Path.Combine(cfTargetFolder, fileInfo.FileName);
            await File.WriteAllBytesAsync(cfDest, cfData);

            StatusText = $"Added {project.Title} to {instance.Name}";
            project.IsInstalled = true;
            return;
        }

        var mcVer = instance.Version;
        var loader = (instance.LoaderType ?? "").ToLowerInvariant();

        var versions = await ModrinthApiService.GetProjectVersionsAsync(
            project.ProjectId,
            !string.IsNullOrWhiteSpace(loader) && loader != "vanilla" ? new[] { loader } : null,
            !string.IsNullOrWhiteSpace(mcVer) ? new[] { mcVer } : null);

        if (versions.Count == 0)
        {
            versions = await ModrinthApiService.GetProjectVersionsAsync(project.ProjectId);
        }

        var targetVer = versions.FirstOrDefault(v => v.GameVersions.Contains(mcVer)) ?? versions.FirstOrDefault();
        if (targetVer == null || targetVer.Files == null || targetVer.Files.Count == 0)
            throw new Exception("No compatible version found.");

        var primaryFile = targetVer.Files.FirstOrDefault(f => f.IsPrimary) ?? targetVer.Files[0];
        if (string.IsNullOrWhiteSpace(primaryFile.Url))
            throw new Exception("No file URL.");

        var data = await ModrinthApiService.DownloadAsync(primaryFile.Url);
        if (data == null || data.Length == 0)
            throw new Exception("Download failed.");

        var subDir = project.ProjectType switch
        {
            "resourcepack" => "resourcepacks",
            "shader" => "shaderpacks",
            _ => "mods"
        };

        var targetFolder = Path.Combine(instance.Path, subDir);
        Directory.CreateDirectory(targetFolder);
        var dest = Path.Combine(targetFolder, primaryFile.Filename);
        await File.WriteAllBytesAsync(dest, data);

        StatusText = $"Added {project.Title} to {instance.Name}";
        project.IsInstalled = true;
    }

    private static bool CheckIfProjectInstalledInInstance(InstanceModel instance, ModrinthProjectItemViewModel project)
    {
        var slug = project.Slug?.ToLowerInvariant() ?? "";
        var title = project.Title?.ToLowerInvariant() ?? "";
        var id = project.ProjectId?.ToLowerInvariant() ?? "";

        if (project.ProjectType == "datapack")
        {
            var saves = Path.Combine(instance.Path, "saves");
            if (!Directory.Exists(saves)) return false;
            foreach (var worldDir in Directory.GetDirectories(saves))
            {
                var dpFolder = Path.Combine(worldDir, "datapacks");
                if (!Directory.Exists(dpFolder)) continue;
                foreach (var file in Directory.GetFiles(dpFolder))
                {
                    var fn = Path.GetFileName(file).ToLowerInvariant();
                    if (!string.IsNullOrEmpty(slug) && fn.Contains(slug)) return true;
                    if (!string.IsNullOrEmpty(id) && fn.Contains(id)) return true;
                    if (!string.IsNullOrEmpty(title) && fn.Contains(title.Replace(" ", "-"))) return true;
                }
            }
            return false;
        }

        var subDir = project.ProjectType switch
        {
            "resourcepack" => "resourcepacks",
            "shader" => "shaderpacks",
            _ => "mods"
        };
        var folder = Path.Combine(instance.Path, subDir);
        if (!Directory.Exists(folder)) return false;

        foreach (var file in Directory.GetFiles(folder))
        {
            var fn = Path.GetFileName(file).ToLowerInvariant();
            if (!string.IsNullOrEmpty(slug) && fn.Contains(slug)) return true;
            if (!string.IsNullOrEmpty(id) && fn.Contains(id)) return true;
            if (!string.IsNullOrEmpty(title) && fn.Contains(title.Replace(" ", "-"))) return true;
        }
        return false;
    }

    private async Task InstallLatestAsync(ModrinthProjectItemViewModel project)
    {
        if (project == null || project.IsInstalling) return;
        var cts = new CancellationTokenSource();
        _installCts[project.ProjectId] = cts;
        var token = cts.Token;
        try
        {
            if (project.Source == ContentSource.CurseForge)
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                project.IsInstalling = true;
                OnPropertyChanged(nameof(ReinstallText));

                if (project.ProjectType == "world" || SelectedProjectType == "world")
                {
                    StatusText = "Fetching map details...";
                    var fileInfo = await CurseForgeApiService.GetLatestMapFileAsync(project.ProjectId, SelectedVersion, apiKey, token);
                    if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                    {
                        StatusText = $"No downloadable map file found for {project.Title}";
                        project.IsInstalling = false;
                        return;
                    }
                    StatusText = "Downloading map...";
                    var targetInstance = _instanceService.GetSelectedInstance();
                    if (targetInstance == null)
                    {
                        StatusText = "Please create or select a profile first.";
                        project.IsInstalling = false;
                        return;
                    }
                    var savesDir = Path.Combine(targetInstance.Path, "saves");
                    Directory.CreateDirectory(savesDir);
                    var tempZip = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{fileInfo.FileName}");
                    var mapData = await CurseForgeApiService.DownloadAsync(fileInfo.DownloadUrl, token);
                    if (mapData is not { Length: > 0 })
                    {
                        StatusText = "Download failed.";
                        project.IsInstalling = false;
                        return;
                    }
                    await File.WriteAllBytesAsync(tempZip, mapData, token);
                    StatusText = "Extracting map...";
                    try
                    {
                        System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, savesDir, overwriteFiles: true);
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Error("Failed to extract map", ex);
                    }
                    finally
                    {
                        try { File.Delete(tempZip); } catch { }
                    }
                    project.IsInstalling = false;
                    project.IsInstalled = true;
                    StatusText = $"Installed map {project.Title} to {targetInstance.Name}";
                    return;
                }
                else if (project.ProjectType == "modpack" || SelectedProjectType == "modpack")
                {
                    StatusText = $"Fetching {project.Title} modpack details...";
                    var fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, SelectedVersion, null, apiKey, token);
                    if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                    {
                        fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, null, null, apiKey, token);
                    }
                    if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                    {
                        StatusText = $"No downloadable file found for {project.Title}";
                        project.IsInstalling = false;
                        return;
                    }

                    StatusText = $"Downloading modpack {fileInfo.FileName}...";
                    var cfTempDir = Path.Combine(Path.GetTempPath(), $"zenith_modpack_{Guid.NewGuid():N}");
                    Directory.CreateDirectory(cfTempDir);
                    var cfZipPath = Path.Combine(cfTempDir, fileInfo.FileName);

                    var cfData = await CurseForgeApiService.DownloadAsync(fileInfo.DownloadUrl, token);
                    if (token.IsCancellationRequested) return;
                    if (cfData is not { Length: > 0 })
                    {
                        StatusText = "Download failed.";
                        project.IsInstalling = false;
                        return;
                    }
                    await File.WriteAllBytesAsync(cfZipPath, cfData, token);

                    var cfInstance = await InstallModpackFromZipAsync(project, cfZipPath, cfTempDir, fileInfo.FileName, token);
                    if (cfInstance != null)
                    {
                        project.IsInstalled = true;
                        StatusText = $"Installed {project.Title}";
                        UpdateInstalledFlags();
                        _onInstalled?.Invoke();
                    }
                    return;
                }
                else
                {
                    var targetInstance = _instanceService.GetSelectedInstance();
                    if (targetInstance == null)
                    {
                        StatusText = "Please create or select a profile first.";
                        project.IsInstalling = false;
                        return;
                    }

                    StatusText = $"Fetching {project.Title} details...";
                    var fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, targetInstance.Version, targetInstance.LoaderType, apiKey, token);
                    if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                    {
                        fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, targetInstance.Version, null, apiKey, token);
                    }
                    if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                    {
                        fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, null, null, apiKey, token);
                    }
                    if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                    {
                        StatusText = $"No compatible file found for {project.Title}";
                        project.IsInstalling = false;
                        return;
                    }

                    StatusText = $"Downloading {fileInfo.FileName}...";
                    var cfSubDir = (project.ProjectType ?? SelectedProjectType) switch
                    {
                        "resourcepack" => "resourcepacks",
                        "shader" => "shaderpacks",
                        "datapack" => Path.Combine("saves", "datapacks"),
                        _ => "mods"
                    };
                    var cfTargetDir = Path.Combine(targetInstance.Path, cfSubDir);
                    Directory.CreateDirectory(cfTargetDir);
                    var cfDestPath = Path.Combine(cfTargetDir, fileInfo.FileName);

                    var cfItemData = await CurseForgeApiService.DownloadAsync(fileInfo.DownloadUrl, token);
                    if (token.IsCancellationRequested) return;
                    if (cfItemData is not { Length: > 0 })
                    {
                        StatusText = "Download failed.";
                        project.IsInstalling = false;
                        return;
                    }
                    await File.WriteAllBytesAsync(cfDestPath, cfItemData, token);
                    project.IsInstalling = false;
                    project.IsInstalled = true;
                    StatusText = $"Installed {project.Title} to {targetInstance.Name}";
                    return;
                }
            }

            if (project.ProjectType == "world" || SelectedProjectType == "world")
            {
                // Fallback for Modrinth worlds if any
                var worldVersion = await ModrinthApiService.GetLatestVersionAsync(project.ProjectId, SelectedVersion, Array.Empty<string>(), token);
                if (worldVersion == null)
                {
                    StatusText = $"No compatible version found for {project.Title}";
                    return;
                }
                return;
            }
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

            if (SelectedProjectType != "modpack")
            {
                var targetInstance = _instanceService.GetSelectedInstance();
                if (targetInstance == null)
                {
                    StatusText = "Please create or select a profile first.";
                    project.IsInstalling = false;
                    return;
                }
                var subDir = SelectedProjectType switch
                {
                    "mod" => "mods",
                    "resourcepack" => "resourcepacks",
                    "shader" => "shaderpacks",
                    "datapack" => Path.Combine("saves", "datapacks"),
                    _ => "mods"
                };
                var targetDir = Path.Combine(targetInstance.Path, subDir);
                Directory.CreateDirectory(targetDir);
                var destPath = Path.Combine(targetDir, version.FileName);
                await File.WriteAllBytesAsync(destPath, data, token);
                project.IsInstalling = false;
                project.IsInstalled = true;
                StatusText = $"Installed {project.Title} to {targetInstance.Name}";
                return;
            }

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
        try { FullProject?.Dispose(); FullProject = null; } catch { }
        try { HeaderImage?.Dispose(); HeaderImage = null; } catch { }
        SelectedProject = null;
        PendingDelete = null;
        MemoryTrimmer.TrimMemory();
    }

    public void Dispose()
    {
        try { _searchCts?.Cancel(); _searchCts?.Dispose(); _searchCts = null; } catch { }
        try { _reinstallCts?.Cancel(); _reinstallCts?.Dispose(); _reinstallCts = null; } catch { }
        try { CurrentAuthorProfileVm?.Dispose(); CurrentAuthorProfileVm = null; } catch { }
        try { FullProject?.Dispose(); FullProject = null; } catch { }
        try { HeaderImage?.Dispose(); HeaderImage = null; } catch { }
        SelectedProject = null;
        PendingDelete = null;
        foreach (var r in Results)
        {
            try { r.Dispose(); } catch { }
        }
        Results.Clear();
        ClearTargetWorlds();
        MemoryTrimmer.TrimMemory();
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
            if (project.Source == ContentSource.CurseForge || project.ProjectType == "world")
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var cfProject = await CurseForgeApiService.GetFullProjectAsync(project.ProjectId, apiKey);
                FullProject = cfProject;
                HeaderImage = null;
                OnPropertyChanged(nameof(CleanBody));
                OnPropertyChanged(nameof(GalleryItems));
                OnPropertyChanged(nameof(HasGalleryItems));

                _allVersions = await CurseForgeApiService.GetProjectVersionsAsync(project.ProjectId, apiKey);
                if (cfProject != null)
                {
                    cfProject.Versions = _allVersions;
                    var actualVersions = (_allVersions.Count > 0
                        ? _allVersions.SelectMany(v => v.GameVersions ?? Array.Empty<string>())
                        : (cfProject.GameVersions ?? Array.Empty<string>()))
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Distinct();
                    FilterGameVersions = SortGameVersionsDesc(actualVersions);
                    GameVersionFilter.SetVersions(actualVersions);

                    var actualLoaders = (_allVersions.Count > 0
                        ? _allVersions.SelectMany(v => v.Loaders ?? Array.Empty<string>())
                        : (cfProject.Loaders ?? Array.Empty<string>()))
                        .Select(Models.ModrinthProjectVersion.FormatPlatformName)
                        .Where(p => !string.IsNullOrWhiteSpace(p) && !p.Equals("All platforms", StringComparison.OrdinalIgnoreCase))
                        .Distinct()
                        .OrderBy(p => p)
                        .ToList();

                    var platforms = new List<string> { "All platforms" };
                    platforms.AddRange(actualLoaders);
                    FilterPlatforms = platforms;
                    SelectedFilterGameVersion = "All versions";
                    SelectedFilterPlatform = "All platforms";

                    PlatformFilterItems.Clear();
                    foreach (var p in actualLoaders)
                    {
                        var item = new Models.FilterCheckItem { Name = p, IsSelected = false };
                        item.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName == nameof(Models.FilterCheckItem.IsSelected))
                            {
                                OnPropertyChanged(nameof(PlatformFilterButtonText));
                                OnPropertyChanged(nameof(IsAllPlatformsSelected));
                                CurrentVersionPage = 1;
                                RefreshPagination();
                            }
                        };
                        PlatformFilterItems.Add(item);
                    }
                    OnPropertyChanged(nameof(PlatformFilterButtonText));
                    OnPropertyChanged(nameof(IsAllPlatformsSelected));

                    CurrentVersionPage = 1;
                    RefreshPagination();
                    OnPropertyChanged(nameof(VersionSelectorProject));
                    OnPropertyChanged(nameof(CompatibilityVersions));

                    if (SelectedVersionForDetails == null && VersionList.Count > 0)
                        SelectedVersionForDetails = VersionList[0];
                }

                FullProjectBusy = false;
                return;
            }
            var full = await ModrinthApiService.GetFullProjectAsync(project.ProjectId);
            FullProject = full;
            HeaderImage = null;
            OnPropertyChanged(nameof(CleanBody));
            OnPropertyChanged(nameof(GalleryItems));
            OnPropertyChanged(nameof(HasGalleryItems));

            _allVersions = await ModrinthApiService.GetProjectVersionsAsync(project.ProjectId);
            if (full != null)
            {
                full.Versions = _allVersions;
                foreach (var g in full.Gallery)
                {
                    g.LoadImage();
                }
                foreach (var tm in full.TeamMembers)
                {
                    tm.LoadAvatar();
                }

                var memberMap = full.TeamMembers.Where(m => !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Username))
                                                .ToDictionary(m => m.Id, m => m.Username);
                foreach (var v in _allVersions)
                {
                    if (!string.IsNullOrWhiteSpace(v.AuthorId) && memberMap.TryGetValue(v.AuthorId, out var uname))
                        v.AuthorName = uname;
                    else
                        v.AuthorName = full.Author ?? project.Author;
                }

                SelectedDetailVersion = null;
                OnPropertyChanged(nameof(IsVersionDetailActive));

                var actualVersions = (_allVersions.Count > 0
                    ? _allVersions.SelectMany(v => v.GameVersions ?? Array.Empty<string>())
                    : (full.GameVersions ?? Array.Empty<string>()))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct();
                FilterGameVersions = SortGameVersionsDesc(actualVersions);
                GameVersionFilter.SetVersions(actualVersions);

                var actualLoaders = (_allVersions.Count > 0
                    ? _allVersions.SelectMany(v => v.Loaders ?? Array.Empty<string>())
                    : (full.Loaders ?? Array.Empty<string>()))
                    .Select(Models.ModrinthProjectVersion.FormatPlatformName)
                    .Where(p => !string.IsNullOrWhiteSpace(p) && !p.Equals("All platforms", StringComparison.OrdinalIgnoreCase))
                    .Distinct()
                    .OrderBy(p => p)
                    .ToList();

                var platforms = new List<string> { "All platforms" };
                platforms.AddRange(actualLoaders);
                FilterPlatforms = platforms;
                SelectedFilterGameVersion = "All versions";
                SelectedFilterPlatform = "All platforms";

                PlatformFilterItems.Clear();
                foreach (var p in actualLoaders)
                {
                    var item = new Models.FilterCheckItem { Name = p, IsSelected = false };
                    item.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(Models.FilterCheckItem.IsSelected))
                        {
                            OnPropertyChanged(nameof(PlatformFilterButtonText));
                            OnPropertyChanged(nameof(IsAllPlatformsSelected));
                            CurrentVersionPage = 1;
                            RefreshPagination();
                        }
                    };
                    PlatformFilterItems.Add(item);
                }
                OnPropertyChanged(nameof(PlatformFilterButtonText));
                OnPropertyChanged(nameof(IsAllPlatformsSelected));

                if (!string.IsNullOrWhiteSpace(full.HeaderImageUrl))
                {
                    var bannerUrl = full.HeaderImageUrl;
                    _ = Task.Run(async () =>
                    {
                        var bytes = await ModrinthApiService.GetIconAsync(bannerUrl);
                        if (bytes is { Length: > 0 })
                        {
                            using var ms = new System.IO.MemoryStream(bytes);
                            var bmp = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(ms, 1200);
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => HeaderImage = bmp);
                        }
                    });
                }

                CurrentVersionPage = 1;
                RefreshPagination();
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
        if (version == null) return;
        if (SelectedProject == null && FullProject != null)
        {
            SelectedProject = new ModrinthProjectItemViewModel
            {
                ProjectId = FullProject.Id,
                Title = FullProject.Title,
                Description = FullProject.Description,
                IconUrl = FullProject.IconUrl,
                Slug = FullProject.Slug,
                Author = FullProject.Author
            };
        }
        if (SelectedProject == null) return;
        IsVersionSelectorOpen = false;
        _ = InstallSpecificVersionAsync(SelectedProject, version);
    }

    private async Task InstallSpecificVersionAsync(ModrinthProjectItemViewModel project, Models.ModrinthProjectVersion version)
    {
        if (project == null || project.IsInstalling) return;
        if (IsDataPacksTab || string.Equals(project.ProjectType, "datapack", StringComparison.OrdinalIgnoreCase))
        {
            OpenAddToInstanceModal(project);
            return;
        }
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
            var data = project.Source == ContentSource.CurseForge
                ? await CurseForgeApiService.DownloadAsync(file.Url, token)
                : await ModrinthApiService.DownloadAsync(file.Url, token);
            if (token.IsCancellationRequested) return;
            if (data is not { Length: > 0 })
            {
                StatusText = "Download failed.";
                return;
            }
            await File.WriteAllBytesAsync(zipPath, data, token);

            if (SelectedProjectType != "modpack")
            {
                var targetInstance = _instanceService.GetSelectedInstance();
                if (targetInstance == null)
                {
                    StatusText = "Please create or select a profile first.";
                    project.IsInstalling = false;
                    return;
                }
                var subDir = SelectedProjectType switch
                {
                    "mod" => "mods",
                    "resourcepack" => "resourcepacks",
                    "shader" => "shaderpacks",
                    "datapack" => Path.Combine("saves", "datapacks"),
                    _ => "mods"
                };
                var targetDir = Path.Combine(targetInstance.Path, subDir);
                Directory.CreateDirectory(targetDir);
                var destPath = Path.Combine(targetDir, file.Filename);
                await File.WriteAllBytesAsync(destPath, data, token);
                project.IsInstalling = false;
                project.IsInstalled = true;
                StatusText = $"Installed {project.Title} to {targetInstance.Name}";
                return;
            }

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

        if (IsDataPacksTab || string.Equals(SelectedProject.ProjectType, "datapack", StringComparison.OrdinalIgnoreCase))
        {
            OpenAddToInstanceModal(SelectedProject);
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
    private async Task ConfirmDeleteAsync()
    {
        var project = PendingDelete;
        if (project == null) return;
        PendingDelete = null;
        await DeleteModpackAsync(project);
    }

    private async Task DeleteModpackAsync(ModrinthProjectItemViewModel project)
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
                await _instanceService.DeleteInstanceAsync(inst.Id);

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




