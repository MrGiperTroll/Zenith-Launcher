using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Views;

public partial class ProjectDetailsView : UserControl
{
    public ProjectDetailsView()
    {
        InitializeComponent();
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
