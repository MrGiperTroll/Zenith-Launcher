using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class LoaderFilterItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";

    [ObservableProperty]
    private bool _isSelected;
}

public partial class LoaderFilterViewModel : ObservableObject
{
    public ObservableCollection<LoaderFilterItem> Items { get; } = new();

    public int SelectedCount => Items.Count(i => i.IsSelected);
    public string SelectedText => string.Format(L10n.T("filter_selected"), SelectedCount);
    public bool HasSelection => SelectedCount > 0;

    public string ButtonText
    {
        get
        {
            var selected = Items.Where(i => i.IsSelected).Select(i => i.DisplayName).ToList();
            if (selected.Count == 0) return L10n.T("filter_any_loader");
            if (selected.Count == 1) return selected[0];
            return $"{L10n.T("filter_loaders")} ({selected.Count})";
        }
    }

    public IReadOnlyList<string> SelectedLoaders => Items.Where(i => i.IsSelected).Select(i => i.Id).ToList();

    public event Action? FilterChanged;

    public LoaderFilterViewModel()
    {
        L10n.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(ButtonText));
            OnPropertyChanged(nameof(SelectedText));
        };
    }

    [RelayCommand]
    public void Clear()
    {
        foreach (var item in Items)
            item.IsSelected = false;
        NotifySelectionState();
        FilterChanged?.Invoke();
    }

    public void NotifySelectionState()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(SelectedLoaders));
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoaderFilterItem.IsSelected))
        {
            NotifySelectionState();
            FilterChanged?.Invoke();
        }
    }

    public void SetAvailableLoaders(IEnumerable<(string Id, string DisplayName)> loaders, IEnumerable<string>? initialSelection = null)
    {
        var prevSelected = initialSelection != null
            ? new HashSet<string>(initialSelection, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(SelectedLoaders, StringComparer.OrdinalIgnoreCase);

        foreach (var old in Items)
            old.PropertyChanged -= OnItemPropertyChanged;

        Items.Clear();

        foreach (var (id, name) in loaders)
        {
            var item = new LoaderFilterItem
            {
                Id = id,
                DisplayName = name,
                IsSelected = prevSelected.Contains(id)
            };
            item.PropertyChanged += OnItemPropertyChanged;
            Items.Add(item);
        }

        NotifySelectionState();
    }
}
