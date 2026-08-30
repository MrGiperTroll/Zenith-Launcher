using Avalonia;
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Views;

public partial class ProjectDetailsView : UserControl
{
    private DispatcherTimer? _textFadeTimer;

    public ProjectDetailsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
            cbVm.PropertyChanged += OnViewModelPropertyChanged;
        else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
            mpVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewModels.ContentBrowserViewModel.ReinstallText)
            && e.PropertyName != "ReinstallText") return;

        var actionBtn = this.FindControl<Button>("actionBtn");
        if (actionBtn == null) return;

        // Quick fade-out → update → fade-in for smooth text transition
        actionBtn.Opacity = 0.3;
        _textFadeTimer?.Stop();
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
