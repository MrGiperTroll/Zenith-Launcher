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
    private MainWindowViewModel? _subscribedVm;

    public MainWindow()
    {
        InitializeComponent();
        Opacity = 1.0;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        
        // RAM optimization: when the window is minimized or deactivated, collect GC generation and release unneeded working set
        PropertyChanged += (s, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized)
            {
                OptimizeMemory();
            }
        };
        Deactivated += (s, e) =>
        {
            OptimizeMemory();
        };
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

    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateMaximizeIcon()
    {
        var icon = this.FindControl<PathIcon>("MaximizeButtonIcon");
        if (icon != null)
        {
            icon.Data = WindowState == WindowState.Maximized
                ? StreamGeometry.Parse("M4 8H8V4H20V16H16V20H4V8ZM6 10V18H14V10H6ZM16 14H18V6H10V8H16V14Z")
                : StreamGeometry.Parse("M4 4h16v16H4V4zm2 2v12h12V6H6z");
        }
    }

private void OnTopBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaximizeIcon();
    }

    private static void OptimizeMemory()
    {
        MemoryTrimmer.TrimMemory(force: true);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
        _subscribedVm = null;

        if (DataContext is not MainWindowViewModel vm) return;
        _subscribedVm = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Tab switching is instantaneous and static with zero jitter or layout shift
        if (e.PropertyName == nameof(MainWindowViewModel.IsUpdateOpen) && _subscribedVm!.IsUpdateOpen)
        {
            var overlay = this.FindControl<Control>("UpdateOverlay");
            if (overlay != null)
                UiFx.FadeInNow(overlay, 240, 4);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsJavaInstallerOpen))
        {
            if (_subscribedVm!.IsJavaInstallerOpen)
            {
                var card = this.FindControl<Border>("JavaInstallerCard");
                if (card != null)
                    UiFx.FadeInNow(card, 240, 6);
            }
        }
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

    private void OnModpackButtonHover(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ViewModels.Items.ModrinthProjectItemViewModel p })
            p.IsHovered = true;
    }

    private void OnModpackButtonHoverOut(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ViewModels.Items.ModrinthProjectItemViewModel p })
            p.IsHovered = false;
    }

    private void OnModpackCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null)
        {
            e.Handled = true;
            return;
        }

        if (sender is Control { DataContext: ViewModels.Items.ModrinthProjectItemViewModel p } && DataContext is MainWindowViewModel vm)
            vm.ModpacksBrowser.OpenProjectPageCommand.Execute(p);
    }

    private void OnModpackButtonTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnInstanceTargetCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null)
        {
            return;
        }
        if (sender is Control { DataContext: ViewModels.Dialogs.InstanceTargetItem item } && item.CanClickButton)
        {
            item.InstallCommand.Execute(null);
        }
    }

    private void OnWorldTargetCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null)
        {
            return;
        }
        if (sender is Control { DataContext: ViewModels.Dialogs.WorldTargetItem item } && !item.IsInstalled)
        {
            item.InstallCommand.Execute(null);
        }
    }

    private bool _modpacksScrollAttached;

    private void OnModpacksListAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_modpacksScrollAttached) return;
        if (sender is ListBox list)
        {
            list.AddHandler(ScrollViewer.ScrollChangedEvent, OnModpacksScrollChanged);
            _modpacksScrollAttached = true;
        }
    }

    private void OnModpacksScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer sv) return;
        if (DataContext is not MainWindowViewModel vm) return;
        if (sv.Extent.Height <= 0 || sv.Viewport.Height <= 0) return;
        if (sv.Extent.Height - sv.Offset.Y - sv.Viewport.Height < 450)
            _ = vm.ModpacksBrowser.LoadMoreAsync();
    }
}
