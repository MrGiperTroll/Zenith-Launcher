using Avalonia;
using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Views;

public partial class ProjectDetailsView : UserControl
{
    private DispatcherTimer? _textFadeTimer;
    private string? _lastReinstallText;
    private PropertyChangedEventHandler? _handler;
    private object? _subscribedContext;

    public ProjectDetailsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from previous ViewModel
        UnsubscribeFromPrevious();

        _lastReinstallText = null;
        _handler = OnViewModelPropertyChanged;

        if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
        {
            cbVm.PropertyChanged += _handler;
            _subscribedContext = cbVm;
        }
        else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
        {
            mpVm.PropertyChanged += _handler;
            _subscribedContext = mpVm;
        }
    }

    private void UnsubscribeFromPrevious()
    {
        if (_handler == null || _subscribedContext == null) return;

        if (_subscribedContext is ViewModels.ContentBrowserViewModel oldCb)
            oldCb.PropertyChanged -= _handler;
        else if (_subscribedContext is ViewModels.ModpacksBrowserViewModel oldMp)
            oldMp.PropertyChanged -= _handler;

        _subscribedContext = null;
        _handler = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "ReinstallText") return;

        var actionBtn = this.FindControl<Button>("actionBtn");
        if (actionBtn == null) return;

        // Get current text from the DataContext
        string? currentText = null;
        if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
            currentText = cbVm.ReinstallText;
        else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
            currentText = mpVm.ReinstallText;

        // Skip if text hasn't actually changed — prevents redundant animations
        if (currentText == _lastReinstallText) return;
        _lastReinstallText = currentText;

        // Smooth single fade cycle: opacity 1→0.3 (120ms), then back to 1 (120ms)
        _textFadeTimer?.Stop();
        actionBtn.Opacity = 0.3;
        _textFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _textFadeTimer.Tick += (_, _) =>
        {
            _textFadeTimer.Stop();
            actionBtn.Opacity = 1.0;
        };
        _textFadeTimer.Start();
    }

    private void OnVersionRowTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Border { DataContext: Models.ModrinthProjectVersion version })
        {
            if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
            {
                cbVm.SelectedVersionForDetails = version;
            }
            else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
            {
                mpVm.SelectedVersionForDetails = version;
            }
        }
    }

    private void OnCompatibilityVersionTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Border { DataContext: string version })
        {
            if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
            {
                cbVm.SelectCompatibilityVersionCommand.Execute(version);
            }
            else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
            {
                mpVm.SelectCompatibilityVersionCommand.Execute(version);
            }
        }
    }
}
