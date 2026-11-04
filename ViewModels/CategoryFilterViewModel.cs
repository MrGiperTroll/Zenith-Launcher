using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public sealed partial class CategoryFilterItem : ObservableObject
{
    public ContentType Type { get; }

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private bool _isSelected;

    private readonly Action? _onChanged;

    public CategoryFilterItem(ContentType type, string displayName, bool isSelected, Action? onChanged = null)
    {
        Type = type;
        DisplayName = displayName;
        _isSelected = isSelected;
        _onChanged = onChanged;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _onChanged?.Invoke();
    }
}

public sealed partial class CategoryFilterViewModel : ObservableObject
{
    public ObservableCollection<CategoryFilterItem> Items { get; } = new();

    public int SelectedCount => Items.Count(i => i.IsSelected);
    public string SelectedText => string.Format(L10n.T("filter_selected"), SelectedCount);
    public bool HasSelection => SelectedCount > 0;

    public string ButtonText
    {
        get
        {
            var selected = Items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return L10n.T("filter_categories");
            if (selected.Count == 1) return selected[0].DisplayName;
            return string.Format(L10n.T("filter_categories_count"), selected.Count);
        }
    }

    public IReadOnlyList<ContentType> SelectedTypes =>
        Items.Where(i => i.IsSelected).Select(i => i.Type).ToList();

    public event Action? FilterChanged;

    public CategoryFilterViewModel()
    {
        PopulateItems();
        L10n.LanguageChanged += () =>
        {
            RefreshDisplayNames();
            NotifySelectionState();
        };
    }

    public void PopulateItems()
    {
        Items.Clear();
        Items.Add(new CategoryFilterItem(ContentType.Mod, L10n.T("tab_mods"), true, OnItemChanged));
        Items.Add(new CategoryFilterItem(ContentType.ResourcePack, L10n.T("tab_resourcepacks"), false, OnItemChanged));
        Items.Add(new CategoryFilterItem(ContentType.DataPack, L10n.T("tab_datapacks"), false, OnItemChanged));
        Items.Add(new CategoryFilterItem(ContentType.Shader, L10n.T("tab_shaders"), false, OnItemChanged));
        Items.Add(new CategoryFilterItem(ContentType.World, L10n.T("tab_worlds"), false, OnItemChanged));
        NotifySelectionState();
    }

    public void RefreshDisplayNames()
    {
        foreach (var item in Items)
        {
            item.DisplayName = item.Type switch
            {
                ContentType.Mod => L10n.T("tab_mods"),
                ContentType.ResourcePack => L10n.T("tab_resourcepacks"),
                ContentType.DataPack => L10n.T("tab_datapacks"),
                ContentType.Shader => L10n.T("tab_shaders"),
                ContentType.World => L10n.T("tab_worlds"),
                _ => item.Type.ToString()
            };
        }
    }

    private void OnItemChanged()
    {
        NotifySelectionState();
        FilterChanged?.Invoke();
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
        OnPropertyChanged(nameof(SelectedTypes));
    }
}
