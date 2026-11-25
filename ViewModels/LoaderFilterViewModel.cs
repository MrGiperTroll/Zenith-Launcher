using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Services;

namespace ZenithLauncher.ViewModels;

public partial class LoaderFilterItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";

    [ObservableProperty]
    private bool _isSelected;
}

public partial class LoaderFilterViewModel : ObservableObject, IDisposable
{
    public ObservableCollection<LoaderFilterItem> Items { get; } = new();
    private readonly Action _languageChangedHandler;
    private bool _disposed;

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
        _languageChangedHandler = () =>
        {
            OnPropertyChanged(nameof(ButtonText));
            OnPropertyChanged(nameof(SelectedText));
        };
        L10n.LanguageChanged += _languageChangedHandler;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        L10n.LanguageChanged -= _languageChangedHandler;
        FilterChanged = null;
        foreach (var item in Items)
            item.PropertyChanged -= OnItemPropertyChanged;
        Items.Clear();
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
