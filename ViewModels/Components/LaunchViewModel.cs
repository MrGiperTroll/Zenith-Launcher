using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
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

    public Func<InstanceModel?>? GetSelectedInstance { get; set; }
    public Func<AccountModel?>? GetSelectedAccount { get; set; }
    public Func<string>? GetActiveAccountName { get; set; }
    public Func<string>? GetSelectedJavaMode { get; set; }
    public Func<string>? GetCustomJavaPath { get; set; }
    public Func<int>? GetMinRamMb { get; set; }
    public Func<int>? GetRamMb { get; set; }
    public Func<string>? GetSelectedLaunchBehavior { get; set; }
    public Action<bool>? SetElyByErrorOpen { get; set; }
    public Action<string>? OnStatusChanged { get; set; }

    [ObservableProperty]
    private bool _isLaunching;

    partial void OnIsLaunchingChanged(bool value)
    {
        OnPropertyChanged(nameof(LaunchButtonText));
        OnPropertyChanged(nameof(CanLaunchOrCancel));
    }

    [ObservableProperty]
    private double _launchProgress;

    [ObservableProperty]
    private string _statusText = string.Empty;

    partial void OnStatusTextChanged(string value)
    {
        OnStatusChanged?.Invoke(value);
    }

    [ObservableProperty]
    private string _currentDownloadFile = string.Empty;

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

    public bool IsInstanceLaunching(string instanceId) => _launchingInstanceIds.Contains(instanceId);

    public void StopInstance(InstanceModel? inst)
    {
        if (inst == null) return;
        _launchingInstanceIds.Remove(inst.Id);
        inst.IsLaunching = false;
        _launchService.KillProcess(inst.Id);
        inst.IsRunning = false;
        NotifyInstanceChanged();
    }

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
    }

    public void NotifyInstanceChanged()
    {
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(CanLaunchOrCancel));
        OnPropertyChanged(nameof(LaunchButtonText));
    }

    [RelayCommand]
    public async Task LaunchOrCancelAsync()
    {
        if (IsLaunching)
        {
            var instance = GetSelectedInstance?.Invoke() ?? _instanceService.GetSelectedInstance();
            if (instance != null)
            {
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

        var minRam = GetMinRamMb?.Invoke() ?? 512;
        var ram = GetRamMb?.Invoke() ?? 4096;

        if (instance.MinRamMb == null || instance.MinRamMb <= 0)
            instance.MinRamMb = minRam;
        instance.MaxMemoryMb = instance.RamMb ?? ram;

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
            var customJava = GetCustomJavaPath?.Invoke() ?? string.Empty;

            if (javaMode == "Custom" && (string.IsNullOrWhiteSpace(customJava) || !File.Exists(customJava)))
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
            var acc = GetSelectedAccount?.Invoke();
            if (acc?.Type == Models.AccountType.ElyBy)
            {
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

        var behavior = GetSelectedLaunchBehavior?.Invoke() ?? "KeepOpen";
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
}
