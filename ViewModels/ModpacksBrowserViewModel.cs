using ZenithLauncher.ViewModels.Dialogs;
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
using ZenithLauncher.Common.Utils;
using ZenithLauncher.Models;
using ZenithLauncher.Services;
using ZenithLauncher.ViewModels.Items;

namespace ZenithLauncher.ViewModels;

public partial class ModpacksBrowserViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private AuthorProfileViewModel? _currentAuthorProfileVm;

    partial void OnCurrentAuthorProfileVmChanging(AuthorProfileViewModel? value)
    {
        var oldAuthor = _currentAuthorProfileVm;
        if (oldAuthor != null && !ReferenceEquals(oldAuthor, value))
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { oldAuthor.Dispose(); } catch { }
            }, DispatcherPriority.Background);
        }
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
        OnPropertyChanged(nameof(WeatherContextKey));
        _ = Search();
    }

    public string WeatherContextKey => IsDetailsOpen ? "Catalog:Details" : SelectedProjectType switch
    {
        "mod" => "Catalog:Mods",
        "resourcepack" => "Catalog:ResourcePacks",
        "datapack" => "Catalog:DataPacks",
        "shader" => "Catalog:Shaders",
        "plugin" => "Catalog:Plugins",
        "world" => "Catalog:Worlds",
        _ => "Catalog:Modpacks"
    };

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

    private static string NormalizeKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"\s*[\(\[].*?[\)\]]", "");
        var chars = cleaned.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }

    private static List<ModrinthProjectItemViewModel> DeduplicateProjects(IEnumerable<ModrinthProjectItemViewModel> items)
    {
        var result = new List<ModrinthProjectItemViewModel>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in items)
        {
            if (p == null) continue;
            var titleKey = NormalizeKey(p.Title);
            var slugKey = NormalizeKey(p.Slug);

            if (!string.IsNullOrEmpty(p.ProjectId) && !seenIds.Add(p.ProjectId))
                continue;

            bool duplicate = false;
            if (!string.IsNullOrEmpty(titleKey) && !seenTitles.Add(titleKey))
                duplicate = true;

            if (!string.IsNullOrEmpty(slugKey) && !seenSlugs.Add(slugKey))
                duplicate = true;

            if (!duplicate)
            {
                result.Add(p);
            }
        }
        return result;
    }

    private static List<ModrinthProjectItemViewModel> MergeProjectResults(
        IReadOnlyList<ModrinthProjectItemViewModel>? mrHits,
        IReadOnlyList<ModrinthProjectItemViewModel>? cfHits,
        string sortOption)
    {
        var mrList = mrHits ?? Array.Empty<ModrinthProjectItemViewModel>();
        var cfList = cfHits ?? Array.Empty<ModrinthProjectItemViewModel>();

        List<ModrinthProjectItemViewModel> combined;
        if (string.Equals(sortOption, "Downloads", StringComparison.OrdinalIgnoreCase))
        {
            combined = mrList.Concat(cfList).OrderByDescending(p => p.Downloads).ToList();
        }
        else if (string.Equals(sortOption, "Newest", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(sortOption, "Recently Updated", StringComparison.OrdinalIgnoreCase))
        {
            combined = mrList.Concat(cfList).OrderByDescending(p => p.Updated).ToList();
        }
        else
        {
            combined = new List<ModrinthProjectItemViewModel>();
            int max = Math.Max(mrList.Count, cfList.Count);
            for (int i = 0; i < max; i++)
            {
                if (i < mrList.Count) combined.Add(mrList[i]);
                if (i < cfList.Count) combined.Add(cfList[i]);
            }
        }

        return DeduplicateProjects(combined);
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
        var oldProject = _fullProject;
        var oldHeader = HeaderImage;
        if (oldHeader != null)
            HeaderImage = null;
        if (oldProject != null || oldHeader != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { oldProject?.Dispose(); } catch { }
                try { oldHeader?.Dispose(); } catch { }
                try { SkiaSharp.SKGraphics.PurgeAllCaches(); } catch { }
            }, DispatcherPriority.Background);
        }
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

        if (FullProject?.Gallery != null)
        {
            if (IsGalleryTab)
            {
                foreach (var g in FullProject.Gallery)
                    g.LoadImage();
            }
            else
            {
                foreach (var g in FullProject.Gallery)
                    g.StopAnimation();
            }
        }
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
        StatusText = L10n.T("install_cancelled");
        OnPropertyChanged(nameof(ReinstallText));
    }

    /// <summary>Button text: "Install" for new content or different version, "Reinstall" only if the selected version is already installed.</summary>
    public string ReinstallText
    {
        get
        {
            if (_showInstallFeedback) return L10n.T("mi_installed");
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
        OnPropertyChanged(nameof(WeatherContextKey));
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

    public bool IsInitialLoading => IsBusy && Results.Count == 0;
    public bool IsEmpty => Results.Count == 0 && !IsBusy && !IsLoadingMore;
    public bool HasMore => _offset < _totalHits;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInitialLoading));
        OnPropertyChanged(nameof(IsEmpty));
    }

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
        // Defer version & catalog requests until Modpacks view is opened (fast cold start)
    }

    private int _versionsLoadStarted;

    public Task EnsureVersionsLoadedAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _versionsLoadStarted, 1) != 0)
            return Task.CompletedTask;
        return LoadVersionsAsync();
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
        CatalogFirstPageCache.Clear();
        _searchCts?.Cancel();
        SearchQuery = "";
        SelectedVersion = "";
        SelectedCategory = "";
        SelectedLoader = "";
        SelectedSort = "Relevance";
        BrowserVersionFilter.ClearCommand.Execute(null);
        BrowserLoaderFilter.ClearCommand.Execute(null);
        _ = ReloadAsync(bypassCache: true);
    }

    [RelayCommand]
    public Task Search()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        return ReloadAsync(_searchCts.Token);
    }

    private string BuildFirstPageCacheKey()
    {
        var vers = BrowserVersionFilter.SelectedVersions.Count > 0
            ? string.Join(",", BrowserVersionFilter.SelectedVersions)
            : SelectedVersion;
        var ldrs = string.Join(",", LoaderFacets);
        return $"mp|{SelectedSource}|{SelectedProjectType}|{SelectedSort}|{SearchQuery.Trim()}|{vers}|{SelectedCategory}|{ldrs}";
    }

    private void ReplaceResults(IEnumerable<ModrinthProjectItemViewModel> items)
    {
        foreach (var r in Results) { try { r.Dispose(); } catch { } }
        Results.Clear();
        foreach (var item in items)
            Results.Add(item);
        OnPropertyChanged(nameof(IsInitialLoading));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public async Task ReloadAsync(CancellationToken token = default, bool bypassCache = false)
    {
        var gen = Interlocked.Increment(ref _searchGeneration);
        var cacheKey = BuildFirstPageCacheKey();

        if (!bypassCache && CatalogFirstPageCache.TryGet(cacheKey, out var cached) && cached != null)
        {
            var restored = CatalogFirstPageCache.MaterializeCards(cached);
            ReplaceResults(restored);
            _totalHits = cached.TotalHits;
            _offset = cached.NextOffset;
            StatusText = cached.StatusText;
            IsBusy = false;
            UpdateInstalledFlags();
            foreach (var p in Results) LoadIcon(p, gen);
            OnPropertyChanged(nameof(IsInitialLoading));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasMore));
            return;
        }

        ReplaceResults(Array.Empty<ModrinthProjectItemViewModel>());
        IsBusy = true;
        StatusText = "";
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
                CatalogFirstPageCache.Set(cacheKey, Results, _totalHits, StatusText);
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
                CatalogFirstPageCache.Set(cacheKey, Results, _totalHits, StatusText);
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
            CatalogFirstPageCache.Set(cacheKey, Results, _totalHits, StatusText);
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
                OnPropertyChanged(nameof(IsInitialLoading));
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
                    var existingIds = Results.Select(r => r.ProjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var existingTitles = Results.Select(r => NormalizeKey(r.Title)).Where(k => !string.IsNullOrEmpty(k)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var existingSlugs = Results.Select(r => NormalizeKey(r.Slug)).Where(k => !string.IsNullOrEmpty(k)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in merged)
                    {
                        var tKey = NormalizeKey(p.Title);
                        var sKey = NormalizeKey(p.Slug);
                        if (existingIds.Add(p.ProjectId) &&
                            (string.IsNullOrEmpty(tKey) || existingTitles.Add(tKey)) &&
                            (string.IsNullOrEmpty(sKey) || existingSlugs.Add(sKey)))
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

    private void LoadIcon(ModrinthProjectItemViewModel project, int generation)
    {
        if (string.IsNullOrWhiteSpace(project.IconUrl) || project.HasIcon) return;
        _ = LoadIconAsync(project, generation);
    }

    private async Task LoadIconAsync(ModrinthProjectItemViewModel project, int generation)
    {
        try
        {
            var bytes = await ModrinthApiService.GetIconAsync(project.IconUrl);
            if (generation == _searchGeneration)
                project.SetIcon(bytes);
        }
        catch { }
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
    [ObservableProperty] private bool _filterCompatibleOnly;
    [ObservableProperty] private string _selectedAddGroup = "";
    [ObservableProperty] private ObservableCollection<string> _availableAddGroups = new();
    [ObservableProperty] private ObservableCollection<AddVersionOption> _availableAddVersions = new();
    [ObservableProperty] private AddVersionOption? _selectedAddVersion;
    [ObservableProperty] private bool _isLoadingAddVersions;
    [ObservableProperty] private string _addToInstanceStatusText = "";
    [ObservableProperty] private bool _isAddToInstanceStatusError;

    private CancellationTokenSource? _addVersionCts;
    private readonly Dictionary<string, HashSet<string>> _sessionInstalledFiles = new(StringComparer.OrdinalIgnoreCase);

    public string TargetInstanceNameForWorld => SelectedTargetInstanceForWorld?.Name ?? "";
    public string TargetInstanceVersionForWorld => SelectedTargetInstanceForWorld?.DisplayVersion ?? "";
    public string? TargetInstanceIconForWorld => SelectedTargetInstanceForWorld?.IconPath;
    public bool HasTargetInstanceIconForWorld => SelectedTargetInstanceForWorld?.HasCustomIcon ?? false;
    public string AddToInstanceModalTitle => IsSelectWorldStep ? L10n.T("modal_select_world") : L10n.T("modal_add_to_instance");
    public string SelectWorldSubtitle => string.Format(L10n.T("modal_select_world_sub"), TargetInstanceNameForWorld, ProjectForAddToInstance?.Title ?? "");
    public int CompatibleInstanceCount => TargetInstances.Count(i => !i.IsInstalled);
    public bool HasAnyProfileGroups => AvailableAddGroups.Count > 1;
    public string SelectedAddGroupLabel => string.IsNullOrWhiteSpace(SelectedAddGroup) ? L10n.T("add_inst_group_all") : SelectedAddGroup;
    public string SelectedAddVersionLabel => SelectedAddVersion?.Label ?? L10n.T("add_inst_version_auto");
    public bool HasAddToInstanceStatus => !string.IsNullOrWhiteSpace(AddToInstanceStatusText);
    public bool HasNoMatchingInstances => TargetInstances.Count == 0;

    public string ProjectSourceLabel => ProjectForAddToInstance?.IsCurseForge == true
        ? L10n.T("imp_src_curseforge")
        : L10n.T("imp_src_modrinth");

    public string ProjectCompatibilitySummary
    {
        get
        {
            if (ProjectForAddToInstance == null) return "";
            var vers = ProjectForAddToInstance.GameVersionsLabel;
            var loaders = ProjectForAddToInstance.LoadersLabel;
            if (!string.IsNullOrWhiteSpace(vers) && !string.IsNullOrWhiteSpace(loaders))
                return $"{vers} • {loaders}";
            return !string.IsNullOrWhiteSpace(vers) ? vers : loaders;
        }
    }

    public bool HasProjectCompatibilitySummary => !string.IsNullOrWhiteSpace(ProjectCompatibilitySummary);

    public string AddToInstanceSummaryText
    {
        get
        {
            if (IsSelectWorldStep)
                return string.Format(L10n.T("add_inst_worlds_count"), TargetWorlds.Count);

            var allCount = _instanceService.GetInstances().Count;
            var compatCount = TargetInstances.Count(i => i.IsCompatible);
            return string.Format(L10n.T("add_inst_count_status"), TargetInstances.Count, allCount, compatCount);
        }
    }

    public string WorldsCountLabel => string.Format(L10n.T("add_inst_worlds_count"), TargetWorlds.Count);

    partial void OnInstanceSearchQueryChanged(string value) => RefreshTargetInstances();
    partial void OnFilterCompatibleOnlyChanged(bool value) => RefreshTargetInstances();
    partial void OnSelectedAddGroupChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedAddGroupLabel));
        RefreshTargetInstances();
    }
    partial void OnSelectedAddVersionChanged(AddVersionOption? value)
    {
        OnPropertyChanged(nameof(SelectedAddVersionLabel));
        RefreshTargetInstances();
    }
    partial void OnAddToInstanceStatusTextChanged(string value) => OnPropertyChanged(nameof(HasAddToInstanceStatus));

    [RelayCommand]
    private void ClearInstanceSearch() => InstanceSearchQuery = "";

    [RelayCommand]
    private void SelectAddGroup(string? group)
    {
        SelectedAddGroup = string.IsNullOrWhiteSpace(group) || group == L10n.T("add_inst_group_all") ? "" : group.Trim();
    }

    [RelayCommand]
    private void SelectAddVersion(AddVersionOption? option)
    {
        if (option != null) SelectedAddVersion = option;
    }

    [RelayCommand]
    private void ResetAddInstanceFilters()
    {
        InstanceSearchQuery = "";
        FilterCompatibleOnly = false;
        SelectedAddGroup = "";
    }

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
        OnPropertyChanged(nameof(TargetInstanceVersionForWorld));
        OnPropertyChanged(nameof(TargetInstanceIconForWorld));
        OnPropertyChanged(nameof(HasTargetInstanceIconForWorld));
        OnPropertyChanged(nameof(AddToInstanceSummaryText));
        OnPropertyChanged(nameof(WorldsCountLabel));
    }

    [RelayCommand]
    public void CloseAddToInstance()
    {
        _addVersionCts?.Cancel();
        _addVersionCts?.Dispose();
        _addVersionCts = null;

        IsAddToInstanceOpen = false;
        IsSelectWorldStep = false;
        SelectedTargetInstanceForWorld = null;
        ClearTargetWorlds();
        ProjectForAddToInstance = null;
        TargetInstances.Clear();
        AvailableAddVersions.Clear();
        SelectedAddVersion = null;
        AddToInstanceStatusText = "";
        IsAddToInstanceStatusError = false;
        OnPropertyChanged(nameof(AddToInstanceModalTitle));
        OnPropertyChanged(nameof(IsSelectWorldStep));
    }

    [RelayCommand]
    public void OpenAddToInstanceModal(ModrinthProjectItemViewModel? project)
    {
        if (project == null) return;

        _addVersionCts?.Cancel();
        _addVersionCts?.Dispose();
        _addVersionCts = new CancellationTokenSource();

        ProjectForAddToInstance = project;
        InstanceSearchQuery = "";
        FilterCompatibleOnly = true;
        SelectedAddGroup = "";
        AddToInstanceStatusText = "";
        IsAddToInstanceStatusError = false;
        IsSelectWorldStep = false;
        SelectedTargetInstanceForWorld = null;
        ClearTargetWorlds();

        // Populate profile group filter options from existing profiles
        AvailableAddGroups.Clear();
        AvailableAddGroups.Add(L10n.T("add_inst_group_all"));
        foreach (var grp in _instanceService.GetInstances()
                     .Select(i => (i.Group ?? "").Trim())
                     .Where(g => !string.IsNullOrWhiteSpace(g))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
        {
            AvailableAddGroups.Add(grp);
        }
        OnPropertyChanged(nameof(HasAnyProfileGroups));
        OnPropertyChanged(nameof(SelectedAddGroupLabel));

        // Seed Auto version selector immediately, then load release list in background
        var autoOpt = new AddVersionOption(
            "",
            L10n.T("add_inst_version_auto"),
            "",
            "",
            "",
            Array.Empty<string>(),
            Array.Empty<string>(),
            IsAuto: true);
        AvailableAddVersions.Clear();
        AvailableAddVersions.Add(autoOpt);
        SelectedAddVersion = autoOpt;

        RefreshTargetInstances();
        OnPropertyChanged(nameof(ProjectSourceLabel));
        OnPropertyChanged(nameof(ProjectCompatibilitySummary));
        OnPropertyChanged(nameof(HasProjectCompatibilitySummary));
        OnPropertyChanged(nameof(AddToInstanceModalTitle));
        OnPropertyChanged(nameof(IsSelectWorldStep));
        OnPropertyChanged(nameof(AddToInstanceSummaryText));

        IsAddToInstanceOpen = true;
        _ = LoadAddVersionsAsync(project, _addVersionCts.Token);
    }

    private async Task LoadAddVersionsAsync(ModrinthProjectItemViewModel project, CancellationToken ct)
    {
        IsLoadingAddVersions = true;
        try
        {
            var fetched = new List<AddVersionOption>();
            if (project.Source == ContentSource.CurseForge)
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var cfFiles = await CurseForgeApiService.GetProjectFilesAsync(project.ProjectId, apiKey, ct);
                foreach (var f in cfFiles.Take(25))
                {
                    var cleanGvs = f.GameVersions
                        .Where(v => !string.IsNullOrWhiteSpace(v) &&
                                    !v.TrimStart('.').Equals("minecraft", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    var gv = string.Join(", ", cleanGvs.Take(4));
                    fetched.Add(new AddVersionOption(
                        f.FileId.ToString(),
                        f.DisplayName,
                        gv,
                        f.FileName,
                        f.DownloadUrl,
                        cleanGvs,
                        Array.Empty<string>(),
                        IsAuto: false));
                }
            }
            else
            {
                var versions = await ModrinthApiService.GetProjectVersionsAsync(project.ProjectId);
                ct.ThrowIfCancellationRequested();
                foreach (var v in versions.Take(30))
                {
                    var pf = v.Files.FirstOrDefault(f => f.IsPrimary) ?? v.Files.FirstOrDefault();
                    if (pf == null || string.IsNullOrWhiteSpace(pf.Url)) continue;
                    var cleanGvs = v.GameVersions
                        .Where(g => !string.IsNullOrWhiteSpace(g) &&
                                    !g.TrimStart('.').Equals("minecraft", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    var cleanLoaders = v.Loaders
                        .Where(l => !string.IsNullOrWhiteSpace(l) &&
                                    !l.TrimStart('.').Equals("minecraft", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    var gv = string.Join(", ", cleanGvs.Take(3));
                    var ld = string.Join(", ", cleanLoaders.Take(2));
                    var sub = !string.IsNullOrWhiteSpace(gv) && !string.IsNullOrWhiteSpace(ld)
                        ? $"{gv} • {ld}"
                        : (!string.IsNullOrWhiteSpace(gv) ? gv : ld);
                    var label = !string.IsNullOrWhiteSpace(v.VersionNumber) ? v.VersionNumber : v.Name;
                    fetched.Add(new AddVersionOption(
                        v.Id,
                        label,
                        sub,
                        pf.Filename,
                        pf.Url,
                        cleanGvs,
                        cleanLoaders,
                        IsAuto: false));
                }
            }

            if (ct.IsCancellationRequested || ProjectForAddToInstance != project) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested || ProjectForAddToInstance != project) return;
                var auto = AvailableAddVersions.FirstOrDefault(a => a.IsAuto);
                AvailableAddVersions.Clear();
                if (auto != null) AvailableAddVersions.Add(auto);
                foreach (var item in fetched) AvailableAddVersions.Add(item);
                if (SelectedAddVersion == null && auto != null) SelectedAddVersion = auto;
            });
        }
        catch { }
        finally
        {
            if (!ct.IsCancellationRequested)
                await Dispatcher.UIThread.InvokeAsync(() => IsLoadingAddVersions = false);
        }
    }

    private bool IsInstanceCompatible(InstanceModel inst, ModrinthProjectItemViewModel project)
    {
        var mcVer = (inst.Version ?? "").Trim();
        var verPool = SelectedAddVersion is { IsAuto: false, GameVersions.Length: > 0 }
            ? SelectedAddVersion.GameVersions
            : project.GameVersions;

        bool verOk = verPool == null || verPool.Length == 0 || string.IsNullOrEmpty(mcVer) ||
                     verPool.Any(v => v.Equals(mcVer, StringComparison.OrdinalIgnoreCase));
        if (!verOk) return false;

        var typeLower = (project.ProjectType ?? SelectedProjectType ?? "").ToLowerInvariant();
        if (typeLower is "mod" or "modpack")
        {
            var loaderPool = SelectedAddVersion is { IsAuto: false, Loaders.Length: > 0 }
                ? SelectedAddVersion.Loaders
                : project.Loaders;

            var meaningfulLoaders = (loaderPool ?? Array.Empty<string>())
                .Where(l => !string.IsNullOrWhiteSpace(l) &&
                            !l.Equals("minecraft", StringComparison.OrdinalIgnoreCase) &&
                            !l.Equals("datapack", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (meaningfulLoaders.Length > 0)
            {
                var instLoader = (inst.LoaderType ?? "Vanilla").Trim();
                if (instLoader.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!meaningfulLoaders.Any(l => l.Equals(instLoader, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }
        }

        return true;
    }

    private void RefreshTargetInstances()
    {
        if (ProjectForAddToInstance == null) return;
        TargetInstances.Clear();
        var allInstances = _instanceService.GetInstances();
        var q = InstanceSearchQuery?.Trim().ToLowerInvariant() ?? "";
        var grpFilter = (SelectedAddGroup ?? "").Trim();
        bool isDatapack = SelectedProjectType == "datapack" ||
                          string.Equals(ProjectForAddToInstance.ProjectType, "datapack", StringComparison.OrdinalIgnoreCase);

        var candidateList = new List<(InstanceModel Inst, bool Compatible, bool Installed)>();
        foreach (var inst in allInstances)
        {
            if (!string.IsNullOrEmpty(q) &&
                !inst.Name.ToLowerInvariant().Contains(q) &&
                !(inst.Version ?? "").ToLowerInvariant().Contains(q) &&
                !(inst.LoaderType ?? "").ToLowerInvariant().Contains(q))
                continue;

            if (!string.IsNullOrEmpty(grpFilter) &&
                !string.Equals((inst.Group ?? "").Trim(), grpFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            bool isCompatible = IsInstanceCompatible(inst, ProjectForAddToInstance);
            if (FilterCompatibleOnly && !isCompatible)
                continue;

            bool isInst = isDatapack ? false : CheckIfProjectInstalledInInstance(inst, ProjectForAddToInstance);
            candidateList.Add((inst, isCompatible, isInst));
        }

        foreach (var (inst, isCompatible, isInst) in candidateList
                     .OrderByDescending(x => x.Compatible)
                     .ThenBy(x => x.Installed)
                     .ThenBy(x => x.Inst.Name, StringComparer.OrdinalIgnoreCase))
        {
            TargetInstances.Add(new InstanceTargetItem(
                inst,
                isInst,
                isDatapack,
                isCompatible,
                async (target) =>
                {
                    if (ProjectForAddToInstance == null) return;
                    if (isDatapack)
                    {
                        OpenSelectWorldStep(target.Instance);
                    }
                    else
                    {
                        try
                        {
                            IsAddToInstanceStatusError = false;
                            AddToInstanceStatusText = "";
                            await InstallProjectToInstanceAsync(ProjectForAddToInstance, target.Instance);
                            OnPropertyChanged(nameof(CompatibleInstanceCount));
                            OnPropertyChanged(nameof(AddToInstanceSummaryText));
                        }
                        catch (Exception ex)
                        {
                            IsAddToInstanceStatusError = true;
                            AddToInstanceStatusText = ex.Message;
                            throw;
                        }
                    }
                },
                async (target) =>
                {
                    if (ProjectForAddToInstance == null) return;
                    try
                    {
                        IsAddToInstanceStatusError = false;
                        AddToInstanceStatusText = "";
                        await UninstallProjectFromInstanceAsync(ProjectForAddToInstance, target.Instance);
                        OnPropertyChanged(nameof(CompatibleInstanceCount));
                        OnPropertyChanged(nameof(AddToInstanceSummaryText));
                    }
                    catch (Exception ex)
                    {
                        IsAddToInstanceStatusError = true;
                        AddToInstanceStatusText = ex.Message;
                        throw;
                    }
                }));
        }
        OnPropertyChanged(nameof(CompatibleInstanceCount));
        OnPropertyChanged(nameof(HasNoMatchingInstances));
        OnPropertyChanged(nameof(AddToInstanceSummaryText));
    }

    public void OpenSelectWorldStep(InstanceModel instance)
    {
        SelectedTargetInstanceForWorld = instance;
        ClearTargetWorlds();
        AddToInstanceStatusText = "";
        IsAddToInstanceStatusError = false;

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
                var metaParts = new List<string>();
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
                            if (level.TryGetValue("GameType", out var gtObj) && gtObj is IConvertible gtConv)
                            {
                                var mode = Convert.ToInt32(gtConv) switch
                                {
                                    1 => "Creative",
                                    2 => "Adventure",
                                    3 => "Spectator",
                                    _ => "Survival"
                                };
                                metaParts.Add(mode);
                            }
                            if (level.TryGetValue("LastPlayed", out var lpObj) && lpObj is IConvertible lpConv)
                            {
                                var lpMs = Convert.ToInt64(lpConv);
                                if (lpMs > 0)
                                {
                                    var dt = DateTimeOffset.FromUnixTimeMilliseconds(lpMs).LocalDateTime;
                                    metaParts.Add(dt.ToString("dd.MM.yyyy"));
                                }
                            }
                        }
                    }
                }
                catch { }

                var dpFolder = Path.Combine(worldDir, "datapacks");
                bool isInstalledInWorld = ProjectForAddToInstance != null &&
                    FindMatchingProjectFiles(dpFolder, ProjectForAddToInstance).Count > 0;

                var worldItem = new WorldTargetItem(
                    folderName,
                    worldDisplayName,
                    worldDir,
                    string.Join(" • ", metaParts),
                    isInstalledInWorld,
                    async (w) =>
                    {
                        if (ProjectForAddToInstance != null)
                        {
                            try
                            {
                                IsAddToInstanceStatusError = false;
                                AddToInstanceStatusText = "";
                                await InstallDatapackToWorldAsync(ProjectForAddToInstance, instance, w.FolderName);
                            }
                            catch (Exception ex)
                            {
                                IsAddToInstanceStatusError = true;
                                AddToInstanceStatusText = ex.Message;
                                throw;
                            }
                        }
                    },
                    async (w) =>
                    {
                        if (ProjectForAddToInstance != null)
                        {
                            try
                            {
                                IsAddToInstanceStatusError = false;
                                AddToInstanceStatusText = "";
                                await UninstallDatapackFromWorldAsync(ProjectForAddToInstance, instance, w.FolderName);
                            }
                            catch (Exception ex)
                            {
                                IsAddToInstanceStatusError = true;
                                AddToInstanceStatusText = ex.Message;
                                throw;
                            }
                        }
                    });
                TargetWorlds.Add(worldItem);
            }
        }

        HasNoWorldsFound = TargetWorlds.Count == 0;
        IsSelectWorldStep = true;
        OnPropertyChanged(nameof(TargetInstanceNameForWorld));
        OnPropertyChanged(nameof(TargetInstanceVersionForWorld));
        OnPropertyChanged(nameof(TargetInstanceIconForWorld));
        OnPropertyChanged(nameof(HasTargetInstanceIconForWorld));
        OnPropertyChanged(nameof(AddToInstanceModalTitle));
        OnPropertyChanged(nameof(SelectWorldSubtitle));
        OnPropertyChanged(nameof(IsSelectWorldStep));
        OnPropertyChanged(nameof(AddToInstanceSummaryText));
        OnPropertyChanged(nameof(WorldsCountLabel));
    }

    private IProgress<double> CreateDownloadProgress(string fileName)
    {
        int lastPct = -1;
        return new Progress<double>(p =>
        {
            var pct = Math.Clamp((int)(p * 100), 0, 100);
            if (pct == lastPct || (pct < 100 && pct - lastPct < 5)) return;
            lastPct = pct;
            var msg = string.Format(L10n.T("mp_downloading"), $"{fileName} ({pct}%)");
            if (Dispatcher.UIThread.CheckAccess())
                StatusText = msg;
            else
                Dispatcher.UIThread.Post(() => StatusText = msg);
        });
    }

    private async Task<bool> StreamDownloadAsync(
        IReadOnlyList<string> urls,
        string destPath,
        string displayName,
        CancellationToken ct,
        int maxRounds = 2)
    {
        try
        {
            await ResilientDownloadService.DownloadFileAsync(
                urls,
                destPath,
                ct: ct,
                progress: CreateDownloadProgress(displayName),
                maxRounds: maxRounds);
            return File.Exists(destPath) && new FileInfo(destPath).Length > 0;
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(destPath + ".part")) File.Delete(destPath + ".part"); } catch { }
            throw;
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Streaming download failed ({displayName})", ex);
            return false;
        }
    }

    private async Task<(string fileName, string destPath)> DownloadProjectArtifactToFileAsync(
        ModrinthProjectItemViewModel project,
        InstanceModel instance,
        bool includeLoaderFilter,
        Func<string, string> resolveDestPath,
        CancellationToken ct = default)
    {
        string fileUrl;
        string fileName;

        if (SelectedAddVersion is { IsAuto: false } picked &&
            !string.IsNullOrWhiteSpace(picked.FileUrl) &&
            !string.IsNullOrWhiteSpace(picked.FileName))
        {
            fileUrl = picked.FileUrl;
            fileName = picked.FileName;
        }
        else if (project.Source == ContentSource.CurseForge)
        {
            var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
            var loader = includeLoaderFilter ? instance.LoaderType : null;
            var fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, instance.Version, loader, apiKey, ct);
            if ((fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl)) && !string.IsNullOrWhiteSpace(loader))
                fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, instance.Version, null, apiKey, ct);
            if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, null, null, apiKey, ct);
            if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                throw new Exception($"No downloadable file found on CurseForge for {project.Title}.");

            fileUrl = fileInfo.DownloadUrl;
            fileName = fileInfo.FileName;
        }
        else
        {
            ct.ThrowIfCancellationRequested();
            var mcVer = instance.Version;
            var mrLoader = (instance.LoaderType ?? "").ToLowerInvariant();
            var versions = await ModrinthApiService.GetProjectVersionsAsync(
                project.ProjectId,
                includeLoaderFilter && !string.IsNullOrWhiteSpace(mrLoader) && mrLoader != "vanilla" ? new[] { mrLoader } : null,
                !string.IsNullOrWhiteSpace(mcVer) ? new[] { mcVer } : null);

            if (versions.Count == 0)
                versions = await ModrinthApiService.GetProjectVersionsAsync(project.ProjectId);
            ct.ThrowIfCancellationRequested();

            var targetVer = versions.FirstOrDefault(v => v.GameVersions.Contains(mcVer)) ?? versions.FirstOrDefault();
            if (targetVer == null || targetVer.Files == null || targetVer.Files.Count == 0)
                throw new Exception("No compatible version found.");

            var primaryFile = targetVer.Files.FirstOrDefault(f => f.IsPrimary) ?? targetVer.Files[0];
            if (string.IsNullOrWhiteSpace(primaryFile.Url))
                throw new Exception("No file URL.");

            fileUrl = primaryFile.Url;
            fileName = primaryFile.Filename;
        }

        var destPath = resolveDestPath(fileName);
        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        try
        {
            await ResilientDownloadService.DownloadFileAsync(
                new[] { fileUrl },
                destPath,
                ct: ct,
                progress: CreateDownloadProgress(fileName),
                maxRounds: 2);
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(destPath + ".part")) File.Delete(destPath + ".part"); } catch { }
            throw;
        }

        if (!File.Exists(destPath) || new FileInfo(destPath).Length == 0)
            throw new Exception("Download failed.");

        return (fileName, destPath);
    }

    private async Task InstallDatapackToWorldAsync(ModrinthProjectItemViewModel project, InstanceModel instance, string worldName)
    {
        var targetFolder = Path.Combine(instance.Path, "saves", worldName, "datapacks");
        Directory.CreateDirectory(targetFolder);
        var (fileName, _) = await DownloadProjectArtifactToFileAsync(
            project,
            instance,
            includeLoaderFilter: false,
            name => Path.Combine(targetFolder, name));
        TrackInstalledFile(targetFolder, project.ProjectId, fileName);

        project.IsInstalled = true;
    }

    private async Task InstallProjectToInstanceAsync(ModrinthProjectItemViewModel project, InstanceModel instance)
    {
        var pType = project.ProjectType ?? SelectedProjectType;
        var includeLoader = pType is "mod" or "modpack";
        var targetFolder = ResolveProjectTargetDir(instance, pType);
        Directory.CreateDirectory(targetFolder);
        var (fileName, dest) = await DownloadProjectArtifactToFileAsync(
            project,
            instance,
            includeLoaderFilter: includeLoader,
            name => Path.Combine(targetFolder, name));
        TrackInstalledFile(targetFolder, project.ProjectId, fileName);

        if (!string.IsNullOrWhiteSpace(project.IconUrl))
        {
            try
            {
                var iconBytes = await ModrinthApiService.GetIconAsync(project.IconUrl);
                if (iconBytes is { Length: > 0 })
                {
                    var cacheDir = Path.Combine(ZenithPaths.AppDataDir, "cache", "icons");
                    Directory.CreateDirectory(cacheDir);
                    await File.WriteAllBytesAsync(Path.Combine(cacheDir, Path.GetFileNameWithoutExtension(dest) + ".png"), iconBytes);
                    await File.WriteAllBytesAsync(Path.Combine(cacheDir, Path.GetFileName(dest) + ".png"), iconBytes);
                    await File.WriteAllBytesAsync(Path.Combine(cacheDir, $"{project.ProjectId}.png"), iconBytes);
                }
            }
            catch { }
        }

        project.IsInstalled = true;
    }

    private async Task UninstallProjectFromInstanceAsync(ModrinthProjectItemViewModel project, InstanceModel instance)
    {
        var pType = project.ProjectType ?? SelectedProjectType;
        var targetFolder = ResolveProjectTargetDir(instance, pType);
        await Task.Run(() =>
        {
            foreach (var file in FindMatchingProjectFiles(targetFolder, project))
            {
                try { File.Delete(file); } catch { }
            }
        });
        UntrackInstalledFolder(targetFolder, project.ProjectId);

        if (!_instanceService.GetInstances().Any(i => CheckIfProjectInstalledInInstance(i, project)))
            project.IsInstalled = false;
    }

    private async Task UninstallDatapackFromWorldAsync(ModrinthProjectItemViewModel project, InstanceModel instance, string worldName)
    {
        var targetFolder = Path.Combine(instance.Path, "saves", worldName, "datapacks");
        await Task.Run(() =>
        {
            foreach (var file in FindMatchingProjectFiles(targetFolder, project))
            {
                try { File.Delete(file); } catch { }
            }
        });
        UntrackInstalledFolder(targetFolder, project.ProjectId);

        if (!_instanceService.GetInstances().Any(i => CheckIfProjectInstalledInInstance(i, project)))
            project.IsInstalled = false;
    }

    private string ResolveProjectTargetDir(InstanceModel instance, string? projectType)
    {
        var subDir = (projectType ?? SelectedProjectType) switch
        {
            "resourcepack" => "resourcepacks",
            "shader" => "shaderpacks",
            "datapack" => Path.Combine("saves", "datapacks"),
            _ => "mods"
        };
        return Path.Combine(instance.Path, subDir);
    }

    private void TrackInstalledFile(string folder, string projectId, string fileName)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(fileName)) return;
        var key = $"{projectId}|{folder}";
        if (!_sessionInstalledFiles.TryGetValue(key, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _sessionInstalledFiles[key] = set;
        }
        set.Add(fileName);
    }

    private void UntrackInstalledFolder(string folder, string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return;
        _sessionInstalledFiles.Remove($"{projectId}|{folder}");
    }

    private List<string> FindMatchingProjectFiles(string folder, ModrinthProjectItemViewModel project)
    {
        var matches = new List<string>();
        if (!Directory.Exists(folder) || project == null) return matches;

        var slug = project.Slug?.ToLowerInvariant() ?? "";
        var title = project.Title?.ToLowerInvariant() ?? "";
        var titleCompact = title.Replace(" ", "").Replace("-", "");
        var titleDashed = title.Replace(" ", "-");
        var id = project.ProjectId?.ToLowerInvariant() ?? "";

        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_sessionInstalledFiles.TryGetValue($"{project.ProjectId}|{folder}", out var tracked))
        {
            foreach (var t in tracked) knownNames.Add(t);
        }
        if (ProjectForAddToInstance?.ProjectId == project.ProjectId)
        {
            foreach (var opt in AvailableAddVersions)
            {
                if (!string.IsNullOrWhiteSpace(opt.FileName))
                    knownNames.Add(opt.FileName);
            }
        }

        foreach (var file in Directory.GetFiles(folder))
        {
            var rawName = Path.GetFileName(file);
            var baseName = rawName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? rawName[..^".disabled".Length]
                : rawName;
            var fn = baseName.ToLowerInvariant();
            var fnCompact = fn.Replace("-", "").Replace("_", "").Replace(" ", "");

            bool matched = knownNames.Contains(rawName) || knownNames.Contains(baseName)
                || (!string.IsNullOrEmpty(slug) && fn.Contains(slug))
                || (!string.IsNullOrEmpty(id) && fn.Contains(id))
                || (!string.IsNullOrEmpty(titleDashed) && fn.Contains(titleDashed))
                || (titleCompact.Length >= 4 && fnCompact.Contains(titleCompact));

            if (matched)
                matches.Add(file);
        }

        return matches;
    }

    private bool CheckIfProjectInstalledInInstance(InstanceModel instance, ModrinthProjectItemViewModel project)
    {
        if (project.ProjectType == "datapack")
        {
            var saves = Path.Combine(instance.Path, "saves");
            if (!Directory.Exists(saves)) return false;
            foreach (var worldDir in Directory.GetDirectories(saves))
            {
                if (FindMatchingProjectFiles(Path.Combine(worldDir, "datapacks"), project).Count > 0)
                    return true;
            }
            return false;
        }

        return FindMatchingProjectFiles(ResolveProjectTargetDir(instance, project.ProjectType), project).Count > 0;
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
                    StatusText = L10n.T("cb_fetching_map");
                    var fileInfo = await CurseForgeApiService.GetLatestMapFileAsync(project.ProjectId, SelectedVersion, apiKey, token);
                    if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                    {
                        StatusText = string.Format(L10n.T("cb_no_map_file"), project.Title);
                        project.IsInstalling = false;
                        return;
                    }
                    StatusText = L10n.T("mp_downloading_map");
                    var targetInstance = _instanceService.GetSelectedInstance();
                    if (targetInstance == null)
                    {
                        StatusText = L10n.T("mp_select_profile_first");
                        project.IsInstalling = false;
                        return;
                    }
                    var savesDir = Path.Combine(targetInstance.Path, "saves");
                    Directory.CreateDirectory(savesDir);
                    var tempZip = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{fileInfo.FileName}");
                    try
                    {
                        var mapDownloaded = await StreamDownloadAsync(new[] { fileInfo.DownloadUrl }, tempZip, fileInfo.FileName, token);
                        if (!mapDownloaded)
                        {
                            StatusText = L10n.T("mp_download_failed_plain");
                            project.IsInstalling = false;
                            return;
                        }
                        StatusText = L10n.T("mp_extracting_map");
                        try
                        {
                            System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, savesDir, overwriteFiles: true);
                        }
                        catch (Exception ex)
                        {
                            LauncherLog.Error("Failed to extract map", ex);
                        }
                    }
                    finally
                    {
                        try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                    }
                    project.IsInstalling = false;
                    project.IsInstalled = true;
                    StatusText = string.Format(L10n.T("mp_installed_to"), project.Title, targetInstance.Name);
                    return;
                }
                else if (project.ProjectType == "modpack" || SelectedProjectType == "modpack")
                {
                    StatusText = string.Format(L10n.T("mp_fetching_modpack"), project.Title);
                    var fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, SelectedVersion, null, apiKey, token);
                    if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                    {
                        fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, null, null, apiKey, token);
                    }
                    if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                    {
                        StatusText = string.Format(L10n.T("cb_no_download_file"), project.Title);
                        project.IsInstalling = false;
                        return;
                    }

                    StatusText = string.Format(L10n.T("mp_downloading"), fileInfo.FileName);
                    var cfTempDir = Path.Combine(Path.GetTempPath(), $"zenith_modpack_{Guid.NewGuid():N}");
                    Directory.CreateDirectory(cfTempDir);
                    var cfZipPath = Path.Combine(cfTempDir, fileInfo.FileName);

                    bool cfDownloaded;
                    try
                    {
                        cfDownloaded = await StreamDownloadAsync(new[] { fileInfo.DownloadUrl }, cfZipPath, fileInfo.FileName, token);
                    }
                    catch
                    {
                        try { Directory.Delete(cfTempDir, true); } catch { }
                        throw;
                    }
                    if (token.IsCancellationRequested || !cfDownloaded)
                    {
                        try { Directory.Delete(cfTempDir, true); } catch { }
                        if (!token.IsCancellationRequested && !cfDownloaded)
                            StatusText = L10n.T("mp_download_failed_plain");
                        project.IsInstalling = false;
                        return;
                    }

                    var cfInstance = await InstallModpackFromZipAsync(project, cfZipPath, cfTempDir, fileInfo.FileName, token);
                    if (cfInstance != null)
                    {
                        project.IsInstalled = true;
                        StatusText = string.Format(L10n.T("cb_installed_item"), project.Title);
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
                        StatusText = L10n.T("mp_select_profile_first");
                        project.IsInstalling = false;
                        return;
                    }

                    StatusText = string.Format(L10n.T("cb_fetching_details"), project.Title);
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
                        StatusText = string.Format(L10n.T("mp_no_compat_file"), project.Title);
                        project.IsInstalling = false;
                        return;
                    }

                    StatusText = string.Format(L10n.T("mp_downloading"), fileInfo.FileName);
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

                    var cfItemDownloaded = await StreamDownloadAsync(new[] { fileInfo.DownloadUrl }, cfDestPath, fileInfo.FileName, token);
                    if (token.IsCancellationRequested) return;
                    if (!cfItemDownloaded)
                    {
                        StatusText = L10n.T("mp_download_failed_plain");
                        project.IsInstalling = false;
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(project.IconUrl))
                    {
                        try
                        {
                            var iconBytes = await ModrinthApiService.GetIconAsync(project.IconUrl);
                            if (iconBytes is { Length: > 0 })
                            {
                                var cacheDir = Path.Combine(ZenithPaths.AppDataDir, "cache", "icons");
                                Directory.CreateDirectory(cacheDir);
                                await File.WriteAllBytesAsync(Path.Combine(cacheDir, Path.GetFileNameWithoutExtension(cfDestPath) + ".png"), iconBytes, token);
                                await File.WriteAllBytesAsync(Path.Combine(cacheDir, Path.GetFileName(cfDestPath) + ".png"), iconBytes, token);
                                await File.WriteAllBytesAsync(Path.Combine(cacheDir, $"{project.ProjectId}.png"), iconBytes, token);
                            }
                        }
                        catch { }
                    }

                    project.IsInstalling = false;
                    project.IsInstalled = true;
                    StatusText = string.Format(L10n.T("mp_installed_to"), project.Title, targetInstance.Name);
                    return;
                }
            }

            if (project.ProjectType == "world" || SelectedProjectType == "world")
            {
                // Fallback for Modrinth worlds if any
                var worldVersion = await ModrinthApiService.GetLatestVersionAsync(project.ProjectId, SelectedVersion, Array.Empty<string>(), token);
                if (worldVersion == null)
                {
                    StatusText = string.Format(L10n.T("mp_no_compat_ver"), project.Title);
                    return;
                }
                return;
            }
            var version = await ModrinthApiService.GetLatestVersionAsync(project.ProjectId, SelectedVersion, Array.Empty<string>(), token);
            if (version == null)
            {
                StatusText = string.Format(L10n.T("mp_no_compat_ver"), project.Title);
                return;
            }

            var file = version.FileUrl;
            if (string.IsNullOrWhiteSpace(file)) return;

            project.IsInstalling = true;
            OnPropertyChanged(nameof(ReinstallText));
            StatusText = L10n.T("mi_installing");

            if (SelectedProjectType != "modpack")
            {
                var targetInstance = _instanceService.GetSelectedInstance();
                if (targetInstance == null)
                {
                    StatusText = L10n.T("mp_select_profile_first");
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
                var downloaded = await StreamDownloadAsync(new[] { file }, destPath, version.FileName, token);
                if (token.IsCancellationRequested) return;
                if (!downloaded)
                {
                    StatusText = L10n.T("mp_download_failed_plain");
                    return;
                }
                project.IsInstalling = false;
                project.IsInstalled = true;
                StatusText = string.Format(L10n.T("mp_installed_to"), project.Title, targetInstance.Name);
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"zenith_modpack_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, version.FileName);
            bool zipDownloaded;
            try
            {
                zipDownloaded = await StreamDownloadAsync(new[] { file }, zipPath, version.FileName, token);
            }
            catch
            {
                try { Directory.Delete(tempDir, true); } catch { }
                throw;
            }
            if (token.IsCancellationRequested || !zipDownloaded)
            {
                try { Directory.Delete(tempDir, true); } catch { }
                if (!token.IsCancellationRequested && !zipDownloaded)
                    StatusText = L10n.T("mp_download_failed_plain");
                return;
            }

            var instance = await InstallModpackFromZipAsync(project, zipPath, tempDir, version.VersionNumber, token);
            if (instance != null)
            {
                project.IsInstalled = true;
                StatusText = string.Format(L10n.T("cb_installed_item"), project.Title);
                UpdateInstalledFlags();
                _onInstalled?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = L10n.T("install_cancelled");
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to quick-install modpack {project.Title}", ex);
            StatusText = string.Format(L10n.T("mp_install_failed"), ex.Message);
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

    private CancellationTokenSource? _fullProjectCts;

    [RelayCommand]
    private void CloseDetails()
    {
        try { _fullProjectCts?.Cancel(); _fullProjectCts?.Dispose(); _fullProjectCts = null; } catch { }
        _allVersions.Clear();
        PlatformFilterItems.Clear();
        SelectedDetailVersion = null;
        SelectedVersionForDetails = null;
        SelectedVersionToInstall = null;
        ActiveGalleryModalItem = null;
        SelectedCompatibilityVersion = "";
        SelectedProject = null;
        FullProject = null;
        HeaderImage = null;
        PendingDelete = null;
        Views.MainWindow.RequestBackgroundMemoryCleanup();
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _searchGeneration);
        try { _searchCts?.Cancel(); _searchCts?.Dispose(); _searchCts = null; } catch { }
        try { _reinstallCts?.Cancel(); _reinstallCts?.Dispose(); _reinstallCts = null; } catch { }
        try { _fullProjectCts?.Cancel(); _fullProjectCts?.Dispose(); _fullProjectCts = null; } catch { }
        try { if (_prevSelectedProject != null && _selectedProjectPropChanged != null) _prevSelectedProject.PropertyChanged -= _selectedProjectPropChanged; } catch { }
        _prevSelectedProject = null;
        _selectedProjectPropChanged = null;
        try { CurrentAuthorProfileVm?.Dispose(); CurrentAuthorProfileVm = null; } catch { }
        try { FullProject?.Dispose(); FullProject = null; } catch { }
        try { HeaderImage?.Dispose(); HeaderImage = null; } catch { }
        SelectedProject = null;
        ProjectForAddToInstance = null;
        SelectedTargetInstanceForWorld = null;
        AvailableAddVersions.Clear();
        TargetInstances.Clear();
        SelectedDetailVersion = null;
        SelectedVersionForDetails = null;
        SelectedVersionToInstall = null;
        ActiveGalleryModalItem = null;
        PendingDelete = null;
        _allVersions.Clear();
        PlatformFilterItems.Clear();
        foreach (var r in Results)
        {
            try { r.Dispose(); } catch { }
        }
        Results.Clear();
        ClearTargetWorlds();
        ModrinthApiService.ClearMemoryCache();
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
        _fullProjectCts?.Cancel();
        _fullProjectCts?.Dispose();
        _fullProjectCts = new CancellationTokenSource();
        var token = _fullProjectCts.Token;

        FullProjectBusy = true;
        try
        {
            if (project.Source == ContentSource.CurseForge || project.ProjectType == "world")
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var cfProject = await CurseForgeApiService.GetFullProjectAsync(project.ProjectId, apiKey);
                if (token.IsCancellationRequested)
                {
                    cfProject?.Dispose();
                    return;
                }
                FullProject = cfProject;
                HeaderImage = null;
                OnPropertyChanged(nameof(CleanBody));
                OnPropertyChanged(nameof(GalleryItems));
                OnPropertyChanged(nameof(HasGalleryItems));

                _allVersions = await CurseForgeApiService.GetProjectVersionsAsync(project.ProjectId, apiKey);
                if (token.IsCancellationRequested) return;
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
            if (token.IsCancellationRequested)
            {
                full?.Dispose();
                return;
            }
            FullProject = full;
            HeaderImage = null;
            OnPropertyChanged(nameof(CleanBody));
            OnPropertyChanged(nameof(GalleryItems));
            OnPropertyChanged(nameof(HasGalleryItems));

            _allVersions = await ModrinthApiService.GetProjectVersionsAsync(project.ProjectId);
            if (token.IsCancellationRequested) return;
            if (full != null)
            {
                full.Versions = _allVersions;
                if (IsGalleryTab)
                {
                    foreach (var g in full.Gallery)
                        g.LoadImage();
                }
                else if (full.Gallery.Count > 0)
                {
                    var previewItem = full.Gallery.FirstOrDefault(g => g.Featured) ?? full.Gallery[0];
                    previewItem.LoadImage();
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
                        if (token.IsCancellationRequested || bytes is not { Length: > 0 }) return;
                        using var ms = new System.IO.MemoryStream(bytes);
                        var bmp = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(ms, 1200);
                        if (token.IsCancellationRequested)
                        {
                            try { bmp.Dispose(); } catch { }
                            return;
                        }
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (token.IsCancellationRequested || !ReferenceEquals(FullProject, full))
                            {
                                try { bmp.Dispose(); } catch { }
                                return;
                            }
                            var old = HeaderImage;
                            HeaderImage = bmp;
                            try { old?.Dispose(); } catch { }
                        });
                    }, token);
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
            StatusText = L10n.T("mi_installing");
            if (SelectedProjectType != "modpack")
            {
                var targetInstance = _instanceService.GetSelectedInstance();
                if (targetInstance == null)
                {
                    StatusText = L10n.T("mp_select_profile_first");
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
                var downloaded = await StreamDownloadAsync(new[] { file.Url }, destPath, file.Filename, token);
                if (token.IsCancellationRequested) return;
                if (!downloaded)
                {
                    StatusText = L10n.T("mp_download_failed_plain");
                    return;
                }
                project.IsInstalling = false;
                project.IsInstalled = true;
                StatusText = string.Format(L10n.T("mp_installed_to"), project.Title, targetInstance.Name);
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"zenith_modpack_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, file.Filename);
            bool zipDownloaded;
            try
            {
                zipDownloaded = await StreamDownloadAsync(new[] { file.Url }, zipPath, file.Filename, token);
            }
            catch
            {
                try { Directory.Delete(tempDir, true); } catch { }
                throw;
            }
            if (token.IsCancellationRequested || !zipDownloaded)
            {
                try { Directory.Delete(tempDir, true); } catch { }
                if (!token.IsCancellationRequested && !zipDownloaded)
                    StatusText = L10n.T("mp_download_failed_plain");
                return;
            }

            var instance = await InstallModpackFromZipAsync(project, zipPath, tempDir, version.VersionNumber, token);
            if (instance != null)
            {
                project.IsInstalled = true;
                StatusText = string.Format(L10n.T("cb_installed_item"), project.Title);
                UpdateInstalledFlags();
                _onInstalled?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = L10n.T("install_cancelled");
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to install modpack {project.Title}", ex);
            StatusText = string.Format(L10n.T("mp_install_failed"), ex.Message);
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
                StatusText = L10n.T("mp_invalid_modpack");
                return null;
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to install modpack from zip", ex);
            StatusText = string.Format(L10n.T("mp_install_failed"), ex.Message);
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

        StatusText = string.Format(L10n.T("mp_creating_instance"), finalName);
        var inst = await _instanceService.CreateInstanceAsync(finalName, mcVersion, loaderType, loaderVersion);
        if (inst == null)
        {
            StatusText = string.Format(L10n.T("mp_create_failed"), packName);
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
            StatusText = string.Format(L10n.T("mp_installing_files_progress"), 0, total);

            using var sem = new System.Threading.SemaphoreSlim(6);

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

                    var urlList = new List<string>();
                    foreach (var dl in downloads.EnumerateArray())
                    {
                        token.ThrowIfCancellationRequested();
                        var url = dl.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                            urlList.Add(url);
                    }
                    if (urlList.Count > 0)
                    {
                        try
                        {
                            await ResilientDownloadService.DownloadFileAsync(urlList, destPath, ct: token, maxRounds: 2);
                        }
                        catch (OperationCanceledException)
                        {
                            try { if (File.Exists(destPath + ".part")) File.Delete(destPath + ".part"); } catch { }
                            throw;
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
                    StatusText = string.Format(L10n.T("mp_installing_files_progress"), count, total);
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
            StatusText = L10n.T("mp_invalid_modpack_ver");
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

        StatusText = string.Format(L10n.T("mp_creating_instance"), finalName);
        var inst = await _instanceService.CreateInstanceAsync(finalName, mcVersion, loaderType, loaderVersion);
        if (inst == null)
        {
            StatusText = string.Format(L10n.T("mp_create_failed"), packName);
            return null;
        }

        inst.ModpackId = project.ProjectId;
        inst.ModpackVersion = !string.IsNullOrWhiteSpace(installedVersion)
            ? installedVersion
            : (manifest.TryGetProperty("version", out var vProp) ? vProp.GetString() ?? "" : "");

        // Download and set modpack icon
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

                    var cacheDir = Path.Combine(ZenithPaths.AppDataDir, "cache", "icons");
                    Directory.CreateDirectory(cacheDir);
                    await File.WriteAllBytesAsync(Path.Combine(cacheDir, $"{inst.Name}.png"), iconBytes, token);
                    await File.WriteAllBytesAsync(Path.Combine(cacheDir, $"{project.ProjectId}.png"), iconBytes, token);
                }
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(inst.IconPath) || !File.Exists(inst.IconPath))
        {
            var localIcon = Directory.GetFiles(extractDir, "*icon*.png", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(extractDir, "*logo*.png", SearchOption.AllDirectories))
                .FirstOrDefault();
            if (localIcon != null && File.Exists(localIcon))
            {
                var destIcon = Path.Combine(inst.Path, "icon.png");
                try
                {
                    File.Copy(localIcon, destIcon, true);
                    inst.IconPath = destIcon;
                }
                catch { }
            }
        }

        var baseDir = Path.GetDirectoryName(manifestPath) ?? extractDir;
        var overridesDir = Path.Combine(baseDir, "overrides");
        if (manifest.TryGetProperty("overrides", out var ovProp) && !string.IsNullOrWhiteSpace(ovProp.GetString()))
            overridesDir = Path.Combine(baseDir, ovProp.GetString()!);

        if (Directory.Exists(overridesDir))
            CopyDirectoryRecursive(overridesDir, inst.Path);

        // Download mod files from manifest.json
        if (manifest.TryGetProperty("files", out var filesProp) && filesProp.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var fileIds = new List<long>();
            foreach (var f in filesProp.EnumerateArray())
            {
                if (f.TryGetProperty("fileID", out var fid) && fid.GetInt64() is long id && id > 0)
                    fileIds.Add(id);
            }

            if (fileIds.Count > 0)
            {
                StatusText = string.Format(L10n.T("mp_fetching_pack_files"), fileIds.Count);
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var filesInfo = await CurseForgeApiService.GetModFilesBatchAsync(fileIds, apiKey, token);
                var modsDir = Path.Combine(inst.Path, "mods");
                Directory.CreateDirectory(modsDir);

                int total = filesInfo.Count;
                int done = 0;
                StatusText = string.Format(L10n.T("mp_downloading_mods_progress"), 0, total);

                using var sem = new System.Threading.SemaphoreSlim(6);
                var tasks = filesInfo.Select(async info =>
                {
                    await sem.WaitAsync(token);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var destFile = Path.Combine(modsDir, info.FileName);
                        if (!File.Exists(destFile) && !string.IsNullOrWhiteSpace(info.DownloadUrl))
                        {
                            try
                            {
                                await ResilientDownloadService.DownloadFileAsync(new[] { info.DownloadUrl }, destFile, ct: token, maxRounds: 2);
                            }
                            catch (OperationCanceledException)
                            {
                                try { if (File.Exists(destFile + ".part")) File.Delete(destFile + ".part"); } catch { }
                                throw;
                            }
                        }
                        var count = System.Threading.Interlocked.Increment(ref done);
                        StatusText = string.Format(L10n.T("mp_downloading_mods_progress"), count, total);
                    }
                    finally
                    {
                        sem.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }
        }

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




