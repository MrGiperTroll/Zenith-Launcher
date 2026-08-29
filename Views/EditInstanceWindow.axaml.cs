using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class EditInstanceWindow : Window
{
    private bool _followBottom = true;

    public EditInstanceWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is EditInstanceViewModel vm)
        {
            vm.PropertyChanged -= OnVmPropertyChanged;
            vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditInstanceViewModel.LogText))
        {
            if (_followBottom && LogScroll != null)
            {
                Dispatcher.UIThread.Post(() => ScrollToEnd());
            }
        }
    }

    private void ScrollToEnd()
    {
        if (LogScroll == null) return;
        var maxY = Math.Max(0, LogScroll.Extent.Height - LogScroll.Viewport.Height);
        LogScroll.Offset = new Vector(LogScroll.Offset.X, maxY);
    }

    private void OnLogScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (LogScroll == null) return;
        var atBottom = LogScroll.Extent.Height - LogScroll.Offset.Y - LogScroll.Viewport.Height < 40;
        if (atBottom)
        {
            _followBottom = true;
        }
        else if (e.OffsetDelta.Y < 0)
        {
            _followBottom = false;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (DataContext is EditInstanceViewModel vm)
            {
                var view = vm.SelectedTab switch
                {
                    "Mods" => ModView,
                    "Resource Packs" => ResourceView,
                    "Shader Packs" => ShaderView,
                    "Worlds" => WorldView,
                    "Screenshots" => ScreenshotView,
                    _ => null
                };
                view?.FocusSearch();
                e.Handled = true;
            }
        }
    }

    private void OnDataPackDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: Models.InstanceFileEntry entry } && DataContext is EditInstanceViewModel vm)
            vm.ShowDataPackDetails(entry);
    }
}