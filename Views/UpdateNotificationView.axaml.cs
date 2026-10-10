using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Views;

public partial class UpdateNotificationView : UserControl
{
    public UpdateNotificationView()
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
    [ObservableProperty] private string _updateButtonText = "";
    [ObservableProperty] private string _dismissText = "";
    [ObservableProperty] private bool _isDownloading;

    public UpdateNotificationViewModel(UpdateCheckResult update, Action? closeWindow = null)
    {
        _update = update;
        _closeWindow = closeWindow;
        TitleText = $"{L10n.T("update_title")} {update.Version}";
        UpdateButtonText = L10n.T("update_download");
        DismissText = L10n.T("update_later");
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (IsDownloading) return;

        // Non-installer asset (e.g. a web fallback URL): open it instead of downloading junk.
        if (!_update.DownloadUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                StatusText = L10n.T("update_browser");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_update.DownloadUrl) { UseShellExecute = true });
            }
            catch (Exception)
            {
                StatusText = L10n.T("update_failed");
            }
            return;
        }

        IsDownloading = true;
        StatusText = L10n.T("update_downloading");
        UpdateButtonText = "...";

        try
        {
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
        catch (OperationCanceledException)
        {
            IsDownloading = false;
            StatusText = L10n.T("update_failed");
            UpdateButtonText = L10n.T("update_download");
        }
        catch (Exception)
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
