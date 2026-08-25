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

namespace CustomMcLauncher.Views;

public partial class CrashReportWindow : Window
{
    private readonly CrashReportWindowVm _vm;

    public CrashReportWindow(CrashAnalysis analysis)
    {
        InitializeComponent();
        _vm = new CrashReportWindowVm(analysis);
        DataContext = _vm;
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{_vm.Title} (exit {_vm.Analysis.ExitCode})");
            sb.AppendLine(_vm.HumanSummary);
            sb.AppendLine();
            foreach (var line in _vm.Evidence) sb.AppendLine(line);

            // Avalonia 12 clipboard API (SetTextAsync was removed).
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(sb.ToString()));
            if (Clipboard != null)
                await Clipboard.SetDataAsync(transfer);
        }
        catch { }
    }

    private void OnOpenLogClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(_vm.Analysis.LogPath)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_vm.Analysis.LogPath}\"")
            {
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

/// <summary>Presentation wrapper over the raw analysis record.</summary>
public sealed class CrashReportWindowVm
{
    public CrashAnalysis Analysis { get; }
    public string Title { get; }
    public string LikelyCauseLabel { get; }
    public string HumanSummary { get; }
    public string ExitCodeLine { get; }
    public IReadOnlyList<string> Evidence => Analysis.Evidence;
    public IReadOnlyList<string> Requirements => Analysis.Requirements ?? Array.Empty<string>();

    public CrashReportWindowVm(CrashAnalysis analysis)
    {
        Analysis = analysis;
        Title = L10n.T("crash_window_title");
        LikelyCauseLabel = analysis.LikelyCause;
        HumanSummary = analysis.HumanSummary;
        ExitCodeLine = string.Format(L10n.T("crash_exit_code"), analysis.ExitCode);
    }
}
