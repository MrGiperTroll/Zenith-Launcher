using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class ServerManagerViewModel : ViewModelBase
{
    private readonly string _serversDatPath;

    public ObservableCollection<ServerEntry> Servers { get; } = new();

    [ObservableProperty] private string _statusText = "";

    [ObservableProperty] private bool _isEditorOpen;

    [ObservableProperty] private string _editorName = "";

    [ObservableProperty] private string _editorIp = "";

    [ObservableProperty] private bool _editorAcceptTextures;

    [ObservableProperty] private bool _editorHideAddress;

    private ServerEntry? _editing;

    public ServerManagerViewModel(string serversDatPath)
    {
        _serversDatPath = serversDatPath;
        Reload();
    }

    private void Reload()
    {
        Servers.Clear();
        foreach (var s in ServersDatService.Read(_serversDatPath))
            Servers.Add(s);
        StatusText = Servers.Count == 0
            ? "No servers. Add your first server below."
            : $"{Servers.Count} server(s) in servers.dat";
    }

    private void ClearEditor()
    {
        _editing = null;
        EditorName = "";
        EditorIp = "";
        EditorAcceptTextures = false;
        EditorHideAddress = false;
        IsEditorOpen = false;
    }

    [RelayCommand]
    private void AddServer()
    {
        _editing = null;
        EditorName = "";
        EditorIp = "";
        EditorAcceptTextures = false;
        EditorHideAddress = false;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void EditServer(ServerEntry entry)
    {
        if (entry == null) return;
        _editing = entry;
        EditorName = entry.Name;
        EditorIp = entry.Ip;
        EditorAcceptTextures = entry.AcceptTextures;
        EditorHideAddress = entry.HideAddress;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void DeleteServer(ServerEntry entry)
    {
        if (entry == null) return;
        Servers.Remove(entry);
        Save();
    }

    [RelayCommand]
    private async Task PickIconAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Server Icon (PNG, 64×64)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("PNG image") { Patterns = new[] { "*.png" } } }
        });
        if (files.Count == 0) return;

        try
        {
            var bytes = await File.ReadAllBytesAsync(files[0].Path.LocalPath);
            if (bytes.Length > 64 * 1024)
            {
                StatusText = "Icon too large — max 64 KB.";
                return;
            }
            if (_editing == null) _editing = new ServerEntry();
            _editing.IconBytes = bytes;
            StatusText = "Icon selected (applied on save).";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load icon: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        ClearEditor();
    }

    [RelayCommand]
    private void SaveEditor()
    {
        if (string.IsNullOrWhiteSpace(EditorName) || string.IsNullOrWhiteSpace(EditorIp))
        {
            StatusText = "Name and IP address are required.";
            return;
        }

        if (_editing == null)
        {
            Servers.Add(new ServerEntry
            {
                Name = EditorName.Trim(),
                Ip = EditorIp.Trim(),
                AcceptTextures = EditorAcceptTextures,
                HideAddress = EditorHideAddress
            });
        }
        else
        {
            _editing.Name = EditorName.Trim();
            _editing.Ip = EditorIp.Trim();
            _editing.AcceptTextures = EditorAcceptTextures;
            _editing.HideAddress = EditorHideAddress;
        }

        Save();
        ClearEditor();
    }

    private void Save()
    {
        try
        {
            ServersDatService.Write(_serversDatPath, Servers.ToList());
            StatusText = $"Saved {Servers.Count} server(s) to servers.dat";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to save servers.dat: {ex.Message}";
        }
    }
}