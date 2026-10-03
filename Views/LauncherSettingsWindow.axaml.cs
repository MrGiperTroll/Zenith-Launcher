using System;
using Avalonia;
using Avalonia.Controls;
using CustomMcLauncher.Services;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class LauncherSettingsWindow : Window
{
    public LauncherSettingsWindow()
    {
        InitializeComponent();
        if (Content is Visual root)
            UiFx.FadeIn(root, 150, 10);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.PropertyChanged -= OnVmPropertyChanged;
                vm.PropertyChanged += OnVmPropertyChanged;
            }
        };
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentSettingsTab))
        {
            var sc = this.FindControl<ScrollViewer>("SettingsContentScroll");
            if (sc != null)
                UiFx.FadeInNow(sc, 220, 4);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Persist every setting change made in this window.
        (DataContext as MainWindowViewModel)?.SaveLauncherConfig();
        base.OnClosed(e);
    }
}
