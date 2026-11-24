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
    [ObservableProperty] private string _customJavaPath = string.Empty;



    [ObservableProperty]

    private string _currentJavaDisplay = "";



    /// <summary>Concrete javaw.exe path for the current Java mode - always shown in Settings.</summary>

    [ObservableProperty]

    private string _resolvedJavaPath = "";



    /// <summary>True while "Custom" is selected (only then the path box is editable).</summary>

    [ObservableProperty]

    private bool _isCustomJavaMode;



    [ObservableProperty]

    private string _jvmArgs = "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC";



    [ObservableProperty]

    private int _minRamMb = 512;



    partial void OnMinRamMbChanged(int value)

    {

        if (value > RamMb)

            RamMb = value;

        SaveLauncherConfig();

    }



    [ObservableProperty]

    private int _ramMb = 4096;



    partial void OnRamMbChanged(int value)

    {

        if (value < MinRamMb)

            MinRamMb = value;

        SaveLauncherConfig();

    }



    [RelayCommand]

    private void StepMinRamDown()

    {

        MinRamMb = Math.Clamp(MinRamMb - 512, 512, MaxRamMb);

    }



    [RelayCommand]

    private void StepMinRamUp()

    {

        MinRamMb = Math.Clamp(MinRamMb + 512, 512, MaxRamMb);

    }



    [RelayCommand]

    private void StepRamDown()

    {

        RamMb = Math.Clamp(RamMb - 512, 512, MaxRamMb);

    }



    [RelayCommand]

    private void StepRamUp()

    {

        RamMb = Math.Clamp(RamMb + 512, 512, MaxRamMb);

    }



    [ObservableProperty]

    private string _selectedLaunchBehavior = "KeepOpen";



    public ObservableCollection<string> LaunchBehaviors { get; } = new() { "KeepOpen", "Hide", "Close" };



    partial void OnSelectedLaunchBehaviorChanged(string value) => SaveLauncherConfig();



    [ObservableProperty]

    private int _gameWidth = 1280;



    [ObservableProperty]

    private int _gameHeight = 720;



    [ObservableProperty]

    private bool _isFullscreen = false;



    [ObservableProperty]

    private bool _isElyByErrorOpen = false;



    [ObservableProperty]

    private string _elyByErrorMessage = "A connection issue occurred while preparing the game. Please verify your internet connection.\n\nRecommendation: Try switching to an Offline account to launch and play without an internet connection.";



    // Accounts

    [ObservableProperty] private string _javaDownloadStatus = string.Empty;



    public bool CanDownloadJava => IsJavaDownloading == 0;



    partial void OnIsJavaDownloadingChanged(int value) => OnPropertyChanged(nameof(CanDownloadJava));



    [RelayCommand]
    private async Task DownloadJavaAsync(object? parameter) => await DownloadJavaCoreAsync(parameter, force: false, progress: null);

    private async Task<bool> DownloadJavaCoreAsync(object? parameter, bool force = false, IProgress<double>? progress = null)
    {
        if (!int.TryParse(parameter?.ToString(), out var major)) return false;
        if (IsJavaDownloading != 0) return false;

        IsJavaDownloading = major;
        JavaDownloadStatus = string.Format(L10n.T("java_status_installing"), major);
        StatusText = JavaDownloadStatus;

        try
        {
            var ok = await Task.Run(() => _launchService.DownloadJavaRuntimeAsync(major, ZenithPaths.AppDataDir, forceRedownload: force, progress: progress));

            JavaDownloadStatus = ok
                ? string.Format(L10n.T("java_status_installed"), major)
                : string.Format(L10n.T("java_status_failed"), major);
            StatusText = JavaDownloadStatus;

            if (ok)
            {
                var detectedPath = JavaVersionHelper.FindJavaForVersion(major);
                if (string.IsNullOrWhiteSpace(detectedPath))
                {
                    detectedPath = await _launchService.ResolveJavaForDisplayAsync(major == 8 ? "1.12.2" : major == 17 ? "1.18.2" : major == 21 ? "1.20.4" : "1.21.4");
                }

                if (!string.IsNullOrWhiteSpace(detectedPath))
                {
                    LauncherSettingsVm?.SetJavaLocation(major, detectedPath);
                    if (SelectedJavaMode == "Custom" || string.IsNullOrWhiteSpace(CustomJavaPath))
                    {
                        CustomJavaPath = detectedPath;
                    }
                }

                SelectedJavaMode = "Recommended";
                SaveLauncherConfig();
                LauncherSettingsVm?.UpdateJavaDisplay();
            }
            return ok;
        }
        catch (Exception ex)
        {
            JavaDownloadStatus = $"Java {major} install failed: {ex.Message}";
            StatusText = JavaDownloadStatus;
            return false;
        }
        finally
        {
            IsJavaDownloading = 0;
            UpdateJavaDisplay();
        }
    }



    partial void OnGameWidthChanged(int value)

    {

        if (value < 800) { GameWidth = 800; return; }

        if (value > 7680) { GameWidth = 7680; return; }

        if (IsLockAspectRatio && value > 0)

            GameHeight = value * 9 / 16;

    }



    partial void OnGameHeightChanged(int value)

    {

        if (value < 600) { GameHeight = 600; return; }

        if (value > 4320) { GameHeight = 4320; return; }

        if (IsLockAspectRatio && value > 0)

            GameWidth = value * 16 / 9;

    }



    // Launch & Logs

    private readonly HashSet<string> _launchingInstanceIds = new();



    public bool CanLaunch => SelectedInstance == null || (!SelectedInstance.IsRunning && !_launchingInstanceIds.Contains(SelectedInstance.Id));



    [ObservableProperty]

    private bool _isLaunching;



    partial void OnIsLaunchingChanged(bool value)

    {

        OnPropertyChanged(nameof(LaunchButtonText));

        OnPropertyChanged(nameof(CanLaunchOrCancel));

    }



    public bool CanLaunchOrCancel => CanLaunch || IsLaunching;



    public string LaunchButtonText

    {

        get

        {

            if (IsLaunching) return L10n.T("cancel");

            if (SelectedInstance?.IsRunning == true) return L10n.T("status_running");

            return L10n.T("btn_launch_game");

        }

    }



    [ObservableProperty]

    private double _launchProgress;



    [ObservableProperty]

    private string _consoleLogs = string.Empty;



    /// <summary>Background queue of console lines waiting to be flushed to the UI in batches.</summary>

    public bool IsDownloading => IsLaunching && LaunchProgress < 100;



    [ObservableProperty]

    private bool _isJavaInstallerOpen;



    


    



    [RelayCommand]

    public async Task SwitchToOfflineAccountAsync()

    {

        IsElyByErrorOpen = false;

        try

        {

            var targetName = !string.IsNullOrWhiteSpace(SelectedAccount?.Username)

                ? SelectedAccount.Username

                : (!string.IsNullOrWhiteSpace(ActiveAccountName) ? ActiveAccountName : "Steve");



            var existingAccounts = _accountService.GetAccounts();

            var offlineAcc = existingAccounts.FirstOrDefault(a => a.Type == Models.AccountType.Offline && string.Equals(a.Username, targetName, StringComparison.OrdinalIgnoreCase));



            if (offlineAcc == null)

            {

                offlineAcc = await _accountService.AddOfflineAccountAsync(targetName);

            }



            _accountService.SetActiveAccount(offlineAcc.Id);

            RefreshState();

        }

        catch (Exception)

        {

            StatusText = L10n.T("mw_status_switch_failed");

        }

    }



    [ObservableProperty]

    private bool _isEffectiveJavaPathInvalid = false;



    partial void OnEffectiveJavaPathChanged(string value)

    {

        IsEffectiveJavaPathInvalid = !string.IsNullOrWhiteSpace(value) && value != "java" && !File.Exists(value);

        if (!string.Equals(value, ResolvedJavaPath, StringComparison.Ordinal) && !string.Equals(value, CustomJavaPath, StringComparison.Ordinal))

        {

            CustomJavaPath = value;

            ResolvedJavaPath = value;

            if (SelectedJavaMode != "Custom")

                SelectedJavaMode = "Custom";

            SaveLauncherConfig();

        }

    }



    partial void OnSelectedJavaModeChanged(string value)

    {

        UpdateJavaDisplay();

    }



    partial void OnCustomJavaPathChanged(string value)

    {

        UpdateJavaDisplay();

    }



    private void UpdateJavaDisplay()

    {

        IsCustomJavaMode = SelectedJavaMode == "Custom";



        switch (SelectedJavaMode)

        {

            case "Custom":

                CurrentJavaDisplay = Services.JavaVersionHelper.CustomDisplay(CustomJavaPath);

                ResolvedJavaPath = CustomJavaPath;

                EffectiveJavaPath = CustomJavaPath;

                break;



            case "System":

                CurrentJavaDisplay = L10n.T("java_display_system_path");

                ResolvedJavaPath = Services.JavaVersionHelper.FindSystemJavaPath() ?? "";

                EffectiveJavaPath = ResolvedJavaPath;

                break;



            default: // Recommended

                var inst = SelectedInstance ?? _instanceService.GetSelectedInstance();

                CurrentJavaDisplay = inst != null

                    ? Services.JavaVersionHelper.RecommendedDisplay(inst.Version)

                    : L10n.T("java_display_auto");

                ResolvedJavaPath = "";

                EffectiveJavaPath = "";

                _ = ResolveRecommendedJavaPathAsync(inst?.Version);

                break;

        }

    }



    /// <summary>

    /// Resolves the exact javaw.exe Recommended mode would launch for the

    /// selected profile (no downloads) so the Settings dialog can always show

    /// a real path. Runs off the UI thread; a result that arrives after the

    /// user switched the mode is discarded.

    /// </summary>

    private async Task ResolveRecommendedJavaPathAsync(string? mcVersion)

    {

        try

        {

            var path = await _launchService.ResolveJavaForDisplayAsync(mcVersion);

            if (SelectedJavaMode != "Recommended") return; // user switched meanwhile

            ResolvedJavaPath = path ?? "";

            EffectiveJavaPath = ResolvedJavaPath;

        }

        catch (Exception ex)

        {

            LauncherLog.Error("Failed to resolve the recommended Java path for display.", ex);

        }

    }



    /// <summary>Passthrough for the per-instance editor dialog.</summary>

    internal Task<string?> ResolveJavaForEditorAsync(string? instanceVersion) =>

        _launchService.ResolveJavaForDisplayAsync(instanceVersion);



    [RelayCommand]

    private async Task PickCustomJavaAsync()

    {

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop

            ? desktop.MainWindow

            : null;



        if (topLevel?.StorageProvider != null)

        {

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions

            {

                Title = "Select Java Executable (javaw.exe / java)",

                AllowMultiple = false,

                FileTypeFilter = new[]

                {

                    new FilePickerFileType("Java Executable") { Patterns = new[] { "javaw.exe", "java.exe", "java" } },

                    FilePickerFileTypes.All

                }

            });



            if (files.Count > 0)

            {

                var chosen = files[0].Path.LocalPath;

                CustomJavaPath = chosen;

                ResolvedJavaPath = chosen;

                EffectiveJavaPath = chosen;

                SelectedJavaMode = "Custom";

                SaveLauncherConfig(); // picked file is active + persisted at once

            }

        }

    }



    [RelayCommand]

    private Task BrowseJavaPathAsync() => PickCustomJavaAsync();



    public async Task<bool> CheckCommandLineLaunchAsync(string[]? args)

    {

        if (args == null || args.Length < 2 || !args[0].Equals("--launch", StringComparison.OrdinalIgnoreCase))

            return false;



        var targetId = args[1].Trim('\"');

        var match = Instances.FirstOrDefault(i => i.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))

                 ?? Instances.FirstOrDefault(i => i.Name.Equals(targetId, StringComparison.OrdinalIgnoreCase));

        if (match == null)

        {

            StatusText = string.Format(L10n.T("mw_status_profile_missing"), targetId);

            return false;

        }



        SelectInstance(match);

        await LaunchGameAsync();

        return match.IsRunning;

    }



    public bool HasProfile(string idOrName) =>

        Instances.Any(i => i.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase))

        || Instances.Any(i => i.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));



    [RelayCommand]

    private async Task LaunchSpecificInstanceAsync(InstanceModel instance)

    {

        await LaunchSpecificInstanceWithArgsAsync(instance, null);

    }



    public async Task LaunchSpecificInstanceWithArgsAsync(InstanceModel? instance, string? extraArgs)

    {

        var inst = instance ?? SelectedInstance;

        if (inst == null) return;

        SelectInstance(inst);

        await LaunchGameWithArgsAsync(extraArgs);

    }



    [RelayCommand]

    private void KillInstance(InstanceModel instance)

    {

        var inst = instance ?? SelectedInstance;

        if (inst == null) return;

        _launchingInstanceIds.Remove(inst.Id);

        inst.IsLaunching = false;

        _launchService.KillProcess(inst.Id);

        inst.IsRunning = false;

        OnPropertyChanged(nameof(CanLaunch));

        if (ReferenceEquals(SelectedInstance, inst))

            IsLaunching = false;

    }



    [RelayCommand]

    private void OpenJavaInstaller()

    {

        IsJavaInstallerOpen = true;

        Services.DiscordPresenceService.SetChoosingVersion();

    }



    [RelayCommand]

    private void CloseJavaInstaller()

    {

        IsJavaInstallerOpen = false;

        Services.DiscordPresenceService.SetBrowsingProfiles();

        UpdateJavaDisplay();

    }



    [RelayCommand]

    private async Task LaunchGameAsync()

    {

        await LaunchGameWithArgsAsync(null);

    }



    public async Task LaunchGameWithArgsAsync(string? extraArgs)

    {

        var instance = SelectedInstance ?? _instanceService.GetSelectedInstance();



        if (instance == null) return;

        if (instance.IsRunning || _launchingInstanceIds.Contains(instance.Id)) return;



        if (instance.MinRamMb == null || instance.MinRamMb <= 0)

            instance.MinRamMb = MinRamMb;

        instance.MaxMemoryMb = instance.RamMb ?? RamMb;



        // Mark launching BEFORE any awaits so Cancel is clickable from second one.

        _launchingInstanceIds.Add(instance.Id);

        instance.IsLaunching = true;

        if (ReferenceEquals(SelectedInstance, instance))

            IsLaunching = true;

        OnPropertyChanged(nameof(CanLaunch));

        OnPropertyChanged(nameof(CanLaunchOrCancel));

        OnPropertyChanged(nameof(LaunchButtonText));

        LaunchProgress = 0;

        Services.DiscordPresenceService.SetLaunchingGame(instance.Name);



        try

        {

            var account = _accountService.GetActiveAccount() ?? await _accountService.AddOfflineAccountAsync(ActiveAccountName);

            if (SelectedJavaMode == "Custom" && (string.IsNullOrWhiteSpace(CustomJavaPath) || !File.Exists(CustomJavaPath)))

            {

                StatusText = L10n.T("mw_status_java_invalid");

                return;

            }



            var process = await Task.Run(() => _launchService.LaunchAsync(instance, account, extraArgs));

            StatusText = string.Format(L10n.T("mw_status_launched"), instance.Name);

            ApplyLaunchBehavior(process);

        }

        catch (HttpRequestException ex)

        {

            if (SelectedAccount?.Type == Models.AccountType.ElyBy)

            {

                IsElyByErrorOpen = true;

                StatusText = L10n.T("mw_status_conn_error");

                return;

            }

            StatusText = string.Format(L10n.T("mw_status_http_error"), ex.Message);

        }

        catch (InvalidOperationException ex)

        {

            ConsoleLogs += $"[ERROR] {ex.Message}\n";

            ConsoleLogs += $"[ERROR] Stack: {ex}\n";

            StatusText = ex.Message;

        }

        catch (Exception ex)

        {

            LauncherLog.Error("Launch failed", ex);

            ConsoleLogs += $"[CRITICAL ERROR] {ex}\n";

            StatusText = string.Format(L10n.T("mw_status_launch_failed"), ex.Message);

        }

        finally

        {

            _launchingInstanceIds.Remove(instance.Id);

            instance.IsLaunching = false;

            if (ReferenceEquals(SelectedInstance, instance))

                IsLaunching = false;

            OnPropertyChanged(nameof(CanLaunch));

            // Launch aborted before the process started - return to browsing.

            if (!instance.IsRunning)

                Services.DiscordPresenceService.SetBrowsingProfiles();

        }

    }



    private static void AddDirectoryToZip(ZipArchive archive, string sourceDir, string entryPrefix)

    {

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))

        {

            var relativePath = Path.GetRelativePath(sourceDir, file);

            archive.CreateEntryFromFile(file, Path.Combine(entryPrefix, relativePath));

        }

    }



    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]

    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]

    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);



    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]

    private struct MEMORYSTATUSEX

    {

        public uint dwLength;

        public uint dwMemoryLoad;

        public ulong ullTotalPhys;

        public ulong ullAvailPhys;

        public ulong ullTotalPageFile;

        public ulong ullAvailPageFile;

        public ulong ullTotalVirtual;

        public ulong ullAvailVirtual;

        public ulong ullAvailExtendedVirtual;

    }



    [RelayCommand]

    private async Task LaunchOrCancelAsync()

    {

        if (IsLaunching)

        {

            var instance = SelectedInstance ?? _instanceService.GetSelectedInstance();

            if (instance != null)

            {

                _launchingInstanceIds.Remove(instance.Id);

                instance.IsLaunching = false;

                _launchService.KillProcess(instance.Id);

                instance.IsRunning = false;

            }

            IsLaunching = false;

            StatusText = L10n.T("mw_status_launch_cancelled");

        }

        else

        {

            await LaunchGameAsync();

        }

    }



    private void ApplyLaunchBehavior(Process? process)

    {

        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null)

            return;



        var behavior = SelectedLaunchBehavior;

        if (string.Equals(behavior, "Hide", StringComparison.OrdinalIgnoreCase))

        {

            Dispatcher.UIThread.Post(() =>

            {

                if (desktop.MainWindow != null)

                {

                    desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;

                }

            });



            if (process != null)

            {

                try

                {

                    process.EnableRaisingEvents = true;

                    process.Exited += (_, _) =>

                    {

                        Dispatcher.UIThread.Post(() =>

                        {

                            if (desktop.MainWindow != null)

                            {

                                desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;

                                desktop.MainWindow.Activate();

                            }

                        });

                    };

                }

                catch (Exception ex)

                {

                    LauncherLog.Error("Failed to hook Exited event for launch behavior restore", ex);

                }

            }

        }

        else if (string.Equals(behavior, "Close", StringComparison.OrdinalIgnoreCase))

        {

            Dispatcher.UIThread.Post(() =>

            {

                desktop.MainWindow?.Close();

            });

        }

    }

}
