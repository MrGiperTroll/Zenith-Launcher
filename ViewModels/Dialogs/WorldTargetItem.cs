using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Services;

namespace ZenithLauncher.ViewModels.Dialogs;

public sealed partial class WorldTargetItem : ObservableObject, IDisposable
{
    public string FolderName { get; }
    public string WorldName { get; }
    public string WorldPath { get; }
    public string MetaSummary { get; }
    public bool HasMetaSummary => !string.IsNullOrWhiteSpace(MetaSummary);
    public string SavesRelativePath => $"saves/{FolderName}";
    public Bitmap? IconBitmap { get; }
    public bool HasIcon => IconBitmap != null;

    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _isHovered;
    [ObservableProperty] private string _buttonText = L10n.T("btn_install");

    private readonly Func<WorldTargetItem, Task> _onInstall;
    private readonly Func<WorldTargetItem, Task>? _onUninstall;

    public WorldTargetItem(
        string folderName,
        string worldName,
        string worldPath,
        string metaSummary,
        bool isInstalled,
        Func<WorldTargetItem, Task> onInstall,
        Func<WorldTargetItem, Task>? onUninstall = null)
    {
        FolderName = folderName;
        WorldName = !string.IsNullOrWhiteSpace(worldName) ? worldName : folderName;
        WorldPath = worldPath;
        MetaSummary = metaSummary ?? "";
        _isInstalled = isInstalled;
        _onInstall = onInstall;
        _onUninstall = onUninstall;
        UpdateButtonText();

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

    partial void OnIsInstalledChanged(bool value) => UpdateButtonText();
    partial void OnIsInstallingChanged(bool value) => UpdateButtonText();
    partial void OnIsHoveredChanged(bool value) => UpdateButtonText();

    private void UpdateButtonText()
    {
        if (IsInstalling)
        {
            ButtonText = L10n.T("btn_installing");
            return;
        }
        if (IsInstalled)
        {
            ButtonText = IsHovered ? L10n.T("mi_delete") : L10n.T("btn_installed");
            return;
        }
        ButtonText = L10n.T("btn_install");
    }

    [RelayCommand]
    public async Task InstallAsync()
    {
        if (IsInstalling) return;

        if (IsInstalled)
        {
            if (_onUninstall == null) return;
            IsInstalling = true;
            try
            {
                await _onUninstall(this);
                IsInstalled = false;
                IsHovered = false;
            }
            catch
            {
                ButtonText = L10n.T("btn_failed");
            }
            finally
            {
                IsInstalling = false;
            }
            return;
        }

        IsInstalling = true;
        try
        {
            await _onInstall(this);
            IsInstalled = true;
        }
        catch
        {
            ButtonText = L10n.T("btn_failed");
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
