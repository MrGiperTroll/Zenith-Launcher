using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Models;
using ZenithLauncher.Services;
using ZenithLauncher.ViewModels.Common;

namespace ZenithLauncher.ViewModels.Components;

public partial class LaunchViewModel : ViewModelBase
{
    private readonly ILaunchService _launchService;
    private readonly IAccountService _accountService;
    private readonly IInstanceService _instanceService;
    private readonly ConsoleViewModel _console;
    private readonly HashSet<string> _launchingInstanceIds = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _launchCtsMap = new();

    public Func<InstanceModel?>? GetSelectedInstance { get; set; }
    public Func<AccountModel?>? GetSelectedAccount { get; set; }
    public Func<string>? GetActiveAccountName { get; set; }
    public Func<string>? GetSelectedJavaMode { get; set; }
    public Action<string>? SetSelectedJavaMode { get; set; }
    public Action<bool>? SetElyByErrorOpen { get; set; }
    public Action<string>? OnStatusChanged { get; set; }
    public Action? SaveLauncherConfigRequested { get; set; }
    public Action? FlushConfigRequested { get; set; }
    public Action? RefreshStateRequested { get; set; }
    public Action<int, string>? OnJavaLocationDetected { get; set; }
    public Action? OnLauncherSettingsUpdateJavaDisplayRequested { get; set; }
    public Action<InstanceModel>? SelectInstanceRequested { get; set; }
    public Func<IEnumerable<InstanceModel>>? GetInstances { get; set; }

    [ObservableProperty]
    private bool _isLaunching;

    partial void OnIsLaunchingChanged(bool value)
    {
        OnPropertyChanged(nameof(LaunchButtonText));
        OnPropertyChanged(nameof(CanLaunchOrCancel));
        OnPropertyChanged(nameof(IsDownloading));
    }

    [ObservableProperty]
    private double _launchProgress;

    partial void OnLaunchProgressChanged(double value)
    {
        OnPropertyChanged(nameof(IsDownloading));
    }

    [ObservableProperty]
    private string _statusText = string.Empty;

    partial void OnStatusTextChanged(string value)
    {
        OnStatusChanged?.Invoke(value);
    }

    [ObservableProperty]
    private string _currentDownloadFile = string.Empty;

    public bool IsDownloading => IsLaunching && LaunchProgress < 100;

    public bool CanLaunch
    {
        get
        {
            var inst = GetSelectedInstance?.Invoke();
            return inst == null || (!inst.IsRunning && !_launchingInstanceIds.Contains(inst.Id));
        }
    }

    public bool CanLaunchOrCancel => CanLaunch || IsLaunching;

    public string LaunchButtonText
    {
        get
        {
            if (IsLaunching) return L10n.T("cancel");
            var inst = GetSelectedInstance?.Invoke();
            if (inst?.IsRunning == true) return L10n.T("status_running");
            return L10n.T("btn_launch_game");
        }
    }

    [ObservableProperty] private string _customJavaPath = string.Empty;
    [ObservableProperty] private string _currentJavaDisplay = "";
    [ObservableProperty] private string _resolvedJavaPath = "";
    [ObservableProperty] private bool _isCustomJavaMode;
    [ObservableProperty] private string _jvmArgs = "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC";

    [ObservableProperty] private int _minRamMb = 512;
    partial void OnMinRamMbChanged(int value)
    {
        if (value > RamMb)
            RamMb = value;
        SaveLauncherConfigRequested?.Invoke();
    }

    [ObservableProperty] private int _ramMb = 4096;
    partial void OnRamMbChanged(int value)
    {
        if (value < MinRamMb)
            MinRamMb = value;
        SaveLauncherConfigRequested?.Invoke();
    }

    [ObservableProperty] private int _maxRamMb = 16384;

    [RelayCommand]
    public void StepMinRamDown() => MinRamMb = Math.Clamp(MinRamMb - 512, 512, MaxRamMb);

    [RelayCommand]
    public void StepMinRamUp() => MinRamMb = Math.Clamp(MinRamMb + 512, 512, MaxRamMb);

    [RelayCommand]
    public void StepRamDown() => RamMb = Math.Clamp(RamMb - 512, 512, MaxRamMb);

    [RelayCommand]
    public void StepRamUp() => RamMb = Math.Clamp(RamMb + 512, 512, MaxRamMb);

    [ObservableProperty] private string _selectedLaunchBehavior = "KeepOpen";
    public ObservableCollection<string> LaunchBehaviors { get; } = new() { "KeepOpen", "Hide", "Close" };
    partial void OnSelectedLaunchBehaviorChanged(string value) => SaveLauncherConfigRequested?.Invoke();

    [ObservableProperty] private int _gameWidth = 1280;
    partial void OnGameWidthChanged(int value)
    {
        if (value < 800) { GameWidth = 800; return; }
        if (value > 7680) { GameWidth = 7680; return; }
        if (IsLockAspectRatio && value > 0) GameHeight = value * 9 / 16;
    }

    [ObservableProperty] private int _gameHeight = 720;
    partial void OnGameHeightChanged(int value)
    {
        if (value < 600) { GameHeight = 600; return; }
        if (value > 4320) { GameHeight = 4320; return; }
        if (IsLockAspectRatio && value > 0) GameWidth = value * 16 / 9;
    }

    [ObservableProperty] private bool _isFullscreen = false;
    [ObservableProperty] private bool _isLockAspectRatio = false;

    [ObservableProperty] private bool _isElyByErrorOpen = false;
    [ObservableProperty] private string _elyByErrorMessage = "A connection issue occurred while preparing the game. Please verify your internet connection.\n\nRecommendation: Try switching to an Offline account to launch and play without an internet connection.";

    [ObservableProperty] private string _javaDownloadStatus = string.Empty;
    [ObservableProperty] private int _isJavaDownloading;
    public bool CanDownloadJava => IsJavaDownloading == 0;
    partial void OnIsJavaDownloadingChanged(int value) => OnPropertyChanged(nameof(CanDownloadJava));

    [ObservableProperty] private bool _isJavaInstallerOpen;
    [ObservableProperty] private bool _isEffectiveJavaPathInvalid = false;
    [ObservableProperty] private string _effectiveJavaPath = "";

    partial void OnEffectiveJavaPathChanged(string value)
    {
        IsEffectiveJavaPathInvalid = !string.IsNullOrWhiteSpace(value) && value != "java" && !File.Exists(value);
        if (!string.Equals(value, ResolvedJavaPath, StringComparison.Ordinal) && !string.Equals(value, CustomJavaPath, StringComparison.Ordinal))
        {
            CustomJavaPath = value;
            ResolvedJavaPath = value;
            if (GetSelectedJavaMode?.Invoke() != "Custom")
                SetSelectedJavaMode?.Invoke("Custom");
            SaveLauncherConfigRequested?.Invoke();
        }
    }

    partial void OnCustomJavaPathChanged(string value) => UpdateJavaDisplay();

    public LaunchViewModel(
        ILaunchService launchService,
        IAccountService accountService,
        IInstanceService instanceService,
        ConsoleViewModel console)
    {
        _launchService = launchService;
        _accountService = accountService;
        _instanceService = instanceService;
        _console = console;

        _launchService.ProgressChanged += p => Dispatcher.UIThread.Post(() => LaunchProgress = p);
        _launchService.CurrentFileChanged += f => Dispatcher.UIThread.Post(() => CurrentDownloadFile = f);
        _launchService.LogReceived += log => _console.AppendLog(log);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var memStatus = new MEMORYSTATUSEX();
                memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref memStatus))
                    MaxRamMb = (int)(memStatus.ullTotalPhys / (1024 * 1024));
            }
        }
        catch { MaxRamMb = 16384; }
    }

    public void NotifyInstanceChanged()
    {
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(CanLaunchOrCancel));
        OnPropertyChanged(nameof(LaunchButtonText));
    }

    public bool IsInstanceLaunching(string instanceId) => _launchingInstanceIds.Contains(instanceId);

    public void StopInstance(InstanceModel? inst)
    {
        if (inst == null) return;
        if (_launchCtsMap.TryGetValue(inst.Id, out var cts))
        {
            try { cts.Cancel(); } catch { }
        }
        _launchingInstanceIds.Remove(inst.Id);
        inst.IsLaunching = false;
        _launchService.KillProcess(inst.Id);
        inst.IsRunning = false;
        NotifyInstanceChanged();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task LaunchOrCancelAsync()
    {
        if (IsLaunching)
        {
            var instance = GetSelectedInstance?.Invoke() ?? _instanceService.GetSelectedInstance();
            if (instance != null)
            {
                if (_launchCtsMap.TryGetValue(instance.Id, out var cts))
                {
                    try { cts.Cancel(); } catch { }
                }
                _launchingInstanceIds.Remove(instance.Id);
                instance.IsLaunching = false;
                _launchService.KillProcess(instance.Id);
                instance.IsRunning = false;
            }
            IsLaunching = false;
            StatusText = L10n.T("mw_status_launch_cancelled");
            NotifyInstanceChanged();
        }
        else
        {
            await LaunchGameAsync();
        }
    }

    [RelayCommand]
    public async Task LaunchGameAsync() => await LaunchGameWithArgsAsync(null);

    public async Task LaunchGameWithArgsAsync(string? extraArgs)
    {
        var instance = GetSelectedInstance?.Invoke() ?? _instanceService.GetSelectedInstance();
        if (instance == null) return;
        if (instance.IsRunning || _launchingInstanceIds.Contains(instance.Id)) return;

        FlushConfigRequested?.Invoke();

        if (instance.MinRamMb == null || instance.MinRamMb <= 0)
            instance.MinRamMb = MinRamMb;
        instance.MaxMemoryMb = instance.RamMb ?? RamMb;

        var launchCts = new CancellationTokenSource();
        if (_launchCtsMap.TryGetValue(instance.Id, out var staleCts))
        {
            try { staleCts.Cancel(); staleCts.Dispose(); } catch { }
        }
        _launchCtsMap[instance.Id] = launchCts;
        var ct = launchCts.Token;

        _launchingInstanceIds.Add(instance.Id);
        instance.IsLaunching = true;

        var currentSelected = GetSelectedInstance?.Invoke();
        if (ReferenceEquals(currentSelected, instance))
            IsLaunching = true;

        NotifyInstanceChanged();
        LaunchProgress = 0;
        DiscordPresenceService.SetLaunchingGame(instance.Name);

        try
        {
            var activeName = GetActiveAccountName?.Invoke() ?? "Steve";
            var account = _accountService.GetActiveAccount() ?? await _accountService.AddOfflineAccountAsync(activeName);

            var javaMode = GetSelectedJavaMode?.Invoke() ?? "Recommended";
            var customJava = CustomJavaPath;

            if (javaMode == "Custom" && string.IsNullOrWhiteSpace(instance.CustomJavaPath) &&
                (string.IsNullOrWhiteSpace(customJava) || !File.Exists(customJava)))
            {
                StatusText = L10n.T("mw_status_java_invalid");
                return;
            }

            ct.ThrowIfCancellationRequested();
            var process = await Task.Run(() => _launchService.LaunchAsync(instance, account, extraArgs, ct), ct);
            if (ct.IsCancellationRequested)
            {
                _launchService.KillProcess(instance.Id);
                throw new OperationCanceledException(ct);
            }

            StatusText = string.Format(L10n.T("mw_status_launched"), instance.Name);
            ApplyLaunchBehavior(process);
        }
        catch (OperationCanceledException)
        {
            _launchService.KillProcess(instance.Id);
            StatusText = L10n.T("mw_status_launch_cancelled");
        }
        catch (HttpRequestException ex)
        {
            var acc = GetSelectedAccount?.Invoke();
            if (acc?.Type == Models.AccountType.ElyBy)
            {
                IsElyByErrorOpen = true;
                SetElyByErrorOpen?.Invoke(true);
                StatusText = L10n.T("mw_status_conn_error");
            }
            else
            {
                StatusText = string.Format(L10n.T("mw_status_http_error"), ex.Message);
            }
        }
        catch (InvalidOperationException ex)
        {
            _console.AppendLog($"[ERROR] {ex.Message}");
            _console.AppendLog($"[ERROR] Stack: {ex}");
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Launch failed for {instance.Name}", ex);
            StatusText = string.Format(L10n.T("mw_status_launch_failed"), ex.Message);
            _console.AppendLog($"[CRITICAL ERROR] {ex}");
        }
        finally
        {
            if (_launchCtsMap.TryRemove(instance.Id, out var removedCts))
            {
                try { removedCts.Dispose(); } catch { }
            }
            _launchingInstanceIds.Remove(instance.Id);
            instance.IsLaunching = false;

            var current = GetSelectedInstance?.Invoke();
            if (ReferenceEquals(current, instance))
                IsLaunching = false;

            NotifyInstanceChanged();

            if (!instance.IsRunning)
                DiscordPresenceService.SetBrowsingProfiles();
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
                    desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;
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
                catch { }
            }
        }
        else if (string.Equals(behavior, "Close", StringComparison.OrdinalIgnoreCase))
        {
            Dispatcher.UIThread.Post(() => desktop.MainWindow?.Close());
        }
    }

    [RelayCommand]
    public async Task DownloadJavaAsync(object? parameter) => await DownloadJavaCoreAsync(parameter, force: false, progress: null);

    public async Task<bool> DownloadJavaCoreAsync(object? parameter, bool force = false, IProgress<double>? progress = null)
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
                    OnJavaLocationDetected?.Invoke(major, detectedPath);
                    if (GetSelectedJavaMode?.Invoke() == "Custom" || string.IsNullOrWhiteSpace(CustomJavaPath))
                    {
                        CustomJavaPath = detectedPath;
                    }
                }

                SetSelectedJavaMode?.Invoke("Recommended");
                SaveLauncherConfigRequested?.Invoke();
                OnLauncherSettingsUpdateJavaDisplayRequested?.Invoke();
            }
            return ok;
        }
        catch (Exception ex)
        {
            JavaDownloadStatus = string.Format(L10n.T("java_install_failed_msg"), major, ex.Message);
            StatusText = JavaDownloadStatus;
            return false;
        }
        finally
        {
            IsJavaDownloading = 0;
            UpdateJavaDisplay();
        }
    }

    public void UpdateJavaDisplay()
    {
        var mode = GetSelectedJavaMode?.Invoke() ?? "Recommended";
        IsCustomJavaMode = mode == "Custom";

        switch (mode)
        {
            case "Custom":
                CurrentJavaDisplay = JavaVersionHelper.CustomDisplay(CustomJavaPath);
                ResolvedJavaPath = CustomJavaPath;
                EffectiveJavaPath = CustomJavaPath;
                break;

            case "System":
                CurrentJavaDisplay = L10n.T("java_display_system_path");
                ResolvedJavaPath = JavaVersionHelper.FindSystemJavaPath() ?? "";
                EffectiveJavaPath = ResolvedJavaPath;
                break;

            default: // Recommended
                var inst = GetSelectedInstance?.Invoke() ?? _instanceService.GetSelectedInstance();
                CurrentJavaDisplay = inst != null
                    ? JavaVersionHelper.RecommendedDisplay(inst.Version)
                    : L10n.T("java_display_auto");
                ResolvedJavaPath = "";
                EffectiveJavaPath = "";
                _ = ResolveRecommendedJavaPathAsync(inst?.Version);
                break;
        }
    }

    public async Task ResolveRecommendedJavaPathAsync(string? mcVersion)
    {
        try
        {
            var path = await _launchService.ResolveJavaForDisplayAsync(mcVersion);
            if ((GetSelectedJavaMode?.Invoke() ?? "Recommended") != "Recommended") return;
            ResolvedJavaPath = path ?? "";
            EffectiveJavaPath = ResolvedJavaPath;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to resolve the recommended Java path for display.", ex);
        }
    }

    public Task<string?> ResolveJavaForEditorAsync(string? instanceVersion) =>
        _launchService.ResolveJavaForDisplayAsync(instanceVersion);

    [RelayCommand]
    public async Task PickCustomJavaAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider != null)
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L10n.T("dlg_select_java"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(L10n.T("ft_java_exe")) { Patterns = new[] { "javaw.exe", "java.exe", "java" } },
                    FilePickerFileTypes.All
                }
            });

            if (files.Count > 0)
            {
                var chosen = files[0].Path.LocalPath;
                CustomJavaPath = chosen;
                ResolvedJavaPath = chosen;
                EffectiveJavaPath = chosen;
                SetSelectedJavaMode?.Invoke("Custom");
                SaveLauncherConfigRequested?.Invoke();
            }
        }
    }

    [RelayCommand]
    public Task BrowseJavaPathAsync() => PickCustomJavaAsync();

    [RelayCommand]
    public void OpenJavaInstaller()
    {
        IsJavaInstallerOpen = true;
        DiscordPresenceService.SetChoosingVersion();
    }

    [RelayCommand]
    public void CloseJavaInstaller()
    {
        IsJavaInstallerOpen = false;
        DiscordPresenceService.SetBrowsingProfiles();
        UpdateJavaDisplay();
    }

    [RelayCommand]
    public async Task SwitchToOfflineAccountAsync()
    {
        IsElyByErrorOpen = false;
        try
        {
            var selectedAccount = GetSelectedAccount?.Invoke();
            var activeName = GetActiveAccountName?.Invoke() ?? "Steve";
            var targetName = !string.IsNullOrWhiteSpace(selectedAccount?.Username)
                ? selectedAccount.Username
                : (!string.IsNullOrWhiteSpace(activeName) ? activeName : "Steve");

            var existingAccounts = _accountService.GetAccounts();
            var offlineAcc = existingAccounts.FirstOrDefault(a => a.Type == AccountType.Offline && string.Equals(a.Username, targetName, StringComparison.OrdinalIgnoreCase));

            if (offlineAcc == null)
            {
                offlineAcc = await _accountService.AddOfflineAccountAsync(targetName);
            }

            _accountService.SetActiveAccount(offlineAcc.Id);
            RefreshStateRequested?.Invoke();
        }
        catch (Exception)
        {
            StatusText = L10n.T("mw_status_switch_failed");
        }
    }

    public bool HasProfile(string idOrName)
    {
        var instances = GetInstances?.Invoke() ?? _instanceService.GetInstances();
        return instances.Any(i => i.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase)
                               || i.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> CheckCommandLineLaunchAsync(string[]? args)
    {
        if (args == null || args.Length < 2 || !args[0].Equals("--launch", StringComparison.OrdinalIgnoreCase))
            return false;

        var targetId = args[1].Trim('\"');
        var instances = GetInstances?.Invoke() ?? _instanceService.GetInstances();
        var match = instances.FirstOrDefault(i => i.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))
                 ?? instances.FirstOrDefault(i => i.Name.Equals(targetId, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            StatusText = string.Format(L10n.T("mw_status_profile_missing"), targetId);
            return false;
        }

        SelectInstanceRequested?.Invoke(match);
        await LaunchGameAsync();
        return match.IsRunning;
    }

    [RelayCommand]
    public async Task LaunchSpecificInstanceAsync(InstanceModel instance)
    {
        await LaunchSpecificInstanceWithArgsAsync(instance, null);
    }

    public async Task LaunchSpecificInstanceWithArgsAsync(InstanceModel? instance, string? extraArgs)
    {
        var inst = instance ?? GetSelectedInstance?.Invoke();
        if (inst == null) return;
        SelectInstanceRequested?.Invoke(inst);
        await LaunchGameWithArgsAsync(extraArgs);
    }

    [RelayCommand]
    public void KillInstance(InstanceModel? instance)
    {
        StopInstance(instance ?? GetSelectedInstance?.Invoke());
    }

    public void LoadFromConfig(LauncherConfigData cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.JvmArgs)) JvmArgs = cfg.JvmArgs;
        if (cfg.MinRamMb > 0) MinRamMb = cfg.MinRamMb;
        if (cfg.RamMb > 0) RamMb = cfg.RamMb;
        if (!string.IsNullOrWhiteSpace(cfg.LaunchBehavior)) SelectedLaunchBehavior = cfg.LaunchBehavior;
        if (cfg.GameWidth > 0) GameWidth = cfg.GameWidth;
        if (cfg.GameHeight > 0) GameHeight = cfg.GameHeight;
        IsFullscreen = cfg.IsFullscreen;
        if (!string.IsNullOrWhiteSpace(cfg.CustomJavaPath)) CustomJavaPath = cfg.CustomJavaPath;
    }

    public void ApplyToConfig(LauncherConfigData cfg)
    {
        cfg.JvmArgs = JvmArgs;
        cfg.MinRamMb = MinRamMb;
        cfg.RamMb = RamMb;
        cfg.LaunchBehavior = SelectedLaunchBehavior;
        cfg.GameWidth = GameWidth;
        cfg.GameHeight = GameHeight;
        cfg.IsFullscreen = IsFullscreen;
        cfg.CustomJavaPath = CustomJavaPath;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
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
}
