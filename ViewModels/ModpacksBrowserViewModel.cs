using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CmlLib.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;

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

    public ObservableCollection<ModrinthProject> Results { get; } = new();
    public ObservableCollection<string> AvailableVersions { get; } = new();
    [ObservableProperty] private ObservableCollection<string> _availableCategories = new();
    [ObservableProperty] private ObservableCollection<string> _availableLoaders = new();
    [ObservableProperty] private ObservableCollection<string> _availableSortOptions = new() { "Relevance", "Downloads", "Newest", "Recently Updated" };

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _selectedVersion = "";
    [ObservableProperty] private string _selectedCategory = "";
    [ObservableProperty] private string _selectedLoader = "";
    [ObservableProperty] private string _selectedSort = "Relevance";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isLoadingMore;

    [ObservableProperty] private ModrinthProject? _pendingDelete;

    [ObservableProperty] private ModrinthProject? _selectedProject;

    public bool IsDetailsOpen => SelectedProject != null;

    public ModrinthProject? DetailsProject => SelectedProject;

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

    public Models.ModrinthFullProject? VersionSelectorProject => FullProject;

    /// <summary>Button text: "Install" for new content, "Reinstall" for already-installed.</summary>
    public string ReinstallText => _showInstallFeedback
        ? "Installed"
        : SelectedProject is { IsInstalled: true }
            ? L10n.T("mi_reinstall")
            : L10n.T("mi_install");

    partial void OnSelectedProjectChanged(ModrinthProject? value)
    {
        OnPropertyChanged(nameof(IsDetailsOpen));
        OnPropertyChanged(nameof(DetailsProject));
        OnPropertyChanged(nameof(InstallButtonVisible));
        OnPropertyChanged(nameof(ReinstallText));
        SelectedVersionForDetails = null;
        SelectedCompatibilityVersion = "";
        if (value != null) _ = LoadFullProjectAsync(value);
        else FullProject = null;
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

    partial void OnPendingDeleteChanged(ModrinthProject? value)
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
            var categoryFilter = string.IsNullOrWhiteSpace(SelectedCategory) ? Array.Empty<string>() : new[] { SelectedCategory };
            var sort = SelectedSort switch
            {
                "Downloads" => "downloads",
                "Newest" => "newest",
                "Recently Updated" => "updated",
                _ => "relevance"
            };
            // For category, pass as categories filter
            var page = await ModrinthApiService.SearchAsync(SearchQuery, SelectedVersion, LoaderFacets, "modpack", sort, categoryFilter, 0, PageSize);
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
            StatusText = page.TotalHits == 0 ? L10n.T("mp_status_none") : string.Format(L10n.T("mp_status_count"), page.TotalHits);
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Modpack search failed", ex);
            StatusText = string.Format(L10n.T("mp_status_failed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasMore));
        }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (IsBusy || IsLoadingMore || !HasMore) return;
        IsLoadingMore = true;
        try
        {
            var categoryFilter = string.IsNullOrWhiteSpace(SelectedCategory) ? Array.Empty<string>() : new[] { SelectedCategory };
            var sort = SelectedSort switch
            {
                "Downloads" => "downloads",
                "Newest" => "newest",
                "Recently Updated" => "updated",
                _ => "relevance"
            };
            var page = await ModrinthApiService.SearchAsync(SearchQuery, SelectedVersion, LoaderFacets, "modpack", sort, categoryFilter, _offset, PageSize);
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
            LauncherLog.Error("Modpack load-more failed", ex);
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
            p.IsInstalled = isInstalled;
        }
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
        OpenVersionSelector(project);
    }

    [RelayCommand]
    private void CancelDelete() => PendingDelete = null;

    [RelayCommand]
    private void OpenProjectPage(ModrinthProject project)
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

    private async Task InstallSpecificVersionAsync(ModrinthProject project, Models.ModrinthProjectVersion version)
    {
        if (project == null || project.IsInstalling) return;
        var file = version.Files.FirstOrDefault(f => f.IsPrimary) ?? version.Files.FirstOrDefault();
        if (file == null || string.IsNullOrWhiteSpace(file.Url)) return;
        try
        {
            project.IsInstalling = true;
            StatusText = "Installing...";
            var tempDir = Path.Combine(Path.GetTempPath(), $"zenith_modpack_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, file.Filename);
            var data = await ModrinthApiService.DownloadAsync(file.Url);
            if (data is not { Length: > 0 })
            {
                StatusText = "Download failed.";
                return;
            }
            await File.WriteAllBytesAsync(zipPath, data);
            var instance = await InstallModpackFromZipAsync(project, zipPath, tempDir);
            if (instance != null)
            {
                project.IsInstalled = true;
                StatusText = $"Installed {project.Title}";
                UpdateInstalledFlags();
                _onInstalled?.Invoke();
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to install modpack {project.Title}", ex);
            StatusText = $"Failed to install: {ex.Message}";
        }
        finally
        {
            project.IsInstalling = false;
        }
    }

    private async Task<Models.InstanceModel?> InstallModpackFromZipAsync(ModrinthProject project, string zipPath, string tempDir)
    {
        try
        {
            var extractDir = Path.Combine(tempDir, "extracted");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);

            // Find manifest.json for curseforge/modrinth modpacks
            var manifestPath = Path.Combine(extractDir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                // Try finding it in subdirectories
                var manifestFiles = Directory.GetFiles(extractDir, "manifest.json", SearchOption.AllDirectories);
                if (manifestFiles.Length > 0) manifestPath = manifestFiles[0];
            }

            if (!File.Exists(manifestPath))
            {
                StatusText = "Invalid modpack: manifest.json not found.";
                return null;
            }

            var manifestJson = await File.ReadAllTextAsync(manifestPath);
            using var manifestDoc = System.Text.Json.JsonDocument.Parse(manifestJson);
            var manifest = manifestDoc.RootElement;

            // Get game version
            var mcVersion = "";
            if (manifest.TryGetProperty("minecraft", out var mc) && mc.TryGetProperty("version", out var mv))
                mcVersion = mv.GetString() ?? "";

            if (string.IsNullOrWhiteSpace(mcVersion))
            {
                StatusText = "Invalid modpack: no Minecraft version found.";
                return null;
            }

            // Create instance
            var instanceName = string.IsNullOrWhiteSpace(project.Title) ? project.Slug : project.Title;
            var instService = new InstanceService();
            var inst = new Models.InstanceModel
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Name = instanceName,
                Version = mcVersion,
                LoaderType = "Fabric",
                LoaderVersion = "",
                Path = Path.Combine(ZenithPaths.AppDataDir, "instances", instanceName),
            };
            await _instanceService.SaveInstanceAsync(inst);

            // Copy modpack contents into instance
            var instancePath = inst.Path;
            Directory.CreateDirectory(instancePath);

            // Copy overrides if present
            var overridesDir = Path.Combine(extractDir, "overrides");
            if (Directory.Exists(overridesDir))
            {
                CopyDirectoryRecursive(overridesDir, instancePath);
            }

            // Install mods from manifest
            if (manifest.TryGetProperty("modLoader", out var modLoader))
            {
                var loaderName = modLoader.TryGetProperty("primaryModLoader", out var pn) ? pn.GetString() ?? "" : "";
                if (loaderName.Contains("forge", StringComparison.OrdinalIgnoreCase))
                    inst.LoaderType = "Forge";
                else if (loaderName.Contains("fabric", StringComparison.OrdinalIgnoreCase))
                    inst.LoaderType = "Fabric";
                else if (loaderName.Contains("quilt", StringComparison.OrdinalIgnoreCase))
                    inst.LoaderType = "Quilt";
                else if (loaderName.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
                    inst.LoaderType = "NeoForge";
            }

            if (manifest.TryGetProperty("modLoader", out var ml2) && ml2.TryGetProperty("version", out var mlv))
                inst.LoaderVersion = mlv.GetString() ?? "";

            await _instanceService.SaveInstanceAsync(inst);
            return inst;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to install modpack from zip", ex);
            return null;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
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

    private void DeleteModpack(ModrinthProject project)
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
    private async Task InstallAsync(ModrinthProject project)
    {
        if (project == null || project.IsInstalling) return;
        project.IsInstalling = true;
        Services.DiscordPresenceService.SetInstallingContent(project.Title);
        StatusText = string.Format(L10n.T("mp_resolving"), project.Title);
        try
        {
            var version = await ModrinthApiService.GetLatestVersionAsync(project.ProjectId, SelectedVersion, LoaderFacets);
            if (version == null && !string.IsNullOrEmpty(SelectedVersion))
                version = await ModrinthApiService.GetLatestVersionAsync(project.ProjectId, "", LoaderFacets);
            if (version == null)
            {
                StatusText = string.Format(L10n.T("mp_noversion"), project.Title);
                return;
            }
            var targetName = project.Title.Trim();
            if (string.IsNullOrWhiteSpace(targetName)) targetName = project.Slug;
            var allNames = _instanceService.GetInstances().Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var finalName = targetName;
            int idx = 1;
            while (allNames.Contains(finalName))
            {
                finalName = $"{targetName}_{idx}";
                idx++;
            }
            var gameVersion = SelectedVersion;
            if (string.IsNullOrWhiteSpace(gameVersion) && version.GameVersions.Length > 0)
                gameVersion = version.GameVersions[0];
            if (string.IsNullOrWhiteSpace(gameVersion))
                gameVersion = "1.20.1";
            var loaderType = "Vanilla";
            if (version.Loaders.Length > 0)
            {
                var l = version.Loaders[0].ToLowerInvariant();
                loaderType = l switch
                {
                    "forge" => "Forge",
                    "fabric" => "Fabric",
                    "quilt" => "Quilt",
                    "neoforge" => "NeoForge",
                    _ => "Vanilla"
                };
            }
            else
            {
                var catLoader = project.Categories.FirstOrDefault(c => c is "forge" or "fabric" or "quilt" or "neoforge");
                if (!string.IsNullOrWhiteSpace(catLoader))
                    loaderType = char.ToUpper(catLoader[0]) + catLoader[1..].ToLowerInvariant();
            }
            string loaderBuild = "";
            if (loaderType != "Vanilla")
            {
                try
                {
                    var builds = await _modLoaderService.GetLoaderBuildsAsync(gameVersion, loaderType);
                    loaderBuild = builds.FirstOrDefault() ?? "";
                }
                catch { }
            }
            var inst = await _instanceService.CreateInstanceAsync(finalName, gameVersion, loaderType, loaderBuild);
            if (inst != null && !string.IsNullOrWhiteSpace(project.IconUrl))
            {
                try
                {
                    var iconBytes = await ModrinthApiService.GetIconAsync(project.IconUrl);
                    if (iconBytes != null)
                    {
                        var ext = ".png";
                        try { var uri = new Uri(project.IconUrl); var e = Path.GetExtension(uri.AbsolutePath); if (!string.IsNullOrWhiteSpace(e)) ext = e; } catch { }
                        var iconPath = Path.Combine(inst.Path, $"icon{ext}");
                        await File.WriteAllBytesAsync(iconPath, iconBytes);
                        inst.IconPath = iconPath;
                        await _instanceService.SaveInstanceAsync(inst);
                    }
                }
                catch { }
            }
            if (inst == null)
            {
                StatusText = string.Format(L10n.T("mp_create_failed"), project.Title);
                return;
            }
            StatusText = string.Format(L10n.T("mp_downloading"), project.Title);
            var data = await ModrinthApiService.DownloadAsync(version.FileUrl);
            if (data == null || data.Length == 0)
            {
                StatusText = string.Format(L10n.T("mp_download_failed"), project.Title);
                return;
            }
            var mrpackPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mrpack");
            await File.WriteAllBytesAsync(mrpackPath, data);
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(mrpackPath);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
                    {
                        var relative = entry.FullName["overrides/".Length..];
                        if (string.IsNullOrEmpty(relative)) continue;
                        var destPath = Path.Combine(inst.Path, relative);
                        var dir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        if (!entry.FullName.EndsWith("/"))
                            entry.ExtractToFile(destPath, true);
                    }
                }
                var hasOverrides = zip.Entries.Any(e => e.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase));
                if (!hasOverrides)
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.FullName == "modrinth.index.json") continue;
                        if (entry.FullName.EndsWith("/")) continue;
                        var destPath = Path.Combine(inst.Path, entry.FullName);
                        var dir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        entry.ExtractToFile(destPath, true);
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"Failed to extract modpack {project.Title}", ex);
            }
            finally
            {
                try { File.Delete(mrpackPath); } catch { }
            }
            project.IsInstalled = true;
            StatusText = string.Format(L10n.T("mp_installed_as"), project.Title, finalName);
            _onInstalled?.Invoke();
            UpdateInstalledFlags();
            FlashInstalledFeedback();
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Modpack install failed for {project.Title}", ex);
            StatusText = string.Format(L10n.T("mp_install_failed"), ex.Message);
        }
        finally
        {
            project.IsInstalling = false;
        }
    }
}
