using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.ViewModels;

public sealed partial class SourceFilterViewModel : ObservableObject
{
    private readonly Action<ContentSource> _onSourceChanged;

    [ObservableProperty]
    private ContentSource _selectedSource = ContentSource.Modrinth;

    [ObservableProperty]
    private bool _isModrinthSelected = true;

    [ObservableProperty]
    private bool _isCurseForgeSelected = false;

    public bool IsAllSource => IsModrinthSelected && IsCurseForgeSelected;
    public bool IsModrinthOnly => IsModrinthSelected && !IsCurseForgeSelected;
    public bool IsCurseForgeOnly => !IsModrinthSelected && IsCurseForgeSelected;

    public string ButtonText => (IsModrinthSelected, IsCurseForgeSelected) switch
    {
        (true, true) => "Modrinth, CurseForge",
        (false, true) => "CurseForge",
        _ => "Modrinth"
    };

    public SourceFilterViewModel(ContentSource initial, Action<ContentSource> onSourceChanged)
    {
        _onSourceChanged = onSourceChanged;
        _selectedSource = initial;
        _isModrinthSelected = initial is ContentSource.Modrinth or ContentSource.All;
        _isCurseForgeSelected = initial is ContentSource.CurseForge or ContentSource.All;
    }

    partial void OnIsModrinthSelectedChanged(bool value)
    {
        if (!value && !IsCurseForgeSelected)
        {
            // Do not allow deselecting both: at least one source must remain active
            IsModrinthSelected = true;
            return;
        }

        SyncSource();
    }

    partial void OnIsCurseForgeSelectedChanged(bool value)
    {
        if (!value && !IsModrinthSelected)
        {
            // Do not allow deselecting both: at least one source must remain active
            IsCurseForgeSelected = true;
            return;
        }

        SyncSource();
    }

    private void SyncSource()
    {
        var newSource = (IsModrinthSelected, IsCurseForgeSelected) switch
        {
            (true, true) => ContentSource.All,
            (false, true) => ContentSource.CurseForge,
            _ => ContentSource.Modrinth
        };

        if (SelectedSource != newSource)
        {
            SelectedSource = newSource;
            NotifyStateChanged();
            _onSourceChanged(newSource);
        }
        else
        {
            NotifyStateChanged();
        }
    }

    public void SetSource(ContentSource source)
    {
        bool modrinth = source is ContentSource.Modrinth or ContentSource.All;
        bool curseforge = source is ContentSource.CurseForge or ContentSource.All;

        if (IsModrinthSelected == modrinth && IsCurseForgeSelected == curseforge && SelectedSource == source)
            return;

        IsModrinthSelected = modrinth;
        IsCurseForgeSelected = curseforge;
        SelectedSource = source;

        NotifyStateChanged();
    }

    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(IsAllSource));
        OnPropertyChanged(nameof(IsModrinthOnly));
        OnPropertyChanged(nameof(IsCurseForgeOnly));
        OnPropertyChanged(nameof(ButtonText));
    }
}
