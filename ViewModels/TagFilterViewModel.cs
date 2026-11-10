using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public sealed partial class TagFilterItem : ObservableObject
{
    public string Id { get; }
    public string DisplayName { get; }

    [ObservableProperty] private bool _isSelected;

    private readonly Action<TagFilterItem> _onSelect;

    public TagFilterItem(string id, string displayName, bool isSelected, Action<TagFilterItem> onSelect)
    {
        Id = id;
        DisplayName = displayName;
        _isSelected = isSelected;
        _onSelect = onSelect;
    }

    [RelayCommand]
    public void Select()
    {
        _onSelect(this);
    }
}

public sealed partial class TagFilterViewModel : ObservableObject
{
    public ObservableCollection<TagFilterItem> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    private string _selectedTag = "All";

    public string ButtonText
    {
        get
        {
            var item = Items.FirstOrDefault(i => i.Id.Equals(SelectedTag, StringComparison.OrdinalIgnoreCase));
            var name = item?.DisplayName ?? SelectedTag;
            if (string.Equals(SelectedTag, "All", StringComparison.OrdinalIgnoreCase))
                return $"{L10n.T("cb_categories")}: {L10n.T("cb_tag_all")}";
            return $"{L10n.T("cb_categories")}: {name}";
        }
    }

    public event Action<string>? TagChanged;

    public TagFilterViewModel()
    {
        L10n.LanguageChanged += () =>
        {
            RefreshDisplayNames();
            OnPropertyChanged(nameof(ButtonText));
        };
    }

    public void SetOptions(IEnumerable<string> options, string? selected = null)
    {
        var targetSelected = selected ?? SelectedTag;
        Items.Clear();

        foreach (var opt in options)
        {
            var disp = FormatTagDisplay(opt);
            bool isSel = opt.Equals(targetSelected, StringComparison.OrdinalIgnoreCase);

            var item = new TagFilterItem(opt, disp, isSel, (clickedItem) =>
            {
                SetSelected(clickedItem.Id);
            });
            Items.Add(item);
        }

        SetSelected(targetSelected, notifyEvent: false);
    }

    public void SetSelected(string tagId, bool notifyEvent = true)
    {
        SelectedTag = tagId;
        foreach (var item in Items)
        {
            item.IsSelected = item.Id.Equals(tagId, StringComparison.OrdinalIgnoreCase);
        }

        OnPropertyChanged(nameof(ButtonText));

        if (notifyEvent)
        {
            TagChanged?.Invoke(tagId);
        }
    }

    public void RefreshDisplayNames()
    {
        var current = SelectedTag;
        var tagIds = Items.Select(i => i.Id).ToList();
        SetOptions(tagIds, current);
    }

    private static string FormatTagDisplay(string tag)
    {
        if (string.Equals(tag, "All", StringComparison.OrdinalIgnoreCase))
            return L10n.T("cb_tag_all");

        return tag.ToLowerInvariant() switch
        {
            "adventure" => L10n.T("cat_adventure"),
            "magic" => L10n.T("cat_magic"),
            "technology" => L10n.T("cat_technology"),
            "utility" => L10n.T("cat_utility"),
            "library" => L10n.T("cat_library"),
            "optimization" => L10n.T("cat_optimization"),
            "worldgen" => L10n.T("cat_worldgen"),
            "equipment" => L10n.T("cat_equipment"),
            "game-mechanics" => L10n.T("cat_game_mechanics"),
            "mobs" => L10n.T("cat_mobs"),
            "realistic" => L10n.T("cat_realistic"),
            "cartoon" => L10n.T("cat_cartoon"),
            "themed" => L10n.T("cat_themed"),
            "vanilla-like" or "vanilla" => L10n.T("cat_vanilla_like"),
            "fantasy" => L10n.T("cat_fantasy"),
            "vibrant" => L10n.T("cat_vibrant"),
            "soft" => L10n.T("cat_soft"),
            "performance" => L10n.T("cat_performance"),
            "simplistic" => L10n.T("cat_simplistic"),
            "tweaks" => L10n.T("cat_tweaks"),
            "decoration" => "Decoration",
            _ => tag.Length > 0 ? char.ToUpperInvariant(tag[0]) + tag[1..].Replace('-', ' ') : tag
        };
    }
}
