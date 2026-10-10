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
            if (vm == null || !File.Exists(vm.Analysis.LogPath)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{vm.Analysis.LogPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch { }
    }
}
