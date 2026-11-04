using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CustomMcLauncher.ViewModels.Dialogs;

public sealed partial class WorldTargetItem : ObservableObject, IDisposable
{
    public string FolderName { get; }
    public string WorldName { get; }
    public string WorldPath { get; }
    public Bitmap? IconBitmap { get; }
    public bool HasIcon => IconBitmap != null;

    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private string _buttonText = "Install";

    private readonly Func<WorldTargetItem, Task> _onInstall;

    public WorldTargetItem(string folderName, string worldName, string worldPath, bool isInstalled, Func<WorldTargetItem, Task> onInstall)
    {
        FolderName = folderName;
        WorldName = !string.IsNullOrWhiteSpace(worldName) ? worldName : folderName;
        WorldPath = worldPath;
        _isInstalled = isInstalled;
        _onInstall = onInstall;
        ButtonText = isInstalled ? "Installed ✓" : "Install";

        var iconFile = Path.Combine(worldPath, "icon.png");
        if (File.Exists(iconFile))
        {
            try
            {
                using var fs = File.OpenRead(iconFile);
                IconBitmap = Bitmap.DecodeToWidth(fs, 64);
            }
            catch { }
        }
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

    public void Dispose()
    {
        try { IconBitmap?.Dispose(); } catch { }
    }
}
