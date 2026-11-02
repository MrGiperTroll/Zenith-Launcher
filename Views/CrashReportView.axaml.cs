using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CustomMcLauncher.Services;
using CustomMcLauncher.ViewModels.Dialogs;

namespace CustomMcLauncher.Views;

public partial class CrashReportView : UserControl
{
    public CrashReportView()
    {
        InitializeComponent();
    }

    private CrashReportViewModel? Vm => DataContext as CrashReportViewModel;

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{vm.Title} (exit {vm.Analysis.ExitCode})");
            sb.AppendLine(vm.HumanSummary);
            sb.AppendLine();
            foreach (var line in vm.Evidence) sb.AppendLine(line);

            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(sb.ToString()));
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetDataAsync(transfer);
        }
        catch { }
    }

    private void OnOpenLogClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null) return;
            var path = vm.Analysis.LogPath;
            if (!File.Exists(path) && !string.IsNullOrWhiteSpace(vm.Analysis.InstanceBasePath))
            {
                var crashDir = Path.Combine(vm.Analysis.InstanceBasePath, "crash-reports");
                if (Directory.Exists(crashDir))
                {
                    var newest = Directory.GetFiles(crashDir, "*.txt")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();
                    if (newest != null) path = newest.FullName;
                }
                if (!File.Exists(path))
                {
                    var latestLog = Path.Combine(vm.Analysis.InstanceBasePath, "logs", "latest.log");
                    if (File.Exists(latestLog)) path = latestLog;
                }
            }
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true,
                });
            }
        }
        catch { }
    }
}
