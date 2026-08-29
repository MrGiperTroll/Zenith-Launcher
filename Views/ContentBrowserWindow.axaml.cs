using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class ContentBrowserWindow : Window
{
    public ContentBrowserWindow()
    {
        InitializeComponent();
    }

    private void OnResultsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (DataContext is not ContentBrowserViewModel vm) return;
        if (sv.Extent.Height - sv.Offset.Y - sv.Viewport.Height < 350)
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
        if (sender is Control { DataContext: Models.ModrinthProject p } && DataContext is ContentBrowserViewModel vm)
            vm.OpenProjectPageCommand.Execute(p);
    }

    private void OnInstallButtonTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
    }
}
