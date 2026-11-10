using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Common.Utils;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;
using CustomMcLauncher.ViewModels.Items;

namespace CustomMcLauncher.ViewModels;

public partial class ContentBrowserViewModel : ObservableObject, IDisposable
{
    [ObservableProperty]
    private AuthorProfileViewModel? _currentAuthorProfileVm;

    partial void OnCurrentAuthorProfileVmChanging(AuthorProfileViewModel? value)
    {
        _currentAuthorProfileVm?.Dispose();
    }

    public bool IsAuthorProfileOpen => CurrentAuthorProfileVm != null;

    public Action<bool>? OnAuthorProfileChanged { get; set; }

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

    private readonly string _gameVersion;
    private readonly string _loaderType;
    private readonly string _instancePath;
    private readonly Action<string> _reportStatus;
    private readonly Action _refreshAll;
    private readonly Func<bool> _hasModsSupport;
    private int _offset;
    private int _totalHits;
    private bool _showInstallFeedback;

    public ObservableCollection<ModrinthProjectItemViewModel> Results { get; } = new();
    public ObservableCollection<BrowserTag> AvailableTags { get; } = new();

    [ObservableProperty]
    private TagFilterViewModel _browserTagFilter = new();

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _searchCts?.Cancel(); } catch { }
        try { _searchCts?.Dispose(); _searchCts = null; } catch { }
        try { CurrentAuthorProfileVm?.Dispose(); CurrentAuthorProfileVm = null; } catch { }
        try { FullProject?.Dispose(); FullProject = null; } catch { }
        try { HeaderImage?.Dispose(); HeaderImage = null; } catch { }
        SelectedProject = null;
        foreach (var r in Results) { try { r.Dispose(); } catch { } }
        Results.Clear();
        MemoryTrimmer.TrimMemory();
        GC.SuppressFinalize(this);
    }

    private ContentType _initialType = ContentType.Mod;

    [ObservableProperty]
    private ContentType _selectedContentType = ContentType.Mod;

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
        ContentSortOption sortOption)
    {
        var mrList = mrHits ?? Array.Empty<ModrinthProjectItemViewModel>();
        var cfList = cfHits ?? Array.Empty<ModrinthProjectItemViewModel>();

        List<ModrinthProjectItemViewModel> combined;
        if (sortOption == ContentSortOption.Downloads)
        {
            combined = mrList.Concat(cfList).OrderByDescending(p => p.Downloads).ToList();
        }
        else if (sortOption is ContentSortOption.Newest or ContentSortOption.RecentlyUpdated)
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

    public CategoryFilterViewModel CategoryFilter { get; } = new();
    public SortFilterViewModel BrowserSortFilter { get; } = new();

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

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private string _selectedWorld = "";

    [ObservableProperty]
    private ModrinthProjectItemViewModel? _pendingDelete;

    [ObservableProperty]
    private ModrinthProjectItemViewModel? _selectedProject;

    public bool IsDetailsOpen => SelectedProject != null;

    /// <summary>Read-only surface the details overlay binds against.</summary>
    public ModrinthProjectItemViewModel? DetailsProject => SelectedProject;

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
            else if (!GameVersionFilter.ShowAllVersions && !ShowAllVersions && !string.IsNullOrWhiteSpace(_gameVersion))
            {
                filtered = filtered.Where(v => v.GameVersions != null && v.GameVersions.Any(gv => gv != null && gv.Contains(_gameVersion, StringComparison.OrdinalIgnoreCase)));
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

    /// <summary>Button text: "Install" for new content, "Reinstall" for already-installed same version, "Install" for different version.</summary>
    public string ReinstallText
    {
        get
        {
            if (_showInstallFeedback) return "Installed";
            if (SelectedProject is { IsInstalling: true })
            {
                return IsDetailsInstallButtonHovered ? L10n.T("btn_cancel") : L10n.T("mi_installing");
            }
            return SelectedProject is { IsInstalled: true }
                && (SelectedVersionForDetails == null
                    || string.Equals(SelectedVersionForDetails.VersionNumber, SelectedProject.InstalledVersion, StringComparison.OrdinalIgnoreCase))
                ? L10n.T("mi_reinstall")
                : L10n.T("mi_install");
        }
    }

    private bool _isLoadingByProjectId;
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
        SelectedCompatibilityVersion = _gameVersion;
        if (value != null && !_isLoadingByProjectId) _ = LoadFullProjectAsync(value);
        else if (value == null) FullProject = null;
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

    public bool IsReinstallConfirmOpen => false;

    public bool IsDeleteConfirmOpen => PendingDelete != null;

    public string DeleteItemName => PendingDelete?.Title ?? "";

    public string DeleteConfirmText =>
        PendingDelete == null ? "" : string.Format(L10n.T("cb_delete_confirm"), PendingDelete.Title);

    partial void OnPendingDeleteChanged(ModrinthProjectItemViewModel? value)
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
            var list = new List<ContentType> { ContentType.Mod, ContentType.ResourcePack, ContentType.Shader, ContentType.DataPack, ContentType.World };
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
    public Func<string>? GetCurseForgeApiKey { get; set; }

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
        _initialType = initialType;
        SelectedContentType = initialType;

        CategoryFilter.PopulateItems(initialType, HasModsSupport, HasShaderSupport);

        BrowserSourceFilter = new SourceFilterViewModel(SelectedSource, s =>
        {
            SelectSource(s switch { ContentSource.All => "all", ContentSource.CurseForge => "curseforge", _ => "modrinth" });
        });

        RefreshWorlds();
        UpdateAvailableTags();
        UpdateAvailableTagFilter();
        BrowserTagFilter.TagChanged += selectedTag => { _ = ReloadAsync(); };
        CategoryFilter.FilterChanged += () =>
        {
            if (CategoryFilter.SelectedTypes.Count == 1)
            {
                var single = CategoryFilter.SelectedTypes[0];
                if (_selectedContentType != single)
                {
                    _selectedContentType = single;
                    UpdateAvailableTags();
                    UpdateAvailableTagFilter();
                    OnPropertyChanged(nameof(SelectedContentType));
                    OnPropertyChanged(nameof(IsDataPackSelected));
                }
            }
            _ = ReloadAsync();
        };
        BrowserSortFilter.SortChanged += (newSort) =>
        {
            SelectedSort = newSort switch
            {
                "Downloads" => ContentSortOption.Downloads,
                "Newest" => ContentSortOption.Newest,
                "Recently Updated" => ContentSortOption.RecentlyUpdated,
                _ => ContentSortOption.Relevance
            };
        };
        GameVersionFilter.FilterChanged += () =>
        {
            CurrentVersionPage = 1;
            RefreshPagination();
            OnPropertyChanged(nameof(VersionList));
        };
        _ = ReloadAsync();
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "WorldMap" : clean;
    }

    private static bool MatchesCurseForgeCategory(ModrinthProjectItemViewModel project, IReadOnlyList<string> categoryTags)
    {
        if (categoryTags == null || categoryTags.Count == 0) return true;
        if (categoryTags.All(t => string.Equals(t, "All", StringComparison.OrdinalIgnoreCase))) return true;
        if (project.Categories == null || project.Categories.Length == 0) return false;

        foreach (var tag in categoryTags)
        {
            if (string.IsNullOrWhiteSpace(tag) || string.Equals(tag, "All", StringComparison.OrdinalIgnoreCase)) continue;
            var cleanTag = tag.Trim().ToLowerInvariant();

            foreach (var c in project.Categories)
            {
                var cleanC = c.Trim().ToLowerInvariant();
                if (cleanC.Contains(cleanTag) || cleanTag.Contains(cleanC)) return true;

                if (cleanTag == "realistic" && (cleanC.Contains("photo") || cleanC.Contains("realistic"))) return true;
                if (cleanTag is "vanilla" or "vanilla-like" && cleanC.Contains("vanilla")) return true;
                if (cleanTag == "performance" && (cleanC.Contains("performance") || cleanC.Contains("optimiz"))) return true;
                if (cleanTag == "utility" && (cleanC.Contains("utility") || cleanC.Contains("tool") || cleanC.Contains("addon"))) return true;
                if (cleanTag == "tweaks" && (cleanC.Contains("tweak") || cleanC.Contains("misc"))) return true;
            }
        }
        return false;
    }

    private static string[] MapLoaders(string loader) => loader.Trim().ToLowerInvariant() switch
    {
        "fabric" => new[] { "fabric" },
        "forge" => new[] { "forge" },
        "neoforge" => new[] { "neoforge" },
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
        UpdateAvailableTagFilter();
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
        UpdateAvailableTagFilter();
        CategoryFilter.SetSingleType(type);
    }

    private void UpdateAvailableTagFilter()
    {
        var tags = SelectedContentType switch
        {
            ContentType.Mod => new[] { "All", "adventure", "magic", "technology", "utility", "library", "optimization", "worldgen", "equipment" },
            ContentType.ResourcePack => new[] { "All", "16x", "32x", "48x", "64x", "128x", "256x", "512x+", "8x-", "realistic", "themed", "vanilla-like", "simplistic", "tweaks", "utility", "decoration" },
            ContentType.Shader => new[] { "All", "realistic", "fantasy", "vanilla", "vibrant", "soft", "performance" },
            ContentType.DataPack => new[] { "All", "utility", "adventure", "magic", "game-mechanics", "mobs", "optimization" },
            _ => new[] { "All" }
        };
        BrowserTagFilter.SetOptions(tags, "All");
    }

    private List<string> GetActiveCategoryTags()
    {
        var tag = BrowserTagFilter.SelectedTag;
        if (!string.IsNullOrWhiteSpace(tag) && !tag.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { tag };
        }
        return new List<string>();
    }

    private void UpdateAvailableTags()
    {
        AvailableTags.Clear();
        // Leading reset chip: clicking it clears the active category filter; active by default when nothing else is selected.
        AvailableTags.Add(new BrowserTag { Name = "All", IsReset = true, IsSelected = true });
        var tags = SelectedContentType switch
        {
            ContentType.Mod => new[] { "adventure", "magic", "technology", "utility", "library", "optimization", "worldgen", "equipment" },
            ContentType.ResourcePack => new[] { "16x", "32x", "48x", "64x", "128x", "256x", "512x+", "8x-", "realistic", "themed", "vanilla-like", "simplistic", "tweaks", "utility", "decoration" },
            ContentType.Shader => new[] { "realistic", "fantasy", "vanilla", "vibrant", "soft", "performance" },
            ContentType.DataPack => new[] { "utility", "adventure", "magic", "game-mechanics", "mobs", "optimization" },
            _ => Array.Empty<string>()
        };
        foreach (var t in tags) AvailableTags.Add(new BrowserTag { Name = t });
    }

    private void UpdateResetTagState()
    {
        var allTag = AvailableTags.FirstOrDefault(t => t.IsReset);
        if (allTag != null)
        {
            bool anyOtherSelected = AvailableTags.Any(t => !t.IsReset && t.IsSelected);
            allTag.IsSelected = !anyOtherSelected;
        }
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
            tag.IsSelected = true;
            if (changed) _ = ReloadAsync();
            return;
        }
        tag.IsSelected = !tag.IsSelected;
        UpdateResetTagState();
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = "";
    }

    [RelayCommand]
    private void ClearFilters()
    {
        ResetFilters();
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SearchQuery = "";
        BrowserTagFilter.SetSelected("All", notifyEvent: false);
        foreach (var t in AvailableTags) t.IsSelected = t.IsReset;
        SelectedSort = ContentSortOption.Relevance;
        SelectedSortDisplay = "Relevance";
        BrowserSortFilter.SelectedSort = "Relevance";
        CategoryFilter.PopulateItems(_initialType, HasModsSupport, HasShaderSupport);
        SelectedContentType = _initialType;
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
    private Task Search()
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
            var selectedTypes = CategoryFilter.SelectedTypes;
            if (selectedTypes.Count == 0)
                selectedTypes = new[] { SelectedContentType };

            var loaders = selectedTypes.Contains(ContentType.Mod) ? MapLoaders(_loaderType) : Array.Empty<string>();
            var categoryTags = GetActiveCategoryTags();
            var projectTypes = selectedTypes.Select(t => t.ModrinthProjectType()).Distinct().ToList();
            var sortIndex = MapSortToModrinth(SelectedSort);

            if (SelectedSource == ContentSource.All)
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var sortName = SelectedSort.ToString();
                var cfHits = new List<ModrinthProjectItemViewModel>();
                int cfTotal = 0;

                foreach (var ct in selectedTypes)
                {
                    var projType = ct.ModrinthProjectType();
                    var ldr = ct == ContentType.Mod ? _loaderType : null;
                    var res = await CurseForgeApiService.SearchProjectsAsync(
                        SearchQuery,
                        projType,
                        _gameVersion,
                        ldr,
                        0,
                        PageSize,
                        sortName,
                        apiKey,
                        token);

                    if (token.IsCancellationRequested || gen != _searchGeneration) return;
                    if (res != null)
                    {
                        cfTotal += res.TotalHits;
                        cfHits.AddRange(res.Hits);
                    }
                }

                if (categoryTags.Count > 0)
                {
                    cfHits = cfHits.Where(p => MatchesCurseForgeCategory(p, categoryTags)).ToList();
                }

                var mrPage = await ModrinthApiService.SearchAsync(SearchQuery, _gameVersion, loaders, projectTypes, sortIndex, categoryTags, 0, PageSize, token);

                if (token.IsCancellationRequested || gen != _searchGeneration) return;

                var merged = MergeProjectResults(mrPage?.Hits, cfHits, SelectedSort);
                _totalHits = (mrPage?.TotalHits ?? 0) + cfTotal;
                _offset = merged.Count;

                foreach (var p in merged.Take(PageSize))
                {
                    Results.Add(p);
                    LoadIcon(p, gen);
                }
                UpdateInstalledFlags();
                StatusText = Results.Count == 0
                    ? string.Format(L10n.T("cb_status_none"), "content")
                    : "";
                return;
            }

            if (SelectedSource == ContentSource.CurseForge || (selectedTypes.Count == 1 && selectedTypes[0] == ContentType.World))
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var sortName = SelectedSort.ToString();
                var hits = new List<ModrinthProjectItemViewModel>();
                int totalCount = 0;

                foreach (var ct in selectedTypes)
                {
                    var projType = ct.ModrinthProjectType();
                    var ldr = ct == ContentType.Mod ? _loaderType : null;
                    var res = await CurseForgeApiService.SearchProjectsAsync(
                        SearchQuery,
                        projType,
                        _gameVersion,
                        ldr,
                        0,
                        PageSize,
                        sortName,
                        apiKey,
                        token);

                    if (token.IsCancellationRequested || gen != _searchGeneration) return;
                    if (res != null)
                    {
                        totalCount += res.TotalHits;
                        hits.AddRange(res.Hits);
                    }
                }

                if (categoryTags.Count > 0)
                {
                    hits = hits.Where(p => MatchesCurseForgeCategory(p, categoryTags)).ToList();
                    totalCount = hits.Count;
                }

                _totalHits = totalCount;
                _offset = hits.Count;
                foreach (var p in hits.Take(PageSize))
                {
                    Results.Add(p);
                    LoadIcon(p, gen);
                }
                UpdateInstalledFlags();
                StatusText = Results.Count == 0
                    ? string.Format(L10n.T("cb_status_none"), "content")
                    : "";
                return;
            }

            var page = await ModrinthApiService.SearchAsync(SearchQuery, _gameVersion, loaders, projectTypes, sortIndex, categoryTags, 0, PageSize, token);

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
            StatusText = page.TotalHits == 0
                ? string.Format(L10n.T("cb_status_none"), "content")
                : "";
        }
        catch (OperationCanceledException)
        {
            // Ignored when superseded by newer search
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && gen == _searchGeneration)
            {
                LauncherLog.Error("Content search failed", ex);
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

    public async Task LoadMoreAsync()
    {
        if (IsBusy || IsLoadingMore || !HasMore) return;
        IsLoadingMore = true;
        var gen = _searchGeneration;
        try
        {
            var selectedTypes = CategoryFilter.SelectedTypes;
            if (selectedTypes.Count == 0)
                selectedTypes = new[] { SelectedContentType };

            var loaders = selectedTypes.Contains(ContentType.Mod) ? MapLoaders(_loaderType) : Array.Empty<string>();
            var categoryTags = GetActiveCategoryTags();
            var projectTypes = selectedTypes.Select(t => t.ModrinthProjectType()).Distinct().ToList();
            var sortIndex = MapSortToModrinth(SelectedSort);

            if (SelectedSource == ContentSource.All)
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var sortName = SelectedSort.ToString();
                var cfHits = new List<ModrinthProjectItemViewModel>();

                foreach (var ct in selectedTypes)
                {
                    var projType = ct.ModrinthProjectType();
                    var ldr = ct == ContentType.Mod ? _loaderType : null;
                    var res = await CurseForgeApiService.SearchProjectsAsync(
                        SearchQuery,
                        projType,
                        _gameVersion,
                        ldr,
                        _offset / 2,
                        PageSize,
                        sortName,
                        apiKey);

                    if (res != null && gen == _searchGeneration)
                    {
                        cfHits.AddRange(res.Hits);
                    }
                }

                var mrPage = await ModrinthApiService.SearchAsync(SearchQuery, _gameVersion, loaders, projectTypes, sortIndex, categoryTags, _offset / 2, PageSize);

                if (gen != _searchGeneration) return;

                var merged = MergeProjectResults(mrPage?.Hits, cfHits, SelectedSort);
                if (merged.Count == 0)
                {
                    _offset = _totalHits;
                }
                else
                {
                    _offset += merged.Count;
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

            if (SelectedSource == ContentSource.CurseForge || (selectedTypes.Count == 1 && selectedTypes[0] == ContentType.World))
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var sortName = SelectedSort.ToString();
                var hits = new List<ModrinthProjectItemViewModel>();

                foreach (var ct in selectedTypes)
                {
                    var projType = ct.ModrinthProjectType();
                    var ldr = ct == ContentType.Mod ? _loaderType : null;
                    var res = await CurseForgeApiService.SearchProjectsAsync(
                        SearchQuery,
                        projType,
                        _gameVersion,
                        ldr,
                        _offset,
                        PageSize,
                        sortName,
                        apiKey);

                    if (res != null && gen == _searchGeneration)
                    {
                        hits.AddRange(res.Hits);
                    }
                }

                if (hits.Count == 0)
                {
                    _offset = _totalHits;
                }
                else
                {
                    _offset += hits.Count;
                    var existing = Results.Select(r => r.ProjectId).ToHashSet();
                    foreach (var p in hits)
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

            var page = await ModrinthApiService.SearchAsync(SearchQuery, _gameVersion, loaders, projectTypes, sortIndex, categoryTags, _offset, PageSize);
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
            LauncherLog.Error("Content load-more failed", ex);
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
        // Install directly with the latest compatible version (no page navigation)
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
            var itemType = project.ProjectType?.ToLowerInvariant() switch
            {
                "mod" => ContentType.Mod,
                "resourcepack" => ContentType.ResourcePack,
                "shader" => ContentType.Shader,
                "datapack" => ContentType.DataPack,
                "world" => ContentType.World,
                _ => SelectedContentType
            };

            if (itemType == ContentType.World)
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                project.IsInstalling = true;
                StatusText = "Fetching map details...";
                var fileInfo = await CurseForgeApiService.GetLatestMapFileAsync(project.ProjectId, _gameVersion, apiKey, token);
                if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                {
                    StatusText = $"No downloadable map file found for {project.Title}";
                    project.IsInstalling = false;
                    return;
                }

                StatusText = $"Downloading {project.Title}...";
                var savesDir = Path.Combine(_instancePath, "saves");
                Directory.CreateDirectory(savesDir);

                var tempZip = Path.Combine(Path.GetTempPath(), $"{project.ProjectId}_{fileInfo.FileId}.zip");
                var zipBytes = await CurseForgeApiService.DownloadAsync(fileInfo.DownloadUrl, token);
                if (zipBytes == null || zipBytes.Length == 0)
                {
                    StatusText = $"Failed to download map file for {project.Title}";
                    project.IsInstalling = false;
                    return;
                }
                await File.WriteAllBytesAsync(tempZip, zipBytes, token);

                StatusText = $"Extracting map to saves folder...";
                var mapDirName = SanitizeFolderName(project.Title);
                var destMapDir = Path.Combine(savesDir, mapDirName);
                if (Directory.Exists(destMapDir))
                {
                    int counter = 1;
                    while (Directory.Exists($"{destMapDir} ({counter})")) counter++;
                    destMapDir = $"{destMapDir} ({counter})";
                }
                Directory.CreateDirectory(destMapDir);

                await Task.Run(() =>
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, destMapDir, true);
                    var subDirs = Directory.GetDirectories(destMapDir);
                    var rootFiles = Directory.GetFiles(destMapDir);
                    if (subDirs.Length == 1 && rootFiles.Length == 0 && File.Exists(Path.Combine(subDirs[0], "level.dat")))
                    {
                        var nested = subDirs[0];
                        foreach (var f in Directory.GetFiles(nested))
                            File.Move(f, Path.Combine(destMapDir, Path.GetFileName(f)), true);
                        foreach (var d in Directory.GetDirectories(nested))
                            Directory.Move(d, Path.Combine(destMapDir, Path.GetFileName(d)));
                        try { Directory.Delete(nested); } catch { }
                    }
                    try { File.Delete(tempZip); } catch { }
                });

                MarkInstalled(project.ProjectId, Path.GetFileName(destMapDir));
                project.IsInstalled = true;
                UpdateInstalledFlags();
                StatusText = $"Installed {project.Title} into worlds!";
                FlashInstalledFeedback();
                _refreshAll?.Invoke();
                return;
            }

            if (project.Source == ContentSource.CurseForge)
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                project.IsInstalling = true;
                StatusText = $"Fetching {project.Title} details...";
                var fileInfo = await CurseForgeApiService.GetLatestProjectFileAsync(project.ProjectId, _gameVersion, _loaderType, apiKey, token);
                if (fileInfo == null || string.IsNullOrWhiteSpace(fileInfo.DownloadUrl))
                {
                    StatusText = $"No downloadable file found for {project.Title}";
                    project.IsInstalling = false;
                    return;
                }

                StatusText = string.Format(L10n.T("mi_installing"));
                var targetDir = itemType.InstallFolder(_instancePath, SelectedWorld);
                Directory.CreateDirectory(targetDir);
                var destPath = Path.Combine(targetDir, fileInfo.FileName);

                var fileBytes = await CurseForgeApiService.DownloadAsync(fileInfo.DownloadUrl, token);
                if (fileBytes == null || fileBytes.Length == 0)
                {
                    StatusText = $"Failed to download {project.Title}";
                    project.IsInstalling = false;
                    return;
                }
                await File.WriteAllBytesAsync(destPath, fileBytes, token);

                MarkInstalled(project.ProjectId, fileInfo.FileName);
                project.IsInstalled = true;
                UpdateInstalledFlags();
                StatusText = $"Installed {project.Title}!";
                FlashInstalledFeedback();
                _refreshAll?.Invoke();
                return;
            }

            var loaders = SelectedContentType == ContentType.Mod ? MapLoaders(_loaderType) : Array.Empty<string>();
            var version = await ModrinthApiService.GetLatestVersionAsync(project.ProjectId, _gameVersion, loaders, token);
            if (version == null)
            {
                StatusText = string.Format(L10n.T("cb_download_failed"), project.Title);
                return;
            }

            var targetFolder = SelectedContentType.InstallFolder(_instancePath, SelectedWorld);
            if (string.IsNullOrWhiteSpace(version.FileUrl)) return;

            project.IsInstalling = true;
            OnPropertyChanged(nameof(ReinstallText));
            StatusText = string.Format(L10n.T("mi_installing"));

            var data = await ModrinthApiService.DownloadAsync(version.FileUrl, token);
            if (token.IsCancellationRequested) return;
            if (data is not { Length: > 0 })
            {
                StatusText = string.Format(L10n.T("cb_download_failed"), version.FileName);
                return;
            }

            var dest = Path.Combine(targetFolder, SanitizeFileName(version.FileName));
            Directory.CreateDirectory(targetFolder);
            await File.WriteAllBytesAsync(dest, data, token);

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
                        await File.WriteAllBytesAsync(cacheFile, iconBytes, token);
                        var cacheById = Path.Combine(cacheDir, $"{project.ProjectId}.png");
                        await File.WriteAllBytesAsync(cacheById, iconBytes, token);
                    }
                }
                catch { }
            }

            try { MarkInstalled(project.ProjectId, Path.GetFileName(dest)); } catch { }

            StatusText = string.Format(L10n.T("cb_installed_file"), project.Title, version.FileName);
            project.IsInstalled = true;
            project.InstalledVersion = version.VersionNumber;
            UpdateInstalledFlags();
            _refreshAll?.Invoke();
            _reportStatus?.Invoke(StatusText);
            FlashInstalledFeedback();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Installation cancelled.";
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Content quick-install failed for {project.Title}", ex);
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
        var cts = new CancellationTokenSource();
        _installCts[project.ProjectId] = cts;
        var token = cts.Token;
        try
        {
            var targetFolder = SelectedContentType.InstallFolder(_instancePath, SelectedWorld);
            var file = version.Files.FirstOrDefault(f => f.IsPrimary) ?? version.Files.FirstOrDefault();
            if (file == null || string.IsNullOrWhiteSpace(file.Url)) return;

            project.IsInstalling = true;
            OnPropertyChanged(nameof(ReinstallText));
            StatusText = string.Format(L10n.T("mi_installing"));

            var data = project.Source == ContentSource.CurseForge
                ? await CurseForgeApiService.DownloadAsync(file.Url, token)
                : await ModrinthApiService.DownloadAsync(file.Url, token);
            if (token.IsCancellationRequested) return;
            if (data is not { Length: > 0 })
            {
                StatusText = string.Format(L10n.T("cb_download_failed"), file.Filename);
                return;
            }

            var dest = Path.Combine(targetFolder, SanitizeFileName(file.Filename));
            Directory.CreateDirectory(targetFolder);
            await File.WriteAllBytesAsync(dest, data, token);

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
                        await File.WriteAllBytesAsync(cacheFile, iconBytes, token);
                        var cacheById = Path.Combine(cacheDir, $"{project.ProjectId}.png");
                        await File.WriteAllBytesAsync(cacheById, iconBytes, token);
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
            FlashInstalledFeedback();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Installation cancelled.";
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Content install failed for {project.Title}", ex);
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

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task InstallAsync(ModrinthProjectItemViewModel project)
    {
        if (project == null || project.IsInstalling) return;

        if (project.Source == ContentSource.CurseForge)
        {
            await InstallLatestAsync(project);
            return;
        }

        if (SelectedContentType == ContentType.DataPack && string.IsNullOrWhiteSpace(SelectedWorld))
        {
            StatusText = L10n.T("cb_select_world_first");
            return;
        }

        var cts = new CancellationTokenSource();
        _installCts[project.ProjectId] = cts;
        var token = cts.Token;
        project.IsInstalling = true;
        OnPropertyChanged(nameof(ReinstallText));
        Services.DiscordPresenceService.SetInstallingContent(project.Title);
        StatusText = string.Format(L10n.T("cb_resolving_for"), project.Title);
        try
        {
            var loaders = SelectedContentType == ContentType.Mod ? MapLoaders(_loaderType) : Array.Empty<string>();
            var version = await ModrinthApiService.GetLatestVersionAsync(project.ProjectId, _gameVersion, loaders, token);

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
            var data = await ModrinthApiService.DownloadAsync(version.FileUrl, token);
            if (token.IsCancellationRequested) return;
            if (data is not { Length: > 0 })
            {
                StatusText = string.Format(L10n.T("cb_download_failed"), version.FileName);
                return;
            }

            await File.WriteAllBytesAsync(dest, data, token);

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
                        await File.WriteAllBytesAsync(cacheFile, iconBytes, token);
                        var cacheById = Path.Combine(cacheDir, $"{project.ProjectId}.png");
                        await File.WriteAllBytesAsync(cacheById, iconBytes, token);
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
                        token.ThrowIfCancellationRequested();
                        await InstallDependencyAsync(dep, targetFolder);
                    }
                    StatusText = string.Format(L10n.T("cb_installed_deps"), project.Title, required.Count);
                    UpdateInstalledFlags();
                }
            }

            _refreshAll?.Invoke();
            _reportStatus?.Invoke(StatusText);
            FlashInstalledFeedback();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Installation cancelled.";
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Content install failed for {project.Title}", ex);
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
    private void OpenProjectPage(ModrinthProjectItemViewModel project)
    {
        SelectedProject = project;
    }

    /// <summary>Opens the full project details page by Modrinth project ID (used by installed-content double-click).</summary>
    public async Task OpenProjectByIdAsync(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return;
        try
        {
            FullProjectBusy = true;
            var full = await ModrinthApiService.GetFullProjectAsync(projectId);
            if (full == null) return;

            var project = new ModrinthProjectItemViewModel
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
                Updated = full.DateModified,
                IsInstalled = true
            };
            // Cache icon
            if (!string.IsNullOrWhiteSpace(full.IconUrl))
                _ = LoadIconAsync(project);

            _isLoadingByProjectId = true;
            SelectedProject = project;
            _isLoadingByProjectId = false;
            FullProject = full;
            OnPropertyChanged(nameof(CleanBody));
            _allVersions = await ModrinthApiService.GetProjectVersionsAsync(projectId);
            var memberMap = full?.TeamMembers?.Where(m => !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Username))
                                              .ToDictionary(m => m.Id, m => m.Username) ?? new Dictionary<string, string>();
            foreach (var v in _allVersions)
            {
                if (!string.IsNullOrWhiteSpace(v.AuthorId) && memberMap.TryGetValue(v.AuthorId, out var uname))
                    v.AuthorName = uname;
                else
                    v.AuthorName = full?.Author ?? project.Author;
            }
            var actualVersions = _allVersions
                .SelectMany(v => v.GameVersions ?? Array.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct();
            FilterGameVersions = SortGameVersionsDesc(actualVersions);
            GameVersionFilter.SetVersions(actualVersions);

            var actualLoaders = _allVersions
                .SelectMany(v => v.Loaders ?? Array.Empty<string>())
                .Select(Models.ModrinthFullProject.FormatPlatformName)
                .Where(p => !string.IsNullOrWhiteSpace(p) && !p.Equals("All platforms", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            var platforms = new List<string> { "All platforms" };
            platforms.AddRange(actualLoaders);
            FilterPlatforms = platforms;

            // Auto-select instance game version if present
            var matchingVer = GameVersionFilter.Items.FirstOrDefault(i => i.Name.Equals(_gameVersion, StringComparison.OrdinalIgnoreCase));
            if (matchingVer != null)
            {
                matchingVer.IsSelected = true;
                GameVersionFilter.NotifySelectionState();
            }

            var loaderName = Models.ModrinthFullProject.FormatPlatformName(_loaderType);
            var matchingPlatform = actualLoaders.FirstOrDefault(p => p.Equals(loaderName, StringComparison.OrdinalIgnoreCase));
            SelectedFilterPlatform = matchingPlatform ?? "All platforms";

            PlatformFilterItems.Clear();
            foreach (var p in actualLoaders)
            {
                var isSel = matchingPlatform != null && p.Equals(matchingPlatform, StringComparison.OrdinalIgnoreCase);
                var item = new Models.FilterCheckItem { Name = p, IsSelected = isSel };
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

            SelectedCompatibilityVersion = _gameVersion;
            CurrentVersionPage = 1;
            RefreshPagination();
            OnPropertyChanged(nameof(CompatibilityVersions));

            if (SelectedVersionForDetails == null && VersionList.Count > 0)
                SelectedVersionForDetails = VersionList[0];
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

    private async Task LoadIconAsync(ModrinthProjectItemViewModel project)
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
        // If a specific version is selected in the Versions tab, install that
        if (SelectedVersionForDetails != null)
        {
            InstallSelectedVersion(SelectedVersionForDetails);
            return;
        }
        // If not installed yet, install latest directly
        if (!SelectedProject.IsInstalled)
        {
            _ = InstallLatestAsync(SelectedProject);
            return;
        }
        // Otherwise reinstall: open version selector to pick a version
        OpenVersionSelector(SelectedProject);
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
            if (project.Source == ContentSource.CurseForge)
            {
                var apiKey = GetCurseForgeApiKey?.Invoke() ?? "";
                var cf = await CurseForgeApiService.GetFullProjectAsync(project.ProjectId, apiKey);
                FullProject = cf;
                HeaderImage = null;
                OnPropertyChanged(nameof(CleanBody));
                OnPropertyChanged(nameof(GalleryItems));
                OnPropertyChanged(nameof(HasGalleryItems));

                _allVersions = await CurseForgeApiService.GetProjectVersionsAsync(project.ProjectId, apiKey);
                if (cf != null)
                {
                    cf.Versions = _allVersions;
                    var actualVersions = (_allVersions.Count > 0
                        ? _allVersions.SelectMany(v => v.GameVersions ?? Array.Empty<string>())
                        : (cf.GameVersions ?? Array.Empty<string>()))
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Distinct();
                    FilterGameVersions = SortGameVersionsDesc(actualVersions);
                    GameVersionFilter.SetVersions(actualVersions);

                    var actualLoaders = (_allVersions.Count > 0
                        ? _allVersions.SelectMany(v => v.Loaders ?? Array.Empty<string>())
                        : (cf.Loaders ?? Array.Empty<string>()))
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

                // Auto-select instance game version if present
                var matchingVer = GameVersionFilter.Items.FirstOrDefault(i => i.Name.Equals(_gameVersion, StringComparison.OrdinalIgnoreCase));
                if (matchingVer != null)
                {
                    matchingVer.IsSelected = true;
                    GameVersionFilter.NotifySelectionState();
                }

                var loaderName = Models.ModrinthFullProject.FormatPlatformName(_loaderType);
                var matchingPlatform = actualLoaders.FirstOrDefault(p => p.Equals(loaderName, StringComparison.OrdinalIgnoreCase));
                SelectedFilterPlatform = matchingPlatform ?? "All platforms";

                PlatformFilterItems.Clear();
                foreach (var p in actualLoaders)
                {
                    var isSel = matchingPlatform != null && p.Equals(matchingPlatform, StringComparison.OrdinalIgnoreCase);
                    var item = new Models.FilterCheckItem { Name = p, IsSelected = isSel };
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

                // Default: select the instance MC version in Compatibility sidebar
                SelectedCompatibilityVersion = _gameVersion;
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
    private void ConfirmDelete()
    {
        var project = PendingDelete;
        if (project == null) return;
        PendingDelete = null;
        _ = DeleteAsync(project);
    }

    private async Task DeleteAsync(ModrinthProjectItemViewModel project)
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

