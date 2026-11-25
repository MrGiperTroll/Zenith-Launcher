using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ZenithLauncher.Models;
using ZenithLauncher.Services;
using ZenithLauncher.UI.Animations;
using ZenithLauncher.ViewModels;

namespace ZenithLauncher.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _observedVm;
    private System.Threading.CancellationTokenSource? _unloadCleanupCts;
    private LauncherNavPage? _lastAnimatedPage;

    private static MainWindow? _activeWindow;

    public MainWindow()
    {
        _activeWindow = this;
        InitializeComponent();
        Opacity = 1.0;
        ZenithMotion.RegisterWindow(this);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        DataContextChanged += OnMainDataContextChanged;
        Closed += OnMainWindowClosed;

        PropertyChanged += (s, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                UpdateWindowChromeState();
            }
        };
    }

    private void OnMainDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedVm != null)
        {
            _observedVm.PropertyChanged -= OnMainVmPropertyChanged;
            _observedVm = null;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            _observedVm = vm;
            _lastAnimatedPage = vm.CurrentPage;
            vm.PropertyChanged += OnMainVmPropertyChanged;
            SyncSecondaryPanes(vm);
        }
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        try { _unloadCleanupCts?.Cancel(); _unloadCleanupCts?.Dispose(); } catch { }
        _unloadCleanupCts = null;
        if (_observedVm != null)
        {
            _observedVm.PropertyChanged -= OnMainVmPropertyChanged;
            _observedVm = null;
        }
    }

    private void OnMainVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel vm) return;
        if (e.PropertyName is nameof(MainWindowViewModel.CurrentPage)
            or nameof(MainWindowViewModel.CurrentEditInstanceVm)
            or nameof(MainWindowViewModel.CurrentContentBrowserVm)
            or nameof(MainWindowViewModel.CurrentServerManagerVm)
            or nameof(MainWindowViewModel.CreateProfileVm)
            or nameof(MainWindowViewModel.CreateServerVm))
        {
            SyncSecondaryPanes(vm);
        }
    }

    private void SyncSecondaryPanes(MainWindowViewModel vm)
    {
        var page = vm.CurrentPage;
        bool anyUnloaded = false;

        anyUnloaded |= SyncPane<Pages.ModpacksView>(ModpacksPane, page == LauncherNavPage.Modpacks, explicitDataContext: null, hasExplicitDc: false);
        anyUnloaded |= SyncPane<EditInstanceView>(EditInstancePane, page == LauncherNavPage.EditInstance, vm.CurrentEditInstanceVm, hasExplicitDc: true);
        anyUnloaded |= SyncPane<ContentBrowserView>(ContentBrowserPane, page == LauncherNavPage.ContentBrowser, vm.CurrentContentBrowserVm, hasExplicitDc: true);
        anyUnloaded |= SyncPane<LauncherSettingsView>(SettingsPane, page == LauncherNavPage.Settings, vm.LauncherSettingsVm, hasExplicitDc: true);
        anyUnloaded |= SyncPane<ServerManagerView>(ServerManagerPane, page == LauncherNavPage.ServerManager, vm.CurrentServerManagerVm, hasExplicitDc: true);
        anyUnloaded |= SyncPane<AccountsView>(AccountsPane, page == LauncherNavPage.Accounts, vm.AccountsVm, hasExplicitDc: true);
        anyUnloaded |= SyncPane<CreateProfileView>(CreateProfilePane, page == LauncherNavPage.CreateProfile, vm.CreateProfileVm, hasExplicitDc: true);
        anyUnloaded |= SyncPane<CreateServerView>(CreateServerPane, page == LauncherNavPage.CreateServer, vm.CreateServerVm, hasExplicitDc: true);

        if (anyUnloaded)
        {
            ScheduleHeavyPageCleanup();
        }

        if (_lastAnimatedPage != page)
        {
            _lastAnimatedPage = page;
            AnimateActivePageEntrance(vm, page);
        }
    }

    private void AnimateActivePageEntrance(MainWindowViewModel vm, LauncherNavPage page)
    {
        Control? target = page switch
        {
            LauncherNavPage.Profiles => ProfilesPane,
            LauncherNavPage.Modpacks => ModpacksPane.Children.Count > 0 ? ModpacksPane.Children[0] as Control : null,
            LauncherNavPage.EditInstance => EditInstancePane.Children.Count > 0 ? EditInstancePane.Children[0] as Control : null,
            LauncherNavPage.ContentBrowser => ContentBrowserPane.Children.Count > 0 ? ContentBrowserPane.Children[0] as Control : null,
            LauncherNavPage.Settings => SettingsPane.Children.Count > 0 ? SettingsPane.Children[0] as Control : null,
            LauncherNavPage.ServerManager => ServerManagerPane.Children.Count > 0 ? ServerManagerPane.Children[0] as Control : null,
            LauncherNavPage.Accounts => AccountsPane.Children.Count > 0 ? AccountsPane.Children[0] as Control : null,
            LauncherNavPage.CreateProfile => CreateProfilePane.Children.Count > 0 ? CreateProfilePane.Children[0] as Control : null,
            LauncherNavPage.CreateServer => CreateServerPane.Children.Count > 0 ? CreateServerPane.Children[0] as Control : null,
            _ => null
        };

        if (target == null) return;

        switch (vm.Navigation.LastNavigationKind)
        {
            case NavTransitionKind.HistoryBack:
                ZenithMotion.AnimateEntrance(target, offsetX: -18, offsetY: 0, startScale: 1.0, startOpacity: 0.25, durationMs: 175);
                break;
            case NavTransitionKind.HistoryForward:
                ZenithMotion.AnimateEntrance(target, offsetX: 18, offsetY: 0, startScale: 1.0, startOpacity: 0.25, durationMs: 175);
                break;
            case NavTransitionKind.DrillIn:
                ZenithMotion.AnimateEntrance(target, offsetX: 20, offsetY: 0, startScale: 0.988, startOpacity: 0.2, durationMs: 185);
                break;
            case NavTransitionKind.DrillOut:
                ZenithMotion.AnimateEntrance(target, offsetX: -14, offsetY: 0, startScale: 1.0, startOpacity: 0.3, durationMs: 165);
                break;
            default:
                double dy = vm.Navigation.LastNavigationOrderDelta >= 0 ? 10 : -10;
                ZenithMotion.AnimateEntrance(target, offsetX: 0, offsetY: dy, startScale: 1.0, startOpacity: 0.25, durationMs: 165);
                break;
        }
    }

    public static void RequestBackgroundMemoryCleanup()
    {
        _activeWindow?.ScheduleHeavyPageCleanup();
    }

    private void ScheduleHeavyPageCleanup()
    {
        try { SkiaSharp.SKGraphics.PurgeAllCaches(); } catch { }

        try { _unloadCleanupCts?.Cancel(); _unloadCleanupCts?.Dispose(); } catch { }
        var cts = new System.Threading.CancellationTokenSource();
        _unloadCleanupCts = cts;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(420, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                try
                {
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                        System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    if (token.IsCancellationRequested) return;
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                        System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                }
                catch { }
                if (token.IsCancellationRequested) return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        try { SkiaSharp.SKGraphics.PurgeAllCaches(); } catch { }
                    }
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
            catch (OperationCanceledException) { }
            catch { }
        }, token);
    }

    private static bool SyncPane<TView>(Panel? pane, bool active, object? explicitDataContext, bool hasExplicitDc)
        where TView : Control, new()
    {
        if (pane == null) return false;

        if (!active || (hasExplicitDc && explicitDataContext == null))
        {
            if (pane.Children.Count > 0)
            {
                if (hasExplicitDc)
                {
                    foreach (var child in pane.Children)
                        child.DataContext = null;
                }
                pane.Children.Clear();
                return true;
            }
            return false;
        }

        if (pane.Children.Count == 0)
        {
            var view = new TView();
            if (hasExplicitDc)
                view.DataContext = explicitDataContext;
            pane.Children.Add(view);
        }
        else if (hasExplicitDc && !ReferenceEquals(pane.Children[0].DataContext, explicitDataContext))
        {
            pane.Children[0].DataContext = explicitDataContext;
        }
        return false;
    }


    private void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

        private void OnMinimizeButtonClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeButtonClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaximizeIcon();
    }

    private bool _isExplicitExit;

    public void ForceClose()
    {
        _isExplicitExit = true;
        Close();
    }

    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isExplicitExit)
        {
            try
            {
                var cfg = LauncherConfigData.Load();
                if (cfg.MinimizeToTrayOnClose)
                {
                    e.Cancel = true;
                    Hide();
                    return;
                }
            }
            catch { }
        }
        base.OnClosing(e);
    }

    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateMaximizeIcon() => UpdateWindowChromeState();

    private void UpdateWindowChromeState()
    {
        bool isMaximized = WindowState == WindowState.Maximized;
        var icon = this.FindControl<PathIcon>("MaximizeButtonIcon");
        if (icon != null)
        {
            icon.Data = isMaximized
                ? StreamGeometry.Parse("M4 8H8V4H20V16H16V20H4V8ZM6 10V18H14V10H6ZM16 14H18V6H10V8H16V14Z")
                : StreamGeometry.Parse("M4 4h16v16H4V4zm2 2v12h12V6H6z");
        }

        var rootBorder = this.FindControl<Border>("WindowRootBorder");
        if (rootBorder != null)
        {
            rootBorder.CornerRadius = isMaximized ? new CornerRadius(0) : new CornerRadius(12);
            rootBorder.BorderThickness = isMaximized ? new Thickness(0) : new Thickness(1);
        }

        var closeBtn = this.FindControl<Button>("CloseWindowBtn");
        if (closeBtn != null)
        {
            closeBtn.CornerRadius = isMaximized ? new CornerRadius(0) : new CornerRadius(0, 11, 0, 0);
        }
    }

private void OnTopBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaximizeIcon();
    }



    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return;
        if (FocusManager?.GetFocusedElement() is TextBox) return;
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.CanLaunch && vm.SelectedInstance != null)
        {
            vm.LaunchOrCancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnLinkTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { }
        }
    }


}
