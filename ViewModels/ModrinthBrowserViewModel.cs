using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class ModrinthBrowserViewModel : ObservableObject
{
    private const int PageSize = 20;

    private readonly string _gameVersion;
    private readonly string[] _loaders;
    private readonly string _modsFolder;
    private readonly Action _refreshMods;
    private int _offset;
    private int _totalHits;

    public ObservableCollection<ModrinthProject> Results { get; } = new();

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoadingMore;

    public string FilterLabel
    {
        get
        {
            var loaders = _loaders.Length == 0 ? "any loader" : string.Join(" / ", _loaders);
            return $"Minecraft {_gameVersion} • {loaders}";
        }
    }

    public bool IsEmpty => Results.Count == 0 && !IsBusy && !IsLoadingMore;

    public bool HasMore => _offset < _totalHits;

    public ModrinthBrowserViewModel(string gameVersion, string loaderType, string modsFolder, Action refreshMods)
    {
        _gameVersion = gameVersion;
        _loaders = MapLoaders(loaderType);
        _modsFolder = modsFolder;
        _refreshMods = refreshMods;
        _ = ReloadAsync();
    }

    private static string[] MapLoaders(string loader)
    {
        return loader.Trim().ToLowerInvariant() switch
        {
            "fabric" => new[] { "fabric" },
            "forge" => new[] { "forge" },
            "neoforge" => new[] { "neoforged" },
            "quilt" => new[] { "quilt" },
            _ => Array.Empty<string>()
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
            var page = await ModrinthApiService.SearchAsync(SearchQuery, _gameVersion, _loaders, 0, PageSize);
            if (page == null)
            {
                StatusText = "Search failed — Modrinth unreachable.";
                return;
            }
            _totalHits = page.TotalHits;
            _offset = page.Hits.Count;
            foreach (var p in page.Hits)
            {
                Results.Add(p);
                LoadIcon(p);
            }
            StatusText = page.TotalHits == 0
                ? "No mods match this version and loader."
                : $"{page.TotalHits} mod(s) • limited to Minecraft {_gameVersion}";
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Modrinth search failed", ex);
            StatusText = $"Search failed: {ex.Message}";
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
            var page = await ModrinthApiService.SearchAsync(SearchQuery, _gameVersion, _loaders, _offset, PageSize);
            if (page != null)
            {
                _offset = page.Offset + page.Hits.Count;
                _totalHits = page.TotalHits;
                foreach (var p in page.Hits)
                {
                    Results.Add(p);
                    LoadIcon(p);
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Modrinth load-more failed", ex);
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
    private async Task InstallAsync(ModrinthProject project)
    {
        if (project == null || project.IsInstalling || project.IsInstalled) return;

        project.IsInstalling = true;
        StatusText = $"Resolving version for {project.Title}…";
        try
        {
            var version = await ModrinthApiService.GetLatestVersionAsync(project.ProjectId, _gameVersion, _loaders);
            if (version == null)
            {
                StatusText = $"No version of {project.Title} matches {FilterLabel}.";
                return;
            }

            StatusText = $"Downloading {version.FileName}…";
            var data = await ModrinthApiService.DownloadAsync(version.FileUrl);
            if (data is not { Length: > 0 })
            {
                StatusText = $"Download of {version.FileName} failed.";
                return;
            }

            Directory.CreateDirectory(_modsFolder);
            var dest = Path.Combine(_modsFolder, SanitizeFileName(version.FileName));
            await File.WriteAllBytesAsync(dest, data);

            project.IsInstalled = true;
            project.InstalledVersion = version.VersionNumber;
            StatusText = $"Installed {project.Title} — {version.FileName}";
            _refreshMods?.Invoke();
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Modrinth install failed for {project.Title}", ex);
            StatusText = $"Install failed: {ex.Message}";
        }
        finally
        {
            project.IsInstalling = false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}