using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZenithLauncher.Views;

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
    [ObservableProperty] private string _currentVersionText = "";
    [ObservableProperty] private string _newVersionText = "";
    [ObservableProperty] private string _releaseNotesShort = "";
    [ObservableProperty] private double _downloadPercent;
    [ObservableProperty] private string _downloadLine = "";
    [ObservableProperty] private bool _showProgress;

    public UpdateNotificationViewModel(UpdateCheckResult update, Action? closeWindow = null)
    {
        _update = update;
        _closeWindow = closeWindow;
        TitleText = $"{L10n.T("update_title")} {update.Version}";
        UpdateButtonText = L10n.T("update_download");
        DismissText = L10n.T("update_later");
        var current = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        CurrentVersionText = "v" + current.TrimStart('v', 'V');
        NewVersionText = "v" + update.Version.Trim().TrimStart('v', 'V');
        ReleaseNotesShort = ShortenNotes(SelectLocalizedNotes(update.ReleaseNotes, L10n.CurrentLanguage));
    }

    /// <summary>
    /// Picks the release-notes section matching the current UI language.
    /// Release body convention (written once on the GitHub release page):
    /// <code>
    /// ## en
    /// English notes…
    /// ## ru
    /// Русские заметки…
    /// </code>
    /// Falls back to English, then to plain (header-less) text, then to anything.
    /// </summary>
    public static string SelectLocalizedNotes(string body, string langCode)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var sections = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(System.StringComparer.OrdinalIgnoreCase);
        string? current = null;
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var t = line.Trim();
            if (t.StartsWith("#"))
            {
                var code = t.TrimStart('#').Trim().Split(new[] { ' ', '\t' }, 2)[0].ToLowerInvariant();
                if (code is "en" or "ru" or "uk" or "tr" or "de" or "fr")
                {
                    current = code;
                    if (!sections.ContainsKey(code)) sections[code] = new System.Collections.Generic.List<string>();
                    continue;
                }
                if (current == null) continue;
            }
            var bucket = current ?? "";
            if (!sections.TryGetValue(bucket, out var list)) { list = new System.Collections.Generic.List<string>(); sections[bucket] = list; }
            list.Add(line);
        }
        static string Text(System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> s, string code)
            => s.TryGetValue(code, out var l) ? string.Join("\n", l).Trim() : "";
        var plain = Text(sections, "");
        var hit = Text(sections, langCode);
        string source = "lang";
        if (string.IsNullOrWhiteSpace(hit)) { hit = Text(sections, "en"); source = "en"; }
        if (string.IsNullOrWhiteSpace(hit)) { hit = plain; source = "plain"; }
        if (string.IsNullOrWhiteSpace(hit))
        {
            foreach (var kv in sections)
            {
                var any = string.Join("\n", kv.Value).Trim();
                if (!string.IsNullOrWhiteSpace(any)) { hit = any; break; }
            }
            source = "any";
        }
        // Lines before the first header (e.g. a Full Changelog link) are shown always.
        if (source != "plain" && !string.IsNullOrWhiteSpace(plain))
            hit = (hit + "\n\n" + plain).Trim();
        return hit;
    }

    private static string ShortenNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";
        var lines = notes.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Join(Environment.NewLine, lines).Trim();
        return joined.Length > 600 ? joined.Substring(0, 600).TrimEnd() + "…" : joined;
    }

    private void ReportProgress(DownloadProgress p)
    {
        DownloadPercent = Math.Clamp(p.Percent, 0, 100);
        var dlMb = p.DownloadedBytes / 1048576.0;
        DownloadLine = p.TotalBytes > 0
            ? $"{dlMb:F1} / {p.TotalBytes / 1048576.0:F1} MB • {DownloadPercent:0}%"
            : $"{dlMb:F1} MB • {DownloadPercent:0}%";
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (IsDownloading) return;

        // Visual simulation for explicit --test-update / --mock-update runs only
        var cmdArgs = Environment.GetCommandLineArgs();
        bool isMockUpdateRun = cmdArgs.Any(a =>
            a.Equals("--test-update", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--mock-update", StringComparison.OrdinalIgnoreCase));
        if (isMockUpdateRun && _update.DownloadUrl.Contains("tag/v1.2.0", StringComparison.OrdinalIgnoreCase))
        {
            IsDownloading = true;
            ShowProgress = true;
            DownloadPercent = 0;
            StatusText = L10n.T("update_downloading");
            UpdateButtonText = "...";

            for (int i = 0; i <= 100; i += 5)
            {
                if (_cts.IsCancellationRequested) break;
                await Task.Delay(50);
                ReportProgress(new DownloadProgress(i, (long)(i * 450000), 45000000));
            }

            StatusText = L10n.T("update_installing");
            await Task.Delay(1200);
            IsDownloading = false;
            ShowProgress = false;
            StatusText = L10n.T("update_uptodate");
            UpdateButtonText = L10n.T("update_download");
            return;
        }

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
        ShowProgress = true;
        DownloadPercent = 0;
        DownloadLine = "";
        StatusText = L10n.T("update_downloading");
        UpdateButtonText = "...";

        try
        {
            var service = new GitHubUpdateService();
            var progress = new Progress<DownloadProgress>(ReportProgress);
            var success = await service.DownloadAndApplyAsync(_update, _cts.Token, progress);

            if (success)
            {
                StatusText = L10n.T("update_installing");

                await Task.Delay(350);
                (Avalonia.Application.Current as App)?.PrepareForRestart();
                Program.ReleaseSingleInstanceMutexForRestart();

                if (Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
                {
                    (life.MainWindow as MainWindow)?.ForceClose();
                    life.Shutdown();
                }
                else
                {
                    Environment.Exit(0);
                }
            }
            else
            {
                IsDownloading = false;
                ShowProgress = false;
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
