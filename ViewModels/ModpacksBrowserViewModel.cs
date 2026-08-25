using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
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

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task InstallAsync(ModrinthProject project)
    {
        if (project == null || project.IsInstalling || project.IsInstalled) return;
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
