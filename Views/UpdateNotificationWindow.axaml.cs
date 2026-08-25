using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Services;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Views;

public partial class UpdateNotificationWindow : Window
{
    public UpdateNotificationWindow()
    {
        InitializeComponent();
    }
}

public partial class UpdateNotificationViewModel : ObservableObject
{
    private readonly UpdateCheckResult _update;
    private readonly CancellationTokenSource _cts = new();
    private readonly Action? _closeWindow;

    [ObservableProperty] private string _titleText = "";
    [ObservableProperty] private string _subtitleText = "";
    [ObservableProperty] private string _releaseNotes = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _updateButtonText = "";
    [ObservableProperty] private string _dismissText = "";
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;

    public UpdateNotificationViewModel(UpdateCheckResult update, Action? closeWindow = null)
    {
        _update = update;
        _closeWindow = closeWindow;
        TitleText = $"Update available: {update.Version}";
        SubtitleText = $"A new version of Zenith Launcher is ready to install.";
        ReleaseNotes = string.IsNullOrWhiteSpace(update.ReleaseNotes)
            ? "No release notes provided."
            : update.ReleaseNotes;
        UpdateButtonText = "Download & Install";
        DismissText = "Skip";
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        IsDownloading = true;
        StatusText = "Downloading update...";
        UpdateButtonText = "Downloading...";

        var service = new GitHubUpdateService();
        var success = await service.DownloadAndApplyAsync(_update, _cts.Token);

        if (success)
        {
            StatusText = "Installer launched. The launcher will now close.";
            UpdateButtonText = "Closing...";

            await Task.Delay(1500);
            if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
                life.Shutdown();
        }
        else
        {
            IsDownloading = false;
            StatusText = "Download failed. Please try again or download manually from GitHub.";
            UpdateButtonText = "Retry";
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        _closeWindow?.Invoke();
    }
}
