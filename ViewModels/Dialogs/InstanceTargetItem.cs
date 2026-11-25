using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Models;
using ZenithLauncher.Services;

namespace ZenithLauncher.ViewModels.Dialogs;

public sealed record AddVersionOption(
    string Id,
    string Label,
    string Subtitle,
    string FileName,
    string FileUrl,
    string[] GameVersions,
    string[] Loaders,
    bool IsAuto = false)
{
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
}

public sealed partial class InstanceTargetItem : ObservableObject
{
    public InstanceModel Instance { get; }
    public string Name => Instance.Name;
    public string VersionDisplay => $"{Instance.Version} {Instance.LoaderType}";
    public string? IconPath => Instance.IconPath;
    public bool HasIcon => Instance.HasCustomIcon;
    public string GroupName => (Instance.Group ?? "").Trim();
    public bool HasGroup => !string.IsNullOrWhiteSpace(GroupName);
    public bool IsCompatible { get; }
    public string CompatibilityLabel => IsCompatible ? L10n.T("add_inst_compat_ok") : L10n.T("add_inst_compat_mismatch");
    public bool IsDatapackSelection { get; }

    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _isHovered;
    [ObservableProperty] private string _buttonText = L10n.T("btn_install");

    public bool CanClickButton => !IsInstalling;

    private readonly Func<InstanceTargetItem, Task> _onInstall;
    private readonly Func<InstanceTargetItem, Task>? _onUninstall;

    public InstanceTargetItem(
        InstanceModel instance,
        bool isInstalled,
        bool isDatapack,
        bool isCompatible,
        Func<InstanceTargetItem, Task> onInstall,
        Func<InstanceTargetItem, Task>? onUninstall = null)
    {
        Instance = instance;
        _isInstalled = isInstalled;
        IsDatapackSelection = isDatapack;
        IsCompatible = isCompatible;
        _onInstall = onInstall;
        _onUninstall = onUninstall;
        UpdateButtonText();
    }

    partial void OnIsInstalledChanged(bool value) => UpdateButtonText();
    partial void OnIsInstallingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanClickButton));
        UpdateButtonText();
    }
    partial void OnIsHoveredChanged(bool value) => UpdateButtonText();

    private void UpdateButtonText()
    {
        if (IsInstalling)
        {
            ButtonText = L10n.T("btn_installing");
            return;
        }
        if (IsDatapackSelection)
        {
            ButtonText = L10n.T("btn_select_world");
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

        if (IsDatapackSelection)
        {
            if (_onInstall != null)
                await _onInstall(this);
            return;
        }

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
            if (_onInstall != null)
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
}
