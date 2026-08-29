using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public enum ContentSortMode
{
    EnabledFirst,
    Name,
    Modified
}

public partial class FileListViewModel : ObservableObject
{
    private readonly string _folder;
    private readonly bool _supportsDisable;
    private readonly bool _worldTarget;
    private readonly string[] _allowedExtensions;
    private readonly Action<string> _reportError;
    private readonly InstanceFileKind _kind;
    private readonly string _instancePath;
    private List<InstanceFileEntry> _all = new();

    /// <summary>Called when a double-clicked entry should open the full project details page.</summary>
    public Action<InstanceFileEntry, InstanceFileKind>? OpenProjectDetails { get; set; }

    public string Title { get; }
    public string EmptyHint { get; }
    public bool SupportsDisable => _supportsDisable;

    /// <summary>True when dropping a folder is meaningful for this section (worlds, resource packs, shader packs).</summary>
    public bool AcceptsFolders =>
        _worldTarget || _kind is InstanceFileKind.ResourcePack or InstanceFileKind.ShaderPack;

    public ObservableCollection<string> SortOptions { get; } = new()
    {
        "Enabled first",
        "Name",
        "Last modified"
    };

    [ObservableProperty]
    private string _selectedSortOption = "Enabled first";

    partial void OnSelectedSortOptionChanged(string value) => ApplyFilter();

    [ObservableProperty]
    private string _searchQuery = "";

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    [ObservableProperty]
    private bool _isDragOver;

    [ObservableProperty]
    private bool _isDragInvalid;

    public ObservableCollection<InstanceFileEntry> Entries { get; } = new();

    [ObservableProperty]
    private InstanceFileEntry? _selectedEntry;

    public bool IsDetailsOpen => SelectedEntry != null;

    partial void OnSelectedEntryChanged(InstanceFileEntry? value)
    {
        OnPropertyChanged(nameof(IsDetailsOpen));
    }

    [ObservableProperty]
    private string _countLabel = "0 items";

    public FileListViewModel(string title, string folder, bool supportsDisable, string emptyHint,
        Action<string> reportError, string[]? allowedExtensions = null, bool worldTarget = false,
        InstanceFileKind kind = InstanceFileKind.Generic, string instancePath = "")
    {
        Title = title;
        _folder = folder;
        _supportsDisable = supportsDisable;
        _worldTarget = worldTarget;
        EmptyHint = emptyHint;
        _reportError = reportError;
        _kind = kind;
        _instancePath = instancePath;
        _allowedExtensions = allowedExtensions ?? new[] { ".jar", ".zip", ".rar", ".7z" };
    }

    public bool IsGalleryMode => _kind == InstanceFileKind.Screenshot;

    public bool SupportsBackup => _kind == InstanceFileKind.World;

    public void Refresh() => _ = RefreshAsync();

    private int _refreshToken;

    /// <summary>
    /// Re-reads the folder on a background thread so heavy metadata/zip reads
    /// never block the UI thread when switching tabs.
    /// </summary>
    public async Task RefreshAsync()
    {
        var token = ++_refreshToken;
        var entries = await Task.Run(ReadEntries);
        if (token != _refreshToken) return; // a newer refresh superseded this one
        _all = entries;
        ApplyFilter();
    }

    private List<InstanceFileEntry> ReadEntries()
    {
        var entries = new List<InstanceFileEntry>();
        if (!Directory.Exists(_folder)) return entries;

        try
        {
            foreach (var f in Directory.GetFiles(_folder))
            {
                var isDisabled = Path.GetExtension(f).Equals(".disabled", StringComparison.OrdinalIgnoreCase);
                var effectivePath = isDisabled ? f[..^9] : f;
                var effectiveExt = Path.GetExtension(effectivePath);
                // Filter to only valid formats for this section (prevents .png sidecars showing as shaders etc.)
                if (_allowedExtensions.Length > 0 && !_allowedExtensions.Contains(effectiveExt, StringComparer.OrdinalIgnoreCase))
                    continue;

                var entry = new InstanceFileEntry
                {
                    Name = Path.GetFileName(f),
                    FullPath = f,
                    IsDirectory = false,
                    IsEnabled = !isDisabled,
                    ModifiedAt = SafeModified(f)
                };
                Enrich(entry);
                entries.Add(entry);
            }
            foreach (var d in Directory.GetDirectories(_folder))
            {
                var disabled = d.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                var entry = new InstanceFileEntry
                {
                    Name = Path.GetFileName(d),
                    FullPath = d,
                    IsDirectory = true,
                    IsEnabled = !disabled,
                    ModifiedAt = SafeModified(d)
                };
                Enrich(entry);
                entries.Add(entry);
            }
        }
        catch (Exception ex)
        {
            _reportError($"Unable to read '{_folder}': {ex.Message}");
        }
        return entries;
    }

    private void Enrich(InstanceFileEntry entry)
    {
        if (_kind == InstanceFileKind.Generic) return;
        entry.Kind = _kind;
        entry.Title = entry.IsDirectory
            ? entry.Name.TrimEnd(Path.DirectorySeparatorChar)
            : Path.GetFileNameWithoutExtension(entry.Name);
        if (_kind == InstanceFileKind.Screenshot) entry.Title = entry.Name;
        try
        {
            ContentMetadataReader.Enrich(entry);
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to read metadata for '{entry.FullPath}'", ex);
        }

        // Look up Modrinth project ID from the installed registry
        if (!entry.IsDirectory && !string.IsNullOrEmpty(_instancePath))
        {
            var projectId = FindProjectIdByFileName(entry.Name);
            if (!string.IsNullOrEmpty(projectId))
                entry.ModrinthProjectId = projectId;
        }
    }

    private Dictionary<string, string> LoadInstalledMap()
    {
        if (string.IsNullOrEmpty(_instancePath)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_instancePath)))[..12];
        var path = Path.Combine(ZenithPaths.AppDataDir, "cache", "installed", $"{hash}.json");
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(StringComparer.OrdinalIgnoreCase); } catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private string FindProjectIdByFileName(string fileName)
    {
        var map = LoadInstalledMap();
        foreach (var kvp in map)
        {
            if (string.Equals(kvp.Value, fileName, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
            // Also check without .disabled extension
            if (fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(kvp.Value, fileName[..^9], StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        }
        return "";
    }

    private static DateTime SafeModified(string path)
    {
        try { return File.GetLastWriteTime(path); }
        catch { return DateTime.MinValue; }
    }

    private void ApplyFilter()
    {
        IEnumerable<InstanceFileEntry> items = _all;

        var q = SearchQuery?.Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var ql = q.ToLowerInvariant();
            items = items.Where(e => e.Name.ToLowerInvariant().Contains(ql));
        }

        items = SelectedSortOption switch
        {
            "Last modified" => items.OrderByDescending(e => e.ModifiedAt).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            "Name" => items.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            _ => items.OrderByDescending(e => e.IsEnabled).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        };

        Entries.Clear();
        foreach (var e in items)
            Entries.Add(e);

        CountLabel = string.Format(L10n.T("fl_items"), Entries.Count);
    }

    [RelayCommand]
    private void ToggleFile(InstanceFileEntry entry)
    {
        if (entry == null) return;
        try
        {
            if (!entry.IsEnabled)
            {
                if (entry.FullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var target = entry.FullPath[..^9];
                    if (!File.Exists(target) && !Directory.Exists(target))
                    {
                        if (Directory.Exists(entry.FullPath)) Directory.Move(entry.FullPath, target);
                        else File.Move(entry.FullPath, target);
                        entry.IsEnabled = true;
                    }
                }
            }
            else if (_supportsDisable)
            {
                var target = entry.FullPath + ".disabled";
                if (!File.Exists(target) && !Directory.Exists(target))
                {
                    if (Directory.Exists(entry.FullPath)) Directory.Move(entry.FullPath, target);
                    else File.Move(entry.FullPath, target);
                    entry.IsEnabled = false;
                }
            }
        }
        catch (Exception ex)
        {
            _reportError($"Failed to toggle {entry.Name}: {ex.Message}");
        }
        ApplyFilter();
    }

    [RelayCommand]
    private void DeleteFile(InstanceFileEntry entry)
    {
        if (entry == null) return;
        try
        {
            if (Directory.Exists(entry.FullPath)) Directory.Delete(entry.FullPath, true);
            else if (File.Exists(entry.FullPath)) File.Delete(entry.FullPath);
        }
        catch (Exception ex)
        {
            _reportError($"Failed to delete {entry.Name}: {ex.Message}");
        }
        Refresh();
    }

    [RelayCommand]
    private void EnableAll()
    {
        foreach (var e in _all.Where(e => !e.IsEnabled).ToList())
            ToggleFile(e);
    }

    [RelayCommand]
    private void DisableAll()
    {
        if (!_supportsDisable) return;
        foreach (var e in _all.Where(e => e.IsEnabled).ToList())
            ToggleFile(e);
    }

    [RelayCommand]
    private void RefreshList()
    {
        Refresh();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            Process.Start(new ProcessStartInfo { FileName = _folder, UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex)
        {
            _reportError($"Failed to open folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenFile(InstanceFileEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.FullPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = entry.FullPath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _reportError($"Failed to open {entry.Name}: {ex.Message}");
        }
    }

    [RelayCommand]
    private void LocateFile(InstanceFileEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.FullPath)) return;
        try
        {
            var path = entry.FullPath;
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "open" });
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
            _reportError($"Opened location of {entry.Title}.");
        }
        catch (Exception ex)
        {
            _reportError($"Failed to locate {entry.Title}: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ShowDetails(InstanceFileEntry entry)
    {
        if (entry == null) return;

        if (OpenProjectDetails != null)
        {
            OpenProjectDetails(entry, _kind);
            return;
        }

        SelectedEntry = entry;
    }

    [RelayCommand]
    private void BackFromDetails() => SelectedEntry = null;

    public bool IsValidImportPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (Directory.Exists(path)) return AcceptsFolders;
            if (!File.Exists(path)) return false;
            var ext = Path.GetExtension(path);
            return _allowedExtensions.Length == 0 || _allowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Counts how many of the dropped paths are acceptable for this section.</summary>
    public (int Valid, int Invalid) EvaluateDropPaths(IEnumerable<string> paths)
    {
        var valid = 0;
        var invalid = 0;
        foreach (var p in paths)
        {
            if (IsValidImportPath(p)) valid++;
            else invalid++;
        }
        return (valid, invalid);
    }

    public void SetDragState(bool hasFiles, bool hasValid)
    {
        IsDragOver = hasFiles && hasValid;
        IsDragInvalid = hasFiles && !hasValid;
    }

    /// <summary>Zips a world folder into an adjacent 'backups' directory (sibling of the saves folder).</summary>
    [RelayCommand]
    private void BackupFile(InstanceFileEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.FullPath) || !Directory.Exists(entry.FullPath)) return;
        try
        {
            var parent = Directory.GetParent(_folder)?.FullName ?? "";
            var backupRoot = Path.Combine(parent, "backups");
            Directory.CreateDirectory(backupRoot);

            var name = Path.GetFileName(entry.FullPath.TrimEnd(Path.DirectorySeparatorChar));
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            var dest = Path.Combine(backupRoot, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            if (File.Exists(dest)) File.Delete(dest);

            ZipFile.CreateFromDirectory(entry.FullPath, dest, CompressionLevel.Optimal, false);
            _reportError($"World backup created: {dest}");
        }
        catch (Exception ex)
        {
            _reportError($"Backup of {entry.Name} failed: {ex.Message}");
        }
    }

    public void ImportPaths(IEnumerable<string> paths)
    {
        var imported = 0;
        var ignored = 0;
        var invalid = 0;
        try
        {
            Directory.CreateDirectory(_folder);
            foreach (var path in paths)
            {
                try
                {
                    if (!IsValidImportPath(path))
                    {
                        invalid++;
                        continue;
                    }
                    if (Directory.Exists(path))
                    {
                        if (Path.GetDirectoryName(path)?.TrimEnd(Path.DirectorySeparatorChar)
                            .Equals(_folder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) == true)
                            continue;

                        if (_worldTarget)
                        {
                            var dst = Path.Combine(_folder, Path.GetFileName(path));
                            if (!Directory.Exists(dst))
                            {
                                CopyDirectoryRecursive(path, dst);
                                imported++;
                            }
                            else ignored++;
                        }
                        else if (Path.GetFileName(path).EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            ignored++;
                        }
                        else
                        {
                            var dst = Path.Combine(_folder, Path.GetFileName(path));
                            if (!Directory.Exists(dst))
                            {
                                CopyDirectoryRecursive(path, dst);
                                imported++;
                            }
                            else ignored++;
                        }
                        continue;
                    }

                    if (!File.Exists(path)) { ignored++; continue; }

                    var ext = Path.GetExtension(path);
                    if (_allowedExtensions.Length > 0 && !_allowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    {
                        ignored++;
                        continue;
                    }

                    var target = Path.Combine(_folder, Path.GetFileName(path));
                    if (_worldTarget && ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ImportWorldZip(path)) imported++;
                        else ignored++;
                        continue;
                    }

                    if (File.Exists(target)) { ignored++; continue; }
                    File.Copy(path, target);
                    imported++;
                }
                catch (Exception ex)
                {
                    _reportError($"Failed to import {Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _reportError($"Import failed: {ex.Message}");
        }
        Refresh();

        if (imported > 0)
        {
            var verb = imported == 1 ? "item" : "items";
            var msg = $"{imported} {verb} imported to {Title}.";
            if (invalid > 0) msg += $" {invalid} skipped (invalid format).";
            _reportError(msg);
        }
        else if (ignored > 0 || invalid > 0)
        {
            var bits = new List<string>();
            if (ignored > 0) bits.Add($"{ignored} already present");
            if (invalid > 0) bits.Add($"{invalid} invalid format");
            _reportError($"Nothing new imported — {string.Join(", ", bits)}.");
        }
    }

    private bool ImportWorldZip(string zipPath)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"zenith_world_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, temp);

            var topLevel = Directory.GetDirectories(temp)
                .OrderByDescending(d => Directory.Exists(Path.Combine(d, "level.dat")))
                .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            var worldDir = topLevel ?? temp;
            var worldName = Directory.Exists(worldDir) && worldDir != temp
                ? Path.GetFileName(worldDir)
                : Path.GetFileNameWithoutExtension(zipPath);

            var dst = Path.Combine(_folder, worldName);
            if (Directory.Exists(dst)) return false;
            CopyDirectoryRecursive(worldDir, dst);
            return true;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to install world from archive", ex);
            return false;
        }
        finally
        {
            try { Directory.Delete(temp, true); }
            catch (Exception ex) { LauncherLog.Error("Failed to clean temp world import dir", ex); }
        }
    }

    private static void CopyDirectoryRecursive(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}