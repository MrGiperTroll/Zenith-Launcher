using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Models;
using ZenithLauncher.Services;
using ZenithLauncher.ViewModels.Common;
using ZenithLauncher.ViewModels.Items;

namespace ZenithLauncher.ViewModels;

public partial class AuthorProfileViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private ModrinthUserProfile? _profile;

    public string Username => Profile?.Username ?? "";

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _selectedCategory = "all";

    public bool IsCategoryAll => SelectedCategory == "all";
    public bool IsCategoryMods => SelectedCategory == "mod";
    public bool IsCategoryPlugins => SelectedCategory == "plugin";

    public ObservableCollection<ModrinthProjectItemViewModel> AllProjects { get; } = new();
    public ObservableCollection<ModrinthProjectItemViewModel> FilteredProjects { get; } = new();

    public int ProjectsCount => AllProjects.Count;

    public string TotalDownloadsDisplay
    {
        get
        {
            long total = AllProjects.Sum(p => p.Downloads);
            if (total >= 1_000_000_000)
                return $"{total / 1e9:0.#}B downloads";
            if (total >= 1_000_000)
                return $"{total / 1e6:0.#}M downloads";
            if (total >= 1_000)
                return $"{total / 1e3:0.#}K downloads";
            return $"{total} downloads";
        }
    }

    public Action<ModrinthProjectItemViewModel>? OnOpenProjectRequested { get; set; }
    public Action? OnCloseRequested { get; set; }
    private System.Threading.CancellationTokenSource? _loadCts;
    private volatile bool _disposed;

    public async Task LoadAuthorAsync(string usernameOrId)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new System.Threading.CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        foreach (var old in AllProjects) old.Dispose();
        AllProjects.Clear();
        FilteredProjects.Clear();

        try
        {
            var user = await ModrinthApiService.GetUserProfileAsync(usernameOrId, token);
            if (_disposed || token.IsCancellationRequested)
            {
                user?.Dispose();
                return;
            }
            Profile?.Dispose();
            Profile = user ?? new ModrinthUserProfile { Username = usernameOrId };

            var projects = await ModrinthApiService.GetUserProjectsAsync(usernameOrId, token);
            if (_disposed || token.IsCancellationRequested)
            {
                foreach (var p in projects) p.Dispose();
                return;
            }
            foreach (var p in projects.OrderByDescending(x => x.Downloads))
            {
                AllProjects.Add(p);
            }
            ApplyFilter();
            OnPropertyChanged(nameof(ProjectsCount));
            OnPropertyChanged(nameof(TotalDownloadsDisplay));
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            if (!_disposed)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectCategory(string category)
    {
        SelectedCategory = category;
        OnPropertyChanged(nameof(IsCategoryAll));
        OnPropertyChanged(nameof(IsCategoryMods));
        OnPropertyChanged(nameof(IsCategoryPlugins));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredProjects.Clear();
        var cat = (SelectedCategory ?? "all").ToLowerInvariant();
        foreach (var p in AllProjects)
        {
            if (cat == "all")
            {
                FilteredProjects.Add(p);
            }
            else if (cat == "mod" && (p.ProjectType.Equals("mod", StringComparison.OrdinalIgnoreCase) || p.ProjectType.Equals("modpack", StringComparison.OrdinalIgnoreCase)))
            {
                FilteredProjects.Add(p);
            }
            else if (cat == "plugin" && (p.ProjectType.Equals("plugin", StringComparison.OrdinalIgnoreCase) || p.ProjectType.Equals("datapack", StringComparison.OrdinalIgnoreCase)))
            {
                FilteredProjects.Add(p);
            }
        }
    }

    [RelayCommand]
    private void OpenProject(ModrinthProjectItemViewModel? project)
    {
        if (project != null)
        {
            OnOpenProjectRequested?.Invoke(project);
        }
    }

    [RelayCommand]
    private void CloseProfile()
    {
        OnCloseRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        if (Profile != null && !string.IsNullOrWhiteSpace(Profile.WebsiteUrl))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Profile.WebsiteUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _loadCts?.Cancel(); _loadCts?.Dispose(); } catch { }
        _loadCts = null;
        Profile?.Dispose();
        Profile = null;
        foreach (var p in AllProjects)
        {
            p.Dispose();
        }
        AllProjects.Clear();
        FilteredProjects.Clear();
        OnOpenProjectRequested = null;
        OnCloseRequested = null;
    }
}
