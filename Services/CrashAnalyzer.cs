using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomMcLauncher.Services;

/// <summary>Result of a crash analysis pass (localized for display).</summary>
public sealed record CrashAnalysis(
    string Title,
    string HumanSummary,
    string LikelyCause,
    IReadOnlyList<string> Evidence,
    string LogPath,
    int ExitCode,
    IReadOnlyList<string>? Requirements = null);

public static class CrashAnalyzer
{
    public static CrashAnalysis Analyze(int exitCode, string instanceLogPath, string instanceBasePath)
    {
        return new CrashAnalysis(
            Title: L10n.T("crash_generic_title") ?? "Game Crashed",
            HumanSummary: L10n.T("crash_generic_human"),
            LikelyCause: "Memory limits or Mod conflict",
            Evidence: new List<string> { $"Exit code: {exitCode}" },
            LogPath: instanceLogPath,
            ExitCode: exitCode
        );
    }
}



