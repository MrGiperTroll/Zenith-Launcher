using System;
using System.Collections.Generic;
using CustomMcLauncher.Services;
using CustomMcLauncher.ViewModels.Common;

namespace CustomMcLauncher.ViewModels.Dialogs;

/// <summary>Presentation wrapper over the raw analysis record.</summary>
public sealed class CrashReportViewModel : ViewModelBase
{
    public CrashAnalysis Analysis { get; }
    public string Title { get; }
    public string LikelyCauseLabel { get; }
    public string HumanSummary { get; }
    public string ExitCodeLine { get; }
    public IReadOnlyList<string> Evidence => Analysis.Evidence;
    public IReadOnlyList<string> Requirements => Analysis.Requirements ?? Array.Empty<string>();

    public CrashReportViewModel(CrashAnalysis analysis)
    {
        Analysis = analysis;
        Title = L10n.T("crash_window_title");
        LikelyCauseLabel = analysis.LikelyCause;
        HumanSummary = analysis.HumanSummary;
        ExitCodeLine = string.Format(L10n.T("crash_exit_code"), analysis.ExitCode);
    }
}
