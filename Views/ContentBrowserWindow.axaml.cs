using Avalonia.Controls;
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
}
