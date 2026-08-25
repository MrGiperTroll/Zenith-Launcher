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
    }

    protected override void OnClosed(EventArgs e)
    {
        // Persist every setting change made in this window.
        (DataContext as MainWindowViewModel)?.SaveLauncherConfig();
        base.OnClosed(e);
    }
}
