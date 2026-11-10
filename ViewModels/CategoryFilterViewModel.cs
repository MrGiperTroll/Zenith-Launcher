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

    public CategoryFilterItem(ContentType type, string displayName, bool isSelected)
    {
        Type = type;
        DisplayName = displayName;
        _isSelected = isSelected;
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
            var selected = Items.FirstOrDefault(i => i.IsSelected);
            return selected?.DisplayName ?? L10n.T("filter_categories");
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

    public void PopulateItems(ContentType selectedType = ContentType.Mod, bool hasModsSupport = true, bool hasShaderSupport = true)
    {
        Items.Clear();
        if (hasModsSupport)
            Items.Add(new CategoryFilterItem(ContentType.Mod, L10n.T("tab_mods"), selectedType == ContentType.Mod));
        Items.Add(new CategoryFilterItem(ContentType.ResourcePack, L10n.T("tab_resourcepacks"), selectedType == ContentType.ResourcePack));
        Items.Add(new CategoryFilterItem(ContentType.DataPack, L10n.T("tab_datapacks"), selectedType == ContentType.DataPack));
        if (hasShaderSupport)
            Items.Add(new CategoryFilterItem(ContentType.Shader, L10n.T("tab_shaders"), selectedType == ContentType.Shader));
        Items.Add(new CategoryFilterItem(ContentType.World, L10n.T("tab_worlds"), selectedType == ContentType.World));
        NotifySelectionState();
    }

    public void SelectItem(CategoryFilterItem item)
    {
        SetSingleType(item.Type);
        FilterChanged?.Invoke();
    }

    public void SetSingleType(ContentType type)
    {
        foreach (var item in Items)
            item.IsSelected = (item.Type == type);
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
