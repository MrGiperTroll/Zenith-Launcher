using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Services;
using ZenithLauncher.ViewModels.Common;

namespace ZenithLauncher.ViewModels;

public partial class ServerManagerViewModel : ViewModelBase, IDisposable
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
    private async Task CopyIpAsync(ServerEntry? entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Ip)) return;
        try
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;
            if (topLevel?.Clipboard != null)
            {
                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.CreateText(entry.Ip));
                await topLevel.Clipboard.SetDataAsync(transfer);
                StatusText = string.Format(L10n.T("sm_copied_ip"), entry.Ip);
            }
        }
        catch { }
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
            Title = L10n.T("sm_select_icon"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(L10n.T("ft_png_image")) { Patterns = new[] { "*.png" } } }
        });
        if (files.Count == 0) return;

        try
        {
            var bytes = await File.ReadAllBytesAsync(files[0].Path.LocalPath);
            if (bytes.Length > 64 * 1024)
            {
                StatusText = L10n.T("sm_icon_too_large");
                return;
            }
            if (_editing == null) _editing = new ServerEntry();
            _editing.IconBytes = bytes;
            StatusText = L10n.T("sm_icon_selected");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(L10n.T("sm_icon_load_failed"), ex.Message);
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
            StatusText = L10n.T("sm_name_ip_required");
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
            StatusText = string.Format(L10n.T("sm_saved_count"), Servers.Count);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(L10n.T("sm_save_failed"), ex.Message);
        }
    }

    public void Dispose()
    {
        ClearEditor();
        foreach (var s in Servers)
            s.IconBytes = Array.Empty<byte>();
        Servers.Clear();
    }
}