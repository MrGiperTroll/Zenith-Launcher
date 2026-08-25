using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class IconPickerViewModel : ObservableObject
{
    public ObservableCollection<IconOption> AllIcons { get; } = new();
    public ObservableCollection<IconOption> FilteredIcons { get; } = new();

    [ObservableProperty] private IconOption? _selectedIcon;
    [ObservableProperty] private string _searchQuery = "";

    public event Action<IconOption>? IconSelected;
    public event Action<string>? CustomIconPicked; // file path

    public IconPickerViewModel()
    {
        LoadIcons();
    }

    private void LoadIcons()
    {
        AllIcons.Clear();
        foreach (var icon in IconGalleryService.GetAllIcons())
            AllIcons.Add(icon);
        FilterIcons();
    }

    partial void OnSearchQueryChanged(string value) => FilterIcons();

    private void FilterIcons()
    {
        FilteredIcons.Clear();
        var q = SearchQuery?.Trim().ToLowerInvariant() ?? "";
        var filtered = string.IsNullOrWhiteSpace(q)
            ? AllIcons
            : AllIcons.Where(i => i.Name.ToLowerInvariant().Contains(q) || i.Category.ToLowerInvariant().Contains(q));
        foreach (var icon in filtered)
            FilteredIcons.Add(icon);
    }

    [RelayCommand]
    private void SelectIcon(IconOption icon)
    {
        SelectedIcon = icon;
        IconSelected?.Invoke(icon);
    }

    [RelayCommand]
    private async Task PickCustomIconAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;
        if (topLevel?.StorageProvider == null) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Custom Icon",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });
        if (files.Count > 0)
        {
            var src = files[0].Path.LocalPath;
            var dest = IconGalleryService.SaveCustomIcon(src);
            var newIcon = new IconOption { Name = Path.GetFileNameWithoutExtension(dest), Category = "Custom", FilePath = dest, IsCustom = true };
            AllIcons.Add(newIcon);
            FilteredIcons.Add(newIcon);
            CustomIconPicked?.Invoke(dest);
            IconSelected?.Invoke(newIcon);
        }
    }

    public void Refresh() => LoadIcons();
}
