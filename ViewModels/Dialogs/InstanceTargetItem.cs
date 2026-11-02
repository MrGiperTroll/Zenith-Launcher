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
    public bool HasIcon => !string.IsNullOrEmpty(IconPath) && File.Exists(IconPath);

    public Avalonia.Media.Imaging.Bitmap? IconBitmap
    {
        get
        {
            if (HasIcon && File.Exists(IconPath))
            {
                try { return new Avalonia.Media.Imaging.Bitmap(IconPath); } catch { }
            }
            return null;
        }
    }

    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private string _buttonText = "Install";

    private readonly Func<InstanceTargetItem, Task> _onInstall;

    public InstanceTargetItem(InstanceModel instance, bool isInstalled, Func<InstanceTargetItem, Task> onInstall)
    {
        Instance = instance;
        _isInstalled = isInstalled;
        _onInstall = onInstall;
        ButtonText = isInstalled ? "Installed ✓" : "Install";
    }

    [RelayCommand]
    public async Task InstallAsync()
    {
        if (IsInstalled || IsInstalling) return;
        IsInstalling = true;
        ButtonText = "Installing...";
        try
        {
            await _onInstall(this);
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
