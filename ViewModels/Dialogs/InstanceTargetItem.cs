using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.ViewModels.Dialogs;

public sealed partial class InstanceTargetItem : ObservableObject
{
    public InstanceModel Instance { get; }
    public string Name => Instance.Name;
    public string VersionDisplay => $"{Instance.Version} {Instance.LoaderType}";
    public string? IconPath => Instance.IconPath;
    public bool HasIcon => Instance.HasCustomIcon;
    public bool IsDatapackSelection { get; }

    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private string _buttonText = "Install";

    public bool CanClickButton => IsDatapackSelection || !IsInstalled;

    private readonly Func<InstanceTargetItem, Task> _onAction;

    public InstanceTargetItem(InstanceModel instance, bool isInstalled, bool isDatapack, Func<InstanceTargetItem, Task> onAction)
    {
        Instance = instance;
        _isInstalled = isInstalled;
        IsDatapackSelection = isDatapack;
        _onAction = onAction;

        if (isDatapack)
        {
            ButtonText = "Select World →";
        }
        else
        {
            ButtonText = isInstalled ? "Installed ✓" : "Install";
        }
    }

    [RelayCommand]
    public async Task InstallAsync()
    {
        if (IsInstalling || (!IsDatapackSelection && IsInstalled)) return;

        if (IsDatapackSelection)
        {
            if (_onAction != null)
                await _onAction(this);
            return;
        }

        IsInstalling = true;
        ButtonText = "Installing...";
        try
        {
            if (_onAction != null)
                await _onAction(this);
            IsInstalled = true;
            ButtonText = "Installed ✓";
        }
        catch
        {
            ButtonText = "Failed";
        }
        finally
        {
            IsInstalling = false;
        }
    }
}
