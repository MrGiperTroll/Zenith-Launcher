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

    [ObservableProperty]
    private string _consoleLogs = string.Empty;

    [ObservableProperty]
    private bool _isConsoleDialogOpen;

    public Func<bool>? HasActiveProcesses { get; set; }

    public ConsoleViewModel()
    {
        _consoleFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _consoleFlushTimer.Tick += (_, _) => FlushConsolePending();
        _consoleFlushTimer.Start();
    }

    public void AppendLog(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        _consolePending.Enqueue(line);
    }

    public void FlushConsolePending()
    {
        if (_consolePending.IsEmpty) return;

        var sb = new StringBuilder();
        while (_consolePending.TryDequeue(out var line))
            sb.Append(line).Append(Environment.NewLine);

        if (sb.Length > 0)
        {
            const int maxChars = 40_000;
            var newText = ConsoleLogs + sb;
            if (newText.Length > maxChars)
                newText = newText.Substring(newText.Length - maxChars);
            ConsoleLogs = newText;
        }
    }

    public void ClearLogs()
    {
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
            MemoryTrimmer.TrimMemory();
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
