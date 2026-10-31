using System;
using Avalonia.Controls;
using Avalonia.Input;
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
        if (sender is Control { DataContext: ModrinthProjectVersion version })
        {
            if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
            {
                cbVm.OpenVersionDetailCommand.Execute(version);
            }
            else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
            {
                mpVm.OpenVersionDetailCommand.Execute(version);
            }
        }
    }

    private void OnGalleryItemTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ModrinthGalleryItem item })
        {
            if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
            {
                cbVm.OpenGalleryModalCommand.Execute(item);
            }
            else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
            {
                mpVm.OpenGalleryModalCommand.Execute(item);
            }
        }
    }

    private void OnGalleryViewerPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
        {
            if (e.Delta.Y > 0) cbVm.GalleryZoomInCommand.Execute(null);
            else if (e.Delta.Y < 0) cbVm.GalleryZoomOutCommand.Execute(null);
            e.Handled = true;
        }
        else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
        {
            if (e.Delta.Y > 0) mpVm.GalleryZoomInCommand.Execute(null);
            else if (e.Delta.Y < 0) mpVm.GalleryZoomOutCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnActionButtonHover(object? sender, PointerEventArgs e)
    {
        if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
        {
            cbVm.IsDetailsInstallButtonHovered = true;
        }
        else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
        {
            mpVm.IsDetailsInstallButtonHovered = true;
        }
    }

    private void OnActionButtonHoverOut(object? sender, PointerEventArgs e)
    {
        if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
        {
            cbVm.IsDetailsInstallButtonHovered = false;
        }
        else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
        {
            mpVm.IsDetailsInstallButtonHovered = false;
        }
    }
}
