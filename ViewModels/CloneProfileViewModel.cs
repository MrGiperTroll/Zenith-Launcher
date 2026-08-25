using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class CloneProfileViewModel : ObservableObject
{
    private readonly InstanceModel _source;
    private readonly IInstanceService _instanceService;

    [ObservableProperty] private string _profileName;
    [ObservableProperty] private bool _selectAll = true;
    [ObservableProperty] private bool _copyMods = true;
    [ObservableProperty] private bool _copyResourcePacks = true;
    [ObservableProperty] private bool _copyShaderPacks = true;
    [ObservableProperty] private bool _copyConfigs = true;
    [ObservableProperty] private bool _copyWorlds = true;
    [ObservableProperty] private bool _copyDataPacks = true;

    public string SourceTitle => $"{_source.Name} • {_source.LoaderType} • Minecraft {_source.Version}";
    public bool IsNameDuplicate => !string.IsNullOrWhiteSpace(ProfileName)
        && _instanceService.GetInstances().Any(i => i.Name.Equals(ProfileName.Trim(), StringComparison.OrdinalIgnoreCase));
    public bool CanClone => !string.IsNullOrWhiteSpace(ProfileName) && !IsNameDuplicate;

    public event Action<InstanceModel>? Cloned;
    public event Action? RequestClose;

    public CloneProfileViewModel(InstanceModel source, IInstanceService instanceService)
    {
        _source = source;
        _instanceService = instanceService;
        _profileName = $"{source.Name} (Clone)";
    }

    partial void OnProfileNameChanged(string value)
    {
        OnPropertyChanged(nameof(IsNameDuplicate));
        OnPropertyChanged(nameof(CanClone));
    }

    partial void OnSelectAllChanged(bool value) => SetAllComponents(value);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();

    private void SetAllComponents(bool v)
    {
        CopyMods = v;
        CopyResourcePacks = v;
        CopyShaderPacks = v;
        CopyConfigs = v;
        CopyWorlds = v;
        CopyDataPacks = v;
    }

    [RelayCommand]
    private async Task CloneAsync()
    {
        if (!CanClone) return;

        var name = ProfileName.Trim();
        var created = await _instanceService.CreateInstanceAsync(name, _source.Version, _source.LoaderType);
        if (created == null) return;

        try
        {
            if (Directory.Exists(_source.Path) && Directory.Exists(created.Path))
            {
                if (CopyMods) CopyDir(created, "mods");
                if (CopyResourcePacks) CopyDir(created, "resourcepacks");
                if (CopyShaderPacks) CopyDir(created, "shaderpacks");
                if (CopyConfigs)
                {
                    CopyDir(created, "config");
                    CopyFile(created, "options.txt");
                }
                if (CopyDataPacks)
                {
                    // Data packs live inside each world folder
                    var savesSrc = Path.Combine(_source.Path, "saves");
                    if (Directory.Exists(savesSrc))
                        foreach (var world in Directory.GetDirectories(savesSrc))
                            CopyAbsolute(created, Path.Combine(world, "datapacks"));

                    // Also copy a root-level datapacks folder when present
                    CopyDir(created, "datapacks");
                }
                if (CopyWorlds) CopyDir(created, "saves");

                // Carry over the profile icon when present
                foreach (var iconFile in Directory.GetFiles(_source.Path, "icon.*"))
                {
                    try
                    {
                        var dest = Path.Combine(created.Path, Path.GetFileName(iconFile));
                        File.Copy(iconFile, dest, true);
                        created.IconPath = dest;
                    }
                    catch { }
                }
            }
        }
        catch { }

        await _instanceService.SaveInstanceAsync(created);
        Cloned?.Invoke(created);
        RequestClose?.Invoke();
    }

    private void CopyDir(InstanceModel target, string relativeFolder)
    {
        var src = Path.Combine(_source.Path, relativeFolder);
        if (!Directory.Exists(src)) return;
        CopyTree(src, Path.Combine(target.Path, relativeFolder));
    }

    private void CopyFile(InstanceModel target, string relativeFile)
    {
        var src = Path.Combine(_source.Path, relativeFile);
        if (!File.Exists(src)) return;
        File.Copy(src, Path.Combine(target.Path, relativeFile), true);
    }

    private void CopyAbsolute(InstanceModel target, string absoluteSource)
    {
        if (!Directory.Exists(absoluteSource)) return;
        var rel = Path.GetRelativePath(_source.Path, absoluteSource);
        CopyTree(absoluteSource, Path.Combine(target.Path, rel));
    }

    private static void CopyTree(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);
        foreach (var file in dir.GetFiles())
            file.CopyTo(Path.Combine(destinationDir, file.Name), true);

        foreach (var subDir in dir.GetDirectories())
            CopyTree(subDir.FullName, Path.Combine(destinationDir, subDir.Name));
    }
}
