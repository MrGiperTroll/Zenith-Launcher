using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZenithLauncher.Models;

namespace ZenithLauncher.Services;

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
    private readonly string _instancesDirFullPrefix;
    private List<InstanceModel> _instances = new();
    private string? _selectedInstanceId;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _instanceWriteLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _watcherGate = new();
    private CancellationTokenSource? _watcherDebounceCts;
    private int _suppressWatcherCount;
    private long _suppressWatcherUntilTicks;
    private FileSystemWatcher? _watcher;

    public InstanceService()
    {
        var zenithDir = ZenithPaths.AppDataDir;
        _instancesDir = Path.Combine(zenithDir, "instances");
        _instancesDirFullPrefix = Path.GetFullPath(_instancesDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        _instancesFilePath = Path.Combine(zenithDir, "instances.json");

        Directory.CreateDirectory(zenithDir);
        Directory.CreateDirectory(_instancesDir);

        LoadInstances();

        try
        {
            _watcher = new FileSystemWatcher(_instancesDir)
            {
                NotifyFilter = NotifyFilters.DirectoryName
            };
            _watcher.Created += OnWatcherCreated;
            _watcher.Deleted += OnWatcherDeleted;
            _watcher.Renamed += OnWatcherRenamed;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"InstanceService FileSystemWatcher disabled: {ex.Message}");
        }
    }

    public event Action? InstancesChanged;

    private void BeginSuppressWatcher()
    {
        Interlocked.Increment(ref _suppressWatcherCount);
        Interlocked.Exchange(ref _suppressWatcherUntilTicks, Environment.TickCount64 + 1000);
    }

    private void EndSuppressWatcher()
    {
        Interlocked.Exchange(ref _suppressWatcherUntilTicks, Environment.TickCount64 + 1000);
        var remaining = Interlocked.Decrement(ref _suppressWatcherCount);
        if (remaining < 0)
            Interlocked.Exchange(ref _suppressWatcherCount, 0);
    }

    private bool IsWatcherSuppressed() =>
        Volatile.Read(ref _suppressWatcherCount) > 0 ||
        Environment.TickCount64 < Volatile.Read(ref _suppressWatcherUntilTicks);

    private bool IsUnderInstancesDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.StartsWith(_instancesDirFullPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void OnWatcherCreated(object sender, FileSystemEventArgs e)
    {
        if (IsWatcherSuppressed()) return;
        ScheduleWatcherReload();
    }

    private void OnWatcherDeleted(object sender, FileSystemEventArgs e)
    {
        if (IsWatcherSuppressed()) return;
        ScheduleWatcherReload(pruneDeletedUnderInstancesDir: true);
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        if (IsWatcherSuppressed()) return;

        bool changed = false;
        lock (_lock)
        {
            var renamed = _instances.FirstOrDefault(i =>
                !string.IsNullOrWhiteSpace(i.Path) &&
                string.Equals(Path.GetFullPath(i.Path), Path.GetFullPath(e.OldFullPath), StringComparison.OrdinalIgnoreCase));

            if (renamed != null)
            {
                renamed.Path = e.FullPath;
                if (!string.IsNullOrWhiteSpace(e.Name) &&
                    string.Equals(renamed.Name, Path.GetFileName(e.OldFullPath), StringComparison.OrdinalIgnoreCase))
                {
                    renamed.Name = e.Name;
                }
                SaveInstances();
                changed = true;
            }
        }

        if (changed)
            InstancesChanged?.Invoke();
        else
            ScheduleWatcherReload(pruneDeletedUnderInstancesDir: true);
    }

    private void ScheduleWatcherReload(bool pruneDeletedUnderInstancesDir = false)
    {
        CancellationToken token;
        lock (_watcherGate)
        {
            _watcherDebounceCts?.Cancel();
            _watcherDebounceCts?.Dispose();
            _watcherDebounceCts = new CancellationTokenSource();
            token = _watcherDebounceCts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token).ConfigureAwait(false);
                if (token.IsCancellationRequested || IsWatcherSuppressed())
                    return;

                if (pruneDeletedUnderInstancesDir)
                    PruneDeletedInstancesDirEntries();

                LoadInstances();
                InstancesChanged?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LauncherLog.Warn($"InstanceService watcher reload error: {ex.Message}");
            }
        }, token);
    }

    private void PruneDeletedInstancesDirEntries()
    {
        lock (_lock)
        {
            int removed = _instances.RemoveAll(i => IsUnderInstancesDir(i.Path) && !Directory.Exists(i.Path));
            if (removed > 0)
            {
                if (string.IsNullOrWhiteSpace(_selectedInstanceId) || !_instances.Any(i => i.Id == _selectedInstanceId))
                    _selectedInstanceId = _instances.FirstOrDefault()?.Id;

                foreach (var inst in _instances)
                    inst.IsSelected = inst.Id == _selectedInstanceId;

                SaveInstances();
            }
        }
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

        BeginSuppressWatcher();
        try
        {
            Directory.CreateDirectory(folderPath);
        }
        finally
        {
            EndSuppressWatcher();
        }

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
        string? pathToDel = null;
        lock (_lock)
        {
            var inst = _instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst != null)
            {
                _instances.Remove(inst);
                pathToDel = inst.Path;
                if (_selectedInstanceId == instanceId)
                    _selectedInstanceId = _instances.FirstOrDefault()?.Id;

                SaveInstances();
            }
        }

        if (!string.IsNullOrEmpty(pathToDel) && Directory.Exists(pathToDel))
        {
            BeginSuppressWatcher();
            try
            {
                Directory.Delete(pathToDel, true);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Failed to delete instance directory '{pathToDel}': {ex.Message}");
            }
            finally
            {
                EndSuppressWatcher();
            }
        }
    }

    public async Task DeleteInstanceAsync(string instanceId)
    {
        string? pathToDel = null;
        lock (_lock)
        {
            var inst = _instances.FirstOrDefault(i => i.Id == instanceId);
            if (inst != null)
            {
                _instances.Remove(inst);
                pathToDel = inst.Path;
                if (_selectedInstanceId == instanceId)
                    _selectedInstanceId = _instances.FirstOrDefault()?.Id;

                SaveInstances();
            }
        }

        if (!string.IsNullOrEmpty(pathToDel) && Directory.Exists(pathToDel))
        {
            await Task.Run(() =>
            {
                BeginSuppressWatcher();
                try
                {
                    Directory.Delete(pathToDel, true);
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn($"Failed to delete instance directory '{pathToDel}': {ex.Message}");
                }
                finally
                {
                    EndSuppressWatcher();
                }
            }).ConfigureAwait(false);
        }
    }

    public async Task SaveInstanceAsync(InstanceModel instance)
    {
        if (instance == null) return;

        var lockKey = !string.IsNullOrWhiteSpace(instance.Id)
            ? instance.Id
            : (!string.IsNullOrWhiteSpace(instance.Path) ? Path.GetFullPath(instance.Path) : "default");
        var writeGate = _instanceWriteLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(instance.Path) && Directory.Exists(instance.Path))
            {
                var jsonPath = Path.Combine(instance.Path, "instance.json");
                var json = JsonSerializer.Serialize(instance, LauncherJsonContext.Default.InstanceModel);
                await AtomicFileWriter.WriteAllTextAsync(jsonPath, json).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to save instance.json for '{instance.Name}'", ex);
        }
        finally
        {
            writeGate.Release();
        }

        lock (_lock) { SaveInstances(); }
    }

    public static DirectoryMode GetSelectedDirectoryMode()
    {
        try
        {
            var cfg = LauncherConfigData.Load();
            var modeStr = cfg.SelectedDirectoryMode ?? "";
            if (modeStr.Contains("Family", StringComparison.OrdinalIgnoreCase))
                return DirectoryMode.Family;
            if (modeStr.Contains("Do not use", StringComparison.OrdinalIgnoreCase) || modeStr.Contains("shared", StringComparison.OrdinalIgnoreCase))
                return DirectoryMode.Shared;
        }
        catch { }
        return DirectoryMode.Separate;
    }

    private void LoadInstances()
    {
        lock (_lock)
        {
            bool needsCleanupSave = false;
            if (File.Exists(_instancesFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_instancesFilePath);
                    var store = JsonSerializer.Deserialize(json, LauncherJsonContext.Default.InstancesStore);
                    if (store != null)
                    {
                        var raw = store.Instances ?? new();
                        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        _instances = new List<InstanceModel>(raw.Count);
                        foreach (var inst in raw)
                        {
                            if (inst == null || string.IsNullOrWhiteSpace(inst.Path) || string.IsNullOrWhiteSpace(inst.Name))
                            {
                                needsCleanupSave = true;
                                continue;
                            }
                            if (!seenPaths.Add(inst.Path))
                            {
                                needsCleanupSave = true;
                                continue;
                            }
                            _instances.Add(inst);
                        }
                        _selectedInstanceId = store.SelectedInstanceId;
                        if (string.IsNullOrWhiteSpace(_selectedInstanceId) || !_instances.Any(i => i.Id == _selectedInstanceId))
                            _selectedInstanceId = _instances.FirstOrDefault()?.Id;
                    }
                    else
                    {
                        _instances = new();
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Error("Failed to load instances.json", ex);
                    _instances = new();
                }
            }

            // Auto-discovery of manually dragged profile folders
            if (Directory.Exists(_instancesDir))
            {
                bool discovered = false;
                foreach (var dir in Directory.GetDirectories(_instancesDir))
                {
                    if (_instances.Any(i => string.Equals(i.Path, dir, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var instName = Path.GetFileName(dir);
                    var instJsonPath = Path.Combine(dir, "instance.json");
                    InstanceModel? newInst = null;
                    if (File.Exists(instJsonPath))
                    {
                        try
                        {
                            var jsonText = File.ReadAllText(instJsonPath);
                            var parsed = JsonSerializer.Deserialize(jsonText, LauncherJsonContext.Default.InstanceModel);
                            if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Id))
                                newInst = parsed;
                        }
                        catch { }
                    }

                    if (newInst == null)
                    {
                        newInst = new InstanceModel
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = instName,
                            Version = "1.20.1",
                            LoaderType = "Vanilla",
                            Path = dir,
                            CreatedAt = Directory.GetCreationTime(dir)
                        };
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(newInst.Path)) newInst.Path = dir;
                        if (string.IsNullOrEmpty(newInst.Name)) newInst.Name = instName;
                    }
                    _instances.Add(newInst);
                    discovered = true;
                }
                if (discovered || needsCleanupSave)
                {
                    // Save them back immediately so they persist cleanly
                    if (string.IsNullOrWhiteSpace(_selectedInstanceId) || !_instances.Any(i => i.Id == _selectedInstanceId))
                        _selectedInstanceId = _instances.FirstOrDefault()?.Id;
                    SaveInstances();
                }
            }
            else if (needsCleanupSave)
            {
                SaveInstances();
            }
        }
    }

    private void SaveInstances()
    {
        try
        {
            var store = new InstancesStore
            {
                Instances = _instances,
                SelectedInstanceId = _selectedInstanceId
            };
            var json = JsonSerializer.Serialize(store, LauncherJsonContext.Default.InstancesStore);
            AtomicFileWriter.WriteAllText(_instancesFilePath, json);
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to save instances.json", ex);
        }
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
                BeginSuppressWatcher();
                try
                {
                    Directory.Move(instance.Path, newFolderPath);
                }
                catch (Exception ex)
                {
                    LauncherLog.Error($"Failed to rename instance folder: {instance.Path} -> {newFolderPath}", ex);
                    return (false, L10n.T("rename_error"));
                }
                finally
                {
                    EndSuppressWatcher();
                }
                instance.Path = newFolderPath;
            }
        }

        instance.Name = trimmed;
        await SaveInstanceAsync(instance).ConfigureAwait(false);
        return (true, null);
    }

    public void ReorderInstances(string movedId, string targetId, bool dropAfter, bool save = true)
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
            if (save) SaveInstances();
        }
    }

    public void SetInstanceOrder(IReadOnlyList<string> orderedIds)
    {
        lock (_lock)
        {
            var byId = _instances.ToDictionary(i => i.Id);
            _instances.Clear();
            foreach (var id in orderedIds)
            {
                if (byId.TryGetValue(id, out var inst))
                    _instances.Add(inst);
            }
            SaveInstances();
        }
    }
}


