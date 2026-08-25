using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Services;
using System;
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
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _releaseNotes = "";
    [ObservableProperty] private string _updateButtonText = "";
    [ObservableProperty] private string _dismissText = "";
    [ObservableProperty] private bool _isDownloading;

    public UpdateNotificationViewModel(UpdateCheckResult update, Action? closeWindow = null)
    {
        _update = update;
        _closeWindow = closeWindow;
        TitleText = $"{L10n.T("update_title")} {update.Version}";
        ReleaseNotes = string.IsNullOrWhiteSpace(update.ReleaseNotes)
            ? ""
            : update.ReleaseNotes;
        UpdateButtonText = L10n.T("update_download");
        DismissText = L10n.T("update_later");
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        IsDownloading = true;
        StatusText = L10n.T("update_downloading");
        UpdateButtonText = "...";

        var service = new GitHubUpdateService();
        var success = await service.DownloadAndApplyAsync(_update, _cts.Token);

        if (success)
        {
            StatusText = L10n.T("update_installing");

            await Task.Delay(800);
            if (App.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
                life.Shutdown();
        }
        else
        {
            IsDownloading = false;
            StatusText = L10n.T("update_failed");
            UpdateButtonText = L10n.T("update_download");
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        _closeWindow?.Invoke();
    }
}
