using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Services;
using CustomMcLauncher.ViewModels.Common;

namespace CustomMcLauncher.ViewModels.Dialogs;

public sealed partial class FaultyModItem : ObservableObject
{
    private readonly Action<string>? _onSearchMod;
    private readonly Action? _onStateChanged;

    public string ModName { get; }
    public string? FilePath { get; }
    public bool CanDisable => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);

    [ObservableProperty]
    private bool _isDisabled;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public FaultyModItem(string modName, string? basePath, Action<string>? onSearchMod, Action? onStateChanged = null)
    {
        ModName = modName;
        _onSearchMod = onSearchMod;
        _onStateChanged = onStateChanged;

        if (!string.IsNullOrEmpty(basePath))
        {
            var modsDir = Path.Combine(basePath, "mods");
            if (Directory.Exists(modsDir))
            {
                var candidate = Path.Combine(modsDir, modName);
                if (File.Exists(candidate))
                {
                    FilePath = candidate;
                }
                else
                {
                    var match = Directory.GetFiles(modsDir, "*.jar")
                        .FirstOrDefault(f => Path.GetFileName(f).Contains(modName, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        FilePath = match;
                }
            }
        }
    }

    [RelayCommand]
    private void DisableMod()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            return;

        try
        {
            var disabledPath = FilePath + ".disabled";
            if (File.Exists(disabledPath))
                File.Delete(disabledPath);
            File.Move(FilePath, disabledPath);
            IsDisabled = true;
            StatusText = L10n.T("fl_disabled");
            _onStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    [RelayCommand]
    private void SearchCompatible()
    {
        var cleanName = ModName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            ? ModName[..^4]
            : ModName;
        _onSearchMod?.Invoke(cleanName);
    }
}

/// <summary>Presentation wrapper over the raw analysis record.</summary>
public sealed partial class CrashReportViewModel : ViewModelBase
{
    public CrashAnalysis Analysis { get; }
    public string Title { get; }
    public string LikelyCauseLabel { get; }
    public string HumanSummary { get; }
    public string ExitCodeLine { get; }
    public IReadOnlyList<string> Evidence => Analysis.Evidence;
    public IReadOnlyList<string> Requirements => Analysis.Requirements ?? Array.Empty<string>();
    public IReadOnlyList<string> SolutionSteps => Analysis.SolutionSteps ?? Array.Empty<string>();
    public bool HasSolutionSteps => SolutionSteps.Count > 0;

    public ObservableCollection<FaultyModItem> FaultyMods { get; } = new();
    public bool HasFaultyMods => FaultyMods.Count > 0;

    public CrashReportViewModel(CrashAnalysis analysis, Action<string>? onSearchMod = null)
    {
        Analysis = analysis;
        Title = L10n.T("crash_window_title");
        LikelyCauseLabel = analysis.LikelyCause;
        HumanSummary = analysis.HumanSummary;
        ExitCodeLine = string.Format(L10n.T("crash_exit_code"), analysis.ExitCode);

        if (analysis.FaultyMods != null && analysis.FaultyMods.Count > 0)
        {
            foreach (var mod in analysis.FaultyMods)
            {
                FaultyMods.Add(new FaultyModItem(mod, analysis.InstanceBasePath, onSearchMod, () =>
                {
                    OnPropertyChanged(nameof(FaultyMods));
                }));
            }
        }
    }
}
