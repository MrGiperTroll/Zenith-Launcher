using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Services;

public enum DirectoryMode
{
    Separate,
    Family,
    Shared
}

public class InstanceService : IInstanceService
{
    private readonly string _instancesFilePath;
    private readonly string _instancesDir;
    private List<InstanceModel> _instances = new();
    private string? _selectedInstanceId;
    private readonly object _lock = new();

    public InstanceService()
    {
        var zenithDir = ZenithPaths.AppDataDir;
        _instancesDir = Path.Combine(zenithDir, "instances");
        _instancesFilePath = Path.Combine(zenithDir, "instances.json");

        Directory.CreateDirectory(zenithDir);
        Directory.CreateDirectory(_instancesDir);

        LoadInstances();
    }

    public IReadOnlyList<InstanceModel> GetInstances() { lock (_lock) return _instances.ToList().AsReadOnly(); }

    public InstanceModel? GetSelectedInstance() { lock (_lock) return _instances.FirstOrDefault(i => i.Id == _selectedInstanceId); }

    public void SetSelectedInstance(string instanceId)
    {
        lock (_lock)
        {
            _selectedInstanceId = instanceId;
            foreach (var inst in _instances)
                inst.IsSelected = inst.Id == instanceId;
            SaveInstances();
        }
    }

    public string? SelectedInstanceId
    {
        get { lock (_lock) return _selectedInstanceId; }
    }

    public Task<InstanceModel?> CreateInstanceAsync(string name, string version, string loaderType = "Vanilla", string loaderBuild = "")
    {
        var trimmedName = name.Trim();

        lock (_lock)
        {
            if (_instances.Any(i => i.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<InstanceModel?>(null);

            if (_instances.Any(i => i.Id.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<InstanceModel?>(null);
        }

        var zenithDir = ZenithPaths.AppDataDir;
        var dirMode = GetSelectedDirectoryMode();

        var safeName = Regex.Replace(trimmedName, @"[\\/:*?""<>|]", "_");
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "Profile_" + Guid.NewGuid().ToString("N")[..6];

        var instanceId = Guid.NewGuid().ToString("N");
        string folderPath;
        if (dirMode == DirectoryMode.Family)
        {
            var split = version.Split('.');
            var familyName = split.Length >= 2 ? $"{split[0]}.{split[1]}x" : $"{version}x";
            folderPath = Path.Combine(zenithDir, "families", familyName);
        }
        else if (dirMode == DirectoryMode.Shared)
        {
            folderPath = Path.Combine(zenithDir, "shared_game");
        }
        else
        {
            folderPath = Path.Combine(_instancesDir, safeName);
        }

        Directory.CreateDirectory(folderPath);

        var instance = new InstanceModel
        {
            Id = instanceId,
            Name = trimmedName,
            Version = version,
            LoaderType = loaderType,
            LoaderVersion = loaderBuild,
            Path = folderPath,
            MaxMemoryMb = 4096
        };

        lock (_lock)
        {
            _instances.Add(instance);
            _selectedInstanceId = instance.Id;
            SaveInstances();
        }

        return Task.FromResult<InstanceModel?>(instance);
    }

    public void DeleteInstance(string instanceId)
    {
        lock (_lock)
        {
            var inst = _instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst != null)
            {
                _instances.Remove(inst);
                if (!string.IsNullOrEmpty(inst.Path) && Directory.Exists(inst.Path))
                {
                    try { Directory.Delete(inst.Path, true); } catch { }
                }
                if (_selectedInstanceId == instanceId)
                    _selectedInstanceId = _instances.FirstOrDefault()?.Id;

                SaveInstances();
            }
        }
    }

    public async Task SaveInstanceAsync(InstanceModel instance)
    {
        try
        {
            if (!string.IsNullOrEmpty(instance.Path) && Directory.Exists(instance.Path))
            {
                var jsonPath = Path.Combine(instance.Path, "instance.json");
                var json = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(jsonPath, json);
            }
        }
        catch { }

        lock (_lock) { SaveInstances(); }
    }

    public static DirectoryMode GetSelectedDirectoryMode()
    {
        try
        {
            var configPath = ZenithPaths.ConfigFilePath;
            if (File.Exists(configPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                if (doc.RootElement.TryGetProperty("SelectedDirectoryMode", out var dm))
                {
                    var modeStr = dm.GetString() ?? "";
                    if (modeStr.Contains("Family", StringComparison.OrdinalIgnoreCase))
                        return DirectoryMode.Family;
                    if (modeStr.Contains("Do not use", StringComparison.OrdinalIgnoreCase) || modeStr.Contains("shared", StringComparison.OrdinalIgnoreCase))
                        return DirectoryMode.Shared;
                }
            }
        }
        catch { }
        return DirectoryMode.Separate;
    }

    private void LoadInstances()
    {
        lock (_lock)
        {
            if (File.Exists(_instancesFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_instancesFilePath);
                    var store = JsonSerializer.Deserialize<LocalStore>(json);
                    if (store != null)
                    {
                        _instances = store.Instances ?? new();
                        _selectedInstanceId = store.SelectedInstanceId ?? _instances.FirstOrDefault()?.Id;
                    }
                    else
                    {
                        _instances = new();
                    }
                }
                catch { _instances = new(); }
            }
        }
    }

    private void SaveInstances()
    {
        try
        {
            var store = new LocalStore
            {
                Instances = _instances,
                SelectedInstanceId = _selectedInstanceId
            };
            var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_instancesFilePath, json);
        }
        catch { }
    }

    public async Task<(bool ok, string? error)> RenameInstanceAsync(InstanceModel instance, string newName)
    {
        var trimmed = newName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return (false, L10n.T("rename_empty"));

        var safeName = Regex.Replace(trimmed, @"[\\/:*?""<>|]", "_");
        if (string.IsNullOrWhiteSpace(safeName))
            return (false, L10n.T("rename_empty"));

        lock (_lock)
        {
            if (_instances.Any(i => i.Id != instance.Id &&
                i.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                return (false, L10n.T("rename_conflict"));
        }

        var dirMode = GetSelectedDirectoryMode();
        if (dirMode == DirectoryMode.Separate &&
            !string.IsNullOrEmpty(instance.Path) && Directory.Exists(instance.Path))
        {
            var parentDir = Path.GetDirectoryName(instance.Path);
            if (parentDir == null)
                return (false, L10n.T("rename_error"));

            var newFolderPath = Path.Combine(parentDir, safeName);
            if (Directory.Exists(newFolderPath) &&
                !string.Equals(newFolderPath, instance.Path, StringComparison.OrdinalIgnoreCase))
                return (false, L10n.T("rename_folder_exists"));

            if (!string.Equals(newFolderPath, instance.Path, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.Move(instance.Path, newFolderPath);
                }
                catch (Exception ex)
                {
                    LauncherLog.Error($"Failed to rename instance folder: {instance.Path} -> {newFolderPath}", ex);
                    return (false, L10n.T("rename_error"));
                }
                instance.Path = newFolderPath;
            }
        }

        instance.Name = trimmed;
        await SaveInstanceAsync(instance);
        return (true, null);
    }

    public void ReorderInstances(string movedId, string targetId, bool dropAfter)
    {
        lock (_lock)
        {
            var moved = _instances.FirstOrDefault(i => i.Id == movedId);
            var target = _instances.FirstOrDefault(i => i.Id == targetId);
            if (moved == null || target == null || moved == target) return;

            _instances.Remove(moved);
            var idx = _instances.IndexOf(target);
            if (dropAfter) idx++;
            if (idx < 0) idx = 0;
            if (idx > _instances.Count) idx = _instances.Count;
            _instances.Insert(idx, moved);
            SaveInstances();
        }
    }

    private class LocalStore
    {
        public List<InstanceModel> Instances { get; set; } = new();
        public string? SelectedInstanceId { get; set; }
    }
}
