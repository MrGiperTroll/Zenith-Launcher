using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CustomMcLauncher.ViewModels;

public partial class GameVersionFilterItem : ObservableObject
{
    public string Name { get; init; } = "";
    public bool IsSnapshot { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;
}

public partial class GameVersionFilterViewModel : ObservableObject
{
    public ObservableCollection<GameVersionFilterItem> Items { get; } = new();
    public ObservableCollection<GameVersionFilterItem> FilteredItems { get; } = new();

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _showAllVersions;

    public int SelectedCount => Items.Count(i => i.IsSelected);
    public string SelectedText => $"{SelectedCount} selected";
    public bool HasSelection => SelectedCount > 0;

    public string ButtonText
    {
        get
        {
            var selected = Items.Where(i => i.IsSelected).Select(i => i.Name).ToList();
            if (selected.Count == 0) return "Game version";
            if (selected.Count == 1) return selected[0];
            return $"Game version ({selected.Count})";
        }
    }

    public IReadOnlyList<string> SelectedVersions => Items.Where(i => i.IsSelected).Select(i => i.Name).ToList();

    public event Action? FilterChanged;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnShowAllVersionsChanged(bool value) => ApplyFilter();

    [RelayCommand]
    public void Clear()
    {
        foreach (var item in Items)
            item.IsSelected = false;
        NotifySelectionState();
        FilterChanged?.Invoke();
    }

    public void ApplyFilter()
    {
        var query = SearchText.Trim();
        var matching = Items.Where(item =>
        {
            bool matchesSnapshot = ShowAllVersions || !item.IsSnapshot || item.IsSelected;
            bool matchesSearch = string.IsNullOrWhiteSpace(query) ||
                                 item.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
            return matchesSnapshot && matchesSearch;
        }).ToList();

        FilteredItems.Clear();
        foreach (var m in matching)
        {
            m.IsVisible = true;
            FilteredItems.Add(m);
        }
    }

    public void SetVersions(IEnumerable<string>? rawVersions)
    {
        if (rawVersions == null) return;

        var previousSelections = new HashSet<string>(SelectedVersions, StringComparer.OrdinalIgnoreCase);

        var distinct = rawVersions
            .Where(v => !string.IsNullOrWhiteSpace(v) && !v.Equals("All versions", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sorted = ContentBrowserViewModel.SortGameVersionsDesc(distinct)
            .Where(v => !v.Equals("All versions", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var old in Items)
        {
            old.PropertyChanged -= OnItemPropertyChanged;
        }

        Items.Clear();

        foreach (var v in sorted)
        {
            var isSnap = !IsStableRelease(v);
            var item = new GameVersionFilterItem
            {
                Name = v,
                IsSnapshot = isSnap,
                IsSelected = previousSelections.Contains(v)
            };
            item.PropertyChanged += OnItemPropertyChanged;
            Items.Add(item);
        }

        ApplyFilter();
        NotifySelectionState();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameVersionFilterItem.IsSelected))
        {
            NotifySelectionState();
            FilterChanged?.Invoke();
        }
    }

    public void NotifySelectionState()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(SelectedVersions));
    }

    public static bool IsStableRelease(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        var trimmed = version.Trim();
        if (trimmed.Contains("-snapshot", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("-pre", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("-rc", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("alpha", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(trimmed, @"^\d{2}w\d{2}[a-z]$", RegexOptions.IgnoreCase))
        {
            return false;
        }
        return Regex.IsMatch(trimmed, @"^\d+\.\d+(\.\d+)?$");
    }
}
