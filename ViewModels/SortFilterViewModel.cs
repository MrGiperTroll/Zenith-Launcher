using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Converters;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public sealed partial class SortFilterItem : ObservableObject
{
    public string Id { get; }
    public string DisplayName { get; }

    [ObservableProperty] private bool _isSelected;

    private readonly Action<SortFilterItem> _onSelect;

    public SortFilterItem(string id, string displayName, bool isSelected, Action<SortFilterItem> onSelect)
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

public sealed partial class SortFilterViewModel : ObservableObject
{
    public ObservableCollection<SortFilterItem> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    private string _selectedSort = "Relevance";

    public string ButtonText
    {
        get
        {
            var item = Items.FirstOrDefault(i => i.Id.Equals(SelectedSort, StringComparison.OrdinalIgnoreCase));
            var name = item?.DisplayName ?? (string?)SortDisplayConverter.Instance.Convert(SelectedSort, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture) ?? SelectedSort;
            var prefix = L10n.T("sort_by");
            return $"{prefix}{name}";
        }
    }

    public event Action<string>? SortChanged;

    public SortFilterViewModel()
    {
        SetOptions(new[] { "Relevance", "Downloads", "Newest", "Recently Updated" }, "Relevance");
        L10n.LanguageChanged += () =>
        {
            SetOptions(new[] { "Relevance", "Downloads", "Newest", "Recently Updated" }, SelectedSort);
            OnPropertyChanged(nameof(ButtonText));
        };
    }

    public void SetOptions(IEnumerable<string> options, string? selected = null)
    {
        var targetSelected = selected ?? SelectedSort;
        Items.Clear();

        foreach (var opt in options)
        {
            var disp = (string?)SortDisplayConverter.Instance.Convert(opt, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture) ?? opt;
            bool isSel = opt.Equals(targetSelected, StringComparison.OrdinalIgnoreCase);

            var item = new SortFilterItem(opt, disp, isSel, (clickedItem) =>
            {
                SetSelected(clickedItem.Id);
                SortChanged?.Invoke(clickedItem.Id);
            });
            Items.Add(item);
        }

        SetSelected(targetSelected, notifyEvent: false);
    }

    public void SetSelected(string sortId, bool notifyEvent = true)
    {
        SelectedSort = sortId;
        foreach (var item in Items)
        {
            item.IsSelected = item.Id.Equals(sortId, StringComparison.OrdinalIgnoreCase);
        }

        if (notifyEvent)
        {
            SortChanged?.Invoke(sortId);
        }
    }
}
