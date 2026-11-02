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

public sealed partial class CrashActionItem : ObservableObject
{
    public string Title { get; }
    public string Description { get; }
    public bool IsPrimary { get; }

    [ObservableProperty]
    private bool _isExecuted;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    private readonly Func<string> _action;

    public CrashActionItem(string title, string description, Func<string> action, bool isPrimary = true)
    {
        Title = title;
        Description = description;
        _action = action;
        IsPrimary = isPrimary;
    }

    [RelayCommand]
    public void Execute()
    {
        if (IsExecuted) return;
        try
        {
            ResultMessage = _action();
            IsExecuted = true;
        }
        catch (Exception ex)
        {
            ResultMessage = ex.Message;
        }
    }
}

public sealed partial class FaultyModItem : ObservableObject
{
    private readonly Action? _onStateChanged;

    public string ModName { get; }
    public string? FilePath { get; }
    public bool CanDisable => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);

    [ObservableProperty]
    private bool _isDisabled;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public FaultyModItem(string modName, string? basePath, Action? onStateChanged = null)
    {
        ModName = modName;
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
    public void DisableMod()
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
            StatusText = "Disabled";
            _onStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }
}

/// <summary>Presentation wrapper over the raw analysis record with automated one-click resolutions.</summary>
public sealed partial class CrashReportViewModel : ViewModelBase
{
    public CrashAnalysis Analysis { get; }
    public string Title { get; }
    public string LikelyCauseLabel { get; }
    public string HumanSummary { get; }
    public string ExitCodeLine { get; }
    public IReadOnlyList<string> Evidence => Analysis.Evidence;

    public ObservableCollection<FaultyModItem> FaultyMods { get; } = new();
    public bool HasFaultyMods => FaultyMods.Count > 0;

    public ObservableCollection<CrashActionItem> QuickActions { get; } = new();
    public bool HasQuickActions => QuickActions.Count > 0;

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
                FaultyMods.Add(new FaultyModItem(mod, analysis.InstanceBasePath, () =>
                {
                    OnPropertyChanged(nameof(FaultyMods));
                }));
            }
        }

        BuildQuickActions();
    }

    private void BuildQuickActions()
    {
        // 1. Faulty mod direct action buttons
        if (FaultyMods.Count == 1)
        {
            var mod = FaultyMods[0];
            if (mod.CanDisable)
            {
                QuickActions.Add(new CrashActionItem(
                    $"Disable {mod.ModName}",
                    "Rename .jar to .disabled to prevent loading",
                    () =>
                    {
                        mod.DisableMod();
                        return $"{mod.ModName} disabled";
                    }));
            }
        }
        else if (FaultyMods.Count > 1)
        {
            // Multiple options: allow user to disable either mod or both
            foreach (var mod in FaultyMods)
            {
                if (mod.CanDisable)
                {
                    var m = mod;
                    QuickActions.Add(new CrashActionItem(
                        $"Disable {m.ModName}",
                        $"Disable only {m.ModName}",
                        () =>
                        {
                            m.DisableMod();
                            return $"{m.ModName} disabled";
                        },
                        isPrimary: false));
                }
            }

            QuickActions.Add(new CrashActionItem(
                "Disable All Conflicting Mods",
                "Disable all conflicting mods at once",
                () =>
                {
                    foreach (var m in FaultyMods)
                    {
                        if (m.CanDisable && !m.IsDisabled)
                            m.DisableMod();
                    }
                    return "All conflicting mods disabled";
                },
                isPrimary: true));
        }

        // 2. Memory (Out of Memory) auto-action
        var summary = (HumanSummary ?? "").ToLowerInvariant();
        var cause = (LikelyCauseLabel ?? "").ToLowerInvariant();
        if (summary.Contains("memory") || summary.Contains("heap") || summary.Contains("oom") || cause.Contains("memory") || Analysis.ExitCode == -1073740791)
        {
            QuickActions.Add(new CrashActionItem(
                "Increase RAM Limit to 4 GB",
                "Automatically boost maximum allocated RAM to 4096 MB",
                () =>
                {
                    try
                    {
                        var configPath = ZenithPaths.ConfigFilePath;
                        if (File.Exists(configPath))
                        {
                            var json = File.ReadAllText(configPath);
                            var cfg = System.Text.Json.JsonSerializer.Deserialize<Models.LauncherConfigData>(json);
                            if (cfg != null)
                            {
                                cfg.RamMb = Math.Max(cfg.RamMb, 4096);
                                File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(cfg, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                                return "RAM limit increased to 4096 MB";
                            }
                        }
                    }
                    catch { }
                    return "RAM updated";
                }));
        }

        // 3. Sodium render buffer overflow auto-action
        if (summary.Contains("sodium") || summary.Contains("mesh time buffer"))
        {
            if (!string.IsNullOrEmpty(Analysis.InstanceBasePath))
            {
                var sodiumConfig = Path.Combine(Analysis.InstanceBasePath, "config", "sodium-options.json");
                if (File.Exists(sodiumConfig))
                {
                    QuickActions.Add(new CrashActionItem(
                        "Reset Sodium Video Settings",
                        "Delete corrupted sodium-options.json to restore default chunk rendering",
                        () =>
                        {
                            try
                            {
                                File.Delete(sodiumConfig);
                                return "sodium-options.json reset";
                            }
                            catch (Exception ex) { return ex.Message; }
                        }));
                }
            }
        }
    }
}
