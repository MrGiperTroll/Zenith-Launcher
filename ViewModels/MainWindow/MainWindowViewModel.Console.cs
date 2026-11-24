using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CmlLib.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Models;
using ZenithLauncher.Services;
using ZenithLauncher.UI.Theming;
using ZenithLauncher.ViewModels.Common;
using ZenithLauncher.ViewModels.Pages;
using ZenithLauncher.Views;

namespace ZenithLauncher.ViewModels;


public partial class MainWindowViewModel
{
    [RelayCommand] private void OpenConsoleDialog() => IsConsoleDialogOpen = true;

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _consolePending = new();



    /// <summary>Flushes pending console lines to the UI on a tick, instead of posting every single

    /// game log line directly to the UI thread (which floods/saturates it for chatty versions).</summary>

    private readonly DispatcherTimer _consoleFlushTimer;



    [ObservableProperty]

    private string _currentDownloadFile = string.Empty;



    [ObservableProperty]

    private bool _isConsoleDialogOpen;



    // Delete confirmation dialog



    private void FlushConsolePending()

    {

        if (_consolePending.IsEmpty) return;

        var sb = new System.Text.StringBuilder();

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



    [RelayCommand]

    private async Task CopyConsoleLogsAsync()

    {

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })

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



    [RelayCommand]

    private void CloseConsoleDialog()
    {
        IsConsoleDialogOpen = false;
        if (!Instances.Any(i => i.IsRunning || i.IsLaunching || _launchingInstanceIds.Contains(i.Id)))
        {
            ConsoleLogs = string.Empty;
            while (_consolePending.TryDequeue(out _)) { }
            Services.MemoryTrimmer.TrimMemory();
        }
    }


}
