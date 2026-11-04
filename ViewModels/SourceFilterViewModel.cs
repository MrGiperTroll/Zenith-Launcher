using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public sealed partial class SourceFilterViewModel : ObservableObject
{
    private readonly Action<ContentSource> _onSourceChanged;

    [ObservableProperty]
    private ContentSource _selectedSource = ContentSource.Modrinth;

    public bool IsAllSource => SelectedSource == ContentSource.All;
    public bool IsModrinthSource => SelectedSource == ContentSource.Modrinth;
    public bool IsCurseForgeSource => SelectedSource == ContentSource.CurseForge;

    public string ButtonText => SelectedSource switch
    {
        ContentSource.All => L10n.T("source_all"),
        ContentSource.CurseForge => "CurseForge",
        _ => "Modrinth"
    };

    public SourceFilterViewModel(ContentSource initial, Action<ContentSource> onSourceChanged)
    {
        _selectedSource = initial;
        _onSourceChanged = onSourceChanged;
        L10n.LanguageChanged += NotifyStateChanged;
    }

    [RelayCommand]
    public void SelectSource(string source)
    {
        var newSource = source?.ToLowerInvariant() switch
        {
            "all" or "both" => ContentSource.All,
            "curseforge" => ContentSource.CurseForge,
            _ => ContentSource.Modrinth
        };

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
        OnPropertyChanged(nameof(IsAllSource));
        OnPropertyChanged(nameof(IsModrinthSource));
        OnPropertyChanged(nameof(IsCurseForgeSource));
        OnPropertyChanged(nameof(ButtonText));
    }
}
