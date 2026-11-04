using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.ViewModels;

public sealed partial class SourceFilterViewModel : ObservableObject
{
    private readonly Action<ContentSource> _onSourceChanged;

    [ObservableProperty]
    private ContentSource _selectedSource = ContentSource.Modrinth;

    public bool IsModrinthSource => SelectedSource == ContentSource.Modrinth;
    public bool IsCurseForgeSource => SelectedSource == ContentSource.CurseForge;
    public string ButtonText => IsCurseForgeSource ? "CurseForge" : "Modrinth";

    public SourceFilterViewModel(ContentSource initial, Action<ContentSource> onSourceChanged)
    {
        _selectedSource = initial;
        _onSourceChanged = onSourceChanged;
    }

    [RelayCommand]
    public void SelectSource(string source)
    {
        var newSource = string.Equals(source, "curseforge", StringComparison.OrdinalIgnoreCase)
            ? ContentSource.CurseForge
            : ContentSource.Modrinth;

        if (SelectedSource == newSource) return;

        SelectedSource = newSource;
        NotifyStateChanged();
        _onSourceChanged(newSource);
    }

    public void SetSource(ContentSource source)
    {
        if (SelectedSource == source) return;
        SelectedSource = source;
        NotifyStateChanged();
    }

    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsModrinthSource));
        OnPropertyChanged(nameof(IsCurseForgeSource));
        OnPropertyChanged(nameof(ButtonText));
    }
}
