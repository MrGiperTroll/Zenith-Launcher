using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Services;
using ZenithLauncher.ViewModels.Common;

namespace ZenithLauncher.ViewModels.Components;

public partial class ConsoleViewModel : ViewModelBase
{
    private readonly ConcurrentQueue<string> _consolePending = new();
    private readonly DispatcherTimer _consoleFlushTimer;
    private int _flushScheduled;

    [ObservableProperty]
    private string _consoleLogs = string.Empty;

    [ObservableProperty]
    private bool _isConsoleDialogOpen;

    public Func<bool>? HasActiveProcesses { get; set; }

    public ConsoleViewModel()
    {
        _consoleFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _consoleFlushTimer.Tick += (_, _) => FlushConsolePending();
    }

    public void AppendLog(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        _consolePending.Enqueue(line);
        if (System.Threading.Interlocked.Exchange(ref _flushScheduled, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_consolePending.IsEmpty && !_consoleFlushTimer.IsEnabled)
                    _consoleFlushTimer.Start();
            });
        }
    }

    public void FlushConsolePending()
    {
        if (_consolePending.IsEmpty)
        {
            System.Threading.Interlocked.Exchange(ref _flushScheduled, 0);
            if (_consolePending.IsEmpty)
            {
                _consoleFlushTimer.Stop();
                return;
            }
        }

        var sb = new StringBuilder();
        while (_consolePending.TryDequeue(out var line))
            sb.Append(line).Append(Environment.NewLine);

        if (sb.Length > 0)
        {
            const int maxChars = 40_000;
            var current = ConsoleLogs ?? string.Empty;
            if (sb.Length >= maxChars)
            {
                ConsoleLogs = sb.ToString(sb.Length - maxChars, maxChars);
            }
            else
            {
                int keepFromCurrent = Math.Min(current.Length, maxChars - sb.Length);
                int skipFromCurrent = current.Length - keepFromCurrent;
                ConsoleLogs = string.Concat(current.AsSpan(skipFromCurrent), sb.ToString());
            }
        }

        System.Threading.Interlocked.Exchange(ref _flushScheduled, 0);
        if (_consolePending.IsEmpty)
        {
            _consoleFlushTimer.Stop();
        }
        else if (!_consoleFlushTimer.IsEnabled)
        {
            _consoleFlushTimer.Start();
        }
    }

    public void ClearLogs()
    {
        _consoleFlushTimer.Stop();
        System.Threading.Interlocked.Exchange(ref _flushScheduled, 0);
        ConsoleLogs = string.Empty;
        while (_consolePending.TryDequeue(out _)) { }
    }

    [RelayCommand]
    public void OpenConsoleDialog() => IsConsoleDialogOpen = true;

    [RelayCommand]
    public void CloseConsoleDialog()
    {
        IsConsoleDialogOpen = false;
        if (HasActiveProcesses == null || !HasActiveProcesses())
        {
            ClearLogs();
        }
    }

    [RelayCommand]
    public async Task CopyConsoleLogsAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(window)?.Clipboard;
            if (clipboard != null)
            {
                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.CreateText(ConsoleLogs ?? string.Empty));
                await clipboard.SetDataAsync(transfer);
            }
        }
    }
}
