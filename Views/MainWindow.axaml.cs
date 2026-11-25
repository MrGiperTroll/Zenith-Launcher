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
    private object? _lastPageVmInstance;
    private bool _isPageMotionReady;

    private static MainWindow? _activeWindow;

    public MainWindow()
    {
        _activeWindow = this;
        InitializeComponent();
        Opacity = 1.0;
        ZenithMotion.RegisterWindow(this);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

        foreach (var pane in GetAllPagePanes())
        {
            pane.PropertyChanged += OnPagePanePropertyChanged;
        }

        DataContextChanged += OnMainDataContextChanged;
        Closed += OnMainWindowClosed;
        LayoutUpdated += OnMainWindowLayoutUpdated;

        PropertyChanged += (s, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                UpdateWindowChromeState();
            }
        };
    }

    private Control[] GetAllPagePanes() =>
    [
        ProfilesPane,
        ModpacksPane,
        EditInstancePane,
        ContentBrowserPane,
        SettingsPane,
        ServerManagerPane,
        AccountsPane,
        CreateProfilePane,
        CreateServerPane
    ];

    private void OnMainDataContextChanged(object? sender, EventArgs e)
    {
        _isPageMotionReady = false;

        if (_observedVm != null)
        {
            _observedVm.PropertyChanged -= OnMainVmPropertyChanged;
            if (_observedVm.LauncherSettingsVm != null)
                _observedVm.LauncherSettingsVm.ClearSnowAccumulationRequested = null;
            _observedVm = null;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            _observedVm = vm;
            _lastAnimatedPage = vm.CurrentPage;
            _lastPageVmInstance = GetPageSubVm(vm, vm.CurrentPage);
            vm.PropertyChanged += OnMainVmPropertyChanged;
            if (vm.LauncherSettingsVm != null)
                vm.LauncherSettingsVm.ClearSnowAccumulationRequested = () => ParticleOverlay?.ClearSnowAccumulation();
            SyncSecondaryPanes(vm);
            _isPageMotionReady = true;
        }
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        try { ParticleOverlay?.FlushSnowPersistence(); } catch { }
        try { _unloadCleanupCts?.Cancel(); _unloadCleanupCts?.Dispose(); } catch { }
        _unloadCleanupCts = null;
        if (_observedVm != null)
        {
            _observedVm.PropertyChanged -= OnMainVmPropertyChanged;
            if (_observedVm.LauncherSettingsVm != null)
                _observedVm.LauncherSettingsVm.ClearSnowAccumulationRequested = null;
            _observedVm = null;
        }
    }

    private void OnMainWindowLayoutUpdated(object? sender, EventArgs e)
    {
        if (ParticleOverlay == null || !ParticleOverlay.IsSnowPhysicsActive)
            return;

        bool occluded = HasVisibleModalOverlay(ConsoleDrawer) || HasVisibleModalOverlay(ModalsHost);
        if (ParticleOverlay.IsModalOccluded != occluded)
        {
            ParticleOverlay.IsModalOccluded = occluded;
        }
    }

    private static bool HasVisibleModalOverlay(Control? host)
    {
        if (host == null) return false;
        foreach (var child in host.GetVisualChildren())
        {
            if (child is Panel panel)
            {
                foreach (var item in panel.Children)
                {
                    if (item is Border b
                        && b.IsVisible
                        && b.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Stretch
                        && !ZenithMotion.GetToastOpen(b))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
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

        object? activeSubVm = GetPageSubVm(vm, page);
        if (_isPageMotionReady && _lastAnimatedPage == page && !ReferenceEquals(_lastPageVmInstance, activeSubVm))
        {
            var pane = GetPaneForPage(page);
            if (pane != null && pane.IsVisible)
            {
                AnimatePagePane(pane, vm);
            }
        }

        _lastAnimatedPage = page;
        _lastPageVmInstance = activeSubVm;
    }

    private void OnPagePanePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || sender is not Control pane)
            return;

        if (e.GetNewValue<bool>())
        {
            if (_isPageMotionReady && _observedVm != null)
            {
                _lastAnimatedPage = _observedVm.CurrentPage;
                _lastPageVmInstance = GetPageSubVm(_observedVm, _observedVm.CurrentPage);
                AnimatePagePane(pane, _observedVm);
            }
        }
        else
        {
            ZenithMotion.CancelControl(pane);
        }
    }

    private static object? GetPageSubVm(MainWindowViewModel vm, LauncherNavPage page) => page switch
    {
        LauncherNavPage.EditInstance => vm.CurrentEditInstanceVm,
        LauncherNavPage.ContentBrowser => vm.CurrentContentBrowserVm,
        LauncherNavPage.ServerManager => vm.CurrentServerManagerVm,
        LauncherNavPage.CreateProfile => vm.CreateProfileVm,
        LauncherNavPage.CreateServer => vm.CreateServerVm,
        _ => null
    };

    private Control? GetPaneForPage(LauncherNavPage page) => page switch
    {
        LauncherNavPage.Profiles => ProfilesPane,
        LauncherNavPage.Modpacks => ModpacksPane,
        LauncherNavPage.EditInstance => EditInstancePane,
        LauncherNavPage.ContentBrowser => ContentBrowserPane,
        LauncherNavPage.Settings => SettingsPane,
        LauncherNavPage.ServerManager => ServerManagerPane,
        LauncherNavPage.Accounts => AccountsPane,
        LauncherNavPage.CreateProfile => CreateProfilePane,
        LauncherNavPage.CreateServer => CreateServerPane,
        _ => null
    };

    private static void AnimatePagePane(Control? target, MainWindowViewModel vm)
    {
        if (target == null) return;

        switch (vm.Navigation.LastNavigationKind)
        {
            case NavTransitionKind.HistoryBack:
                ZenithMotion.AnimateEntrance(target, offsetX: -46, offsetY: 0, startScale: 0.98, startOpacity: 0.0, durationMs: 280);
                break;
            case NavTransitionKind.HistoryForward:
                ZenithMotion.AnimateEntrance(target, offsetX: 46, offsetY: 0, startScale: 0.98, startOpacity: 0.0, durationMs: 280);
                break;
            case NavTransitionKind.DrillIn:
                ZenithMotion.AnimateEntrance(target, offsetX: 46, offsetY: 0, startScale: 0.975, startOpacity: 0.0, durationMs: 295);
                break;
            case NavTransitionKind.DrillOut:
                ZenithMotion.AnimateEntrance(target, offsetX: -38, offsetY: 0, startScale: 0.985, startOpacity: 0.0, durationMs: 265);
                break;
            default:
                // Centered depth-bloom emergence for top-level sidebar destinations (zero vertical jitter).
                ZenithMotion.AnimateEntrance(target, offsetX: 0, offsetY: 0, startScale: 0.96, startOpacity: 0.0, durationMs: 275);
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
        try { ParticleOverlay?.FlushSnowPersistence(); } catch { }
        if (!_isExplicitExit && e.CloseReason != WindowCloseReason.ApplicationShutdown)
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
