using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class CreateServerViewModel : ObservableObject
{
    private readonly ServerCreatorService _serverService = new();
    private readonly MainWindowViewModel _host;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _serverName = "My Local Server";
    [ObservableProperty] private string _selectedVersion = "1.21.4";
    [ObservableProperty] private ServerSoftwareOption _selectedSoftware;
    [ObservableProperty] private int _ramGb = 4;
    [ObservableProperty] private int _serverPort = 25565;
    [ObservableProperty] private bool _agreeEula = true;
    [ObservableProperty] private bool _onlineMode = true;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isCreated;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _createdServerDirectory = "";

    public ObservableCollection<string> AvailableVersions { get; } = new();
    public IReadOnlyList<ServerSoftwareOption> AvailableSoftware => ServerCreatorService.AvailableSoftware;
    public int[] RamOptions { get; } = { 2, 3, 4, 6, 8, 12, 16, 24, 32 };

    public bool CanCreate => !IsBusy && !string.IsNullOrWhiteSpace(ServerName) && !string.IsNullOrWhiteSpace(SelectedVersion);

    public CreateServerViewModel(IInstanceService instanceService, MainWindowViewModel host)
    {
        _host = host;
        _selectedSoftware = AvailableSoftware[0];
        _ = LoadVersionsAsync();
    }

    private async Task LoadVersionsAsync()
    {
        IsBusy = true;
        StatusText = "Loading versions...";
        try
        {
            var versions = await _serverService.GetPopularReleaseVersionsAsync();
            AvailableVersions.Clear();
            foreach (var v in versions)
                AvailableVersions.Add(v);

            if (AvailableVersions.Count > 0)
            {
                SelectedVersion = AvailableVersions[0];
            }
            StatusText = L10n.T("cs_status_ready");
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading versions: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanCreate));
        }
    }

    partial void OnServerNameChanged(string value) => OnPropertyChanged(nameof(CanCreate));
    partial void OnSelectedVersionChanged(string value) => OnPropertyChanged(nameof(CanCreate));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanCreate));

    [RelayCommand]
    public async Task CreateServerAsync()
    {
        if (!CanCreate) return;
        IsBusy = true;
        IsCreated = false;
        ProgressPercent = 0;
        StatusText = L10n.T("cs_creating");
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(string Status, double Percent)>(p =>
            {
                StatusText = p.Status;
                ProgressPercent = p.Percent;
            });

            var targetDir = await _serverService.CreateServerAsync(
                ServerName,
                SelectedVersion,
                SelectedSoftware.Id,
                RamGb,
                ServerPort,
                AgreeEula,
                OnlineMode,
                progress,
                _cts.Token);

            CreatedServerDirectory = targetDir;
            IsCreated = true;
            StatusText = L10n.T("cs_created_success");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to create server", ex);
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    public void OpenServerFolder()
    {
        var dir = string.IsNullOrWhiteSpace(CreatedServerDirectory)
            ? ServerCreatorService.DefaultServersDirectory
            : CreatedServerDirectory;

        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to open server folder", ex);
        }
    }

    [RelayCommand]
    public void RunServer()
    {
        if (string.IsNullOrWhiteSpace(CreatedServerDirectory) || !Directory.Exists(CreatedServerDirectory))
            return;

        try
        {
            var batPath = Path.Combine(CreatedServerDirectory, "run.bat");
            if (File.Exists(batPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k cd /d \"{CreatedServerDirectory}\" && run.bat",
                    WorkingDirectory = CreatedServerDirectory,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to run server", ex);
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        _cts?.Cancel();
        _host.GoBack();
    }
}
