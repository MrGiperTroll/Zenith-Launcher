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
    public MainWindow()
    {
        InitializeComponent();
        Opacity = 1.0;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        
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

    private bool _isExplicitExit;

    public void ForceClose()
    {
        _isExplicitExit = true;
        Close();
    }

    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        WindowState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        Activate();
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
                    OptimizeMemory();
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
