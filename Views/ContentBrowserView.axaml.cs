using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class ContentBrowserView : UserControl
{
    private bool _scrollAttached;

    public ContentBrowserView()
    {
        InitializeComponent();
        ResultsList.AddHandler(ScrollViewer.ScrollChangedEvent, OnResultsScrollChanged);
        ResultsList.AttachedToVisualTree += OnResultsListAttached;
    }

    private void OnResultsListAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_scrollAttached) return;
        ResultsList.AddHandler(ScrollViewer.ScrollChangedEvent, OnResultsScrollChanged);
        _scrollAttached = true;
    }

    private void OnResultsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer sv) return;
        if (DataContext is not ContentBrowserViewModel vm) return;
        if (sv.Extent.Height <= 0 || sv.Viewport.Height <= 0) return;
        if (sv.Extent.Height - sv.Offset.Y - sv.Viewport.Height < 450)
            _ = vm.LoadMoreAsync();
    }

    private void OnInstalledButtonHover(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: Models.ModrinthProject p })
            p.IsHovered = true;
    }

    private void OnInstalledButtonHoverOut(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: Models.ModrinthProject p })
            p.IsHovered = false;
    }

    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null)
        {
            e.Handled = true;
            return;
        }

        if (sender is Control { DataContext: Models.ModrinthProject p } && DataContext is ContentBrowserViewModel vm)
            vm.OpenProjectPageCommand.Execute(p);
    }

    private void OnInstallButtonTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
    }
}
