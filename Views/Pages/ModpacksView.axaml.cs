using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ZenithLauncher.ViewModels;
using ZenithLauncher.ViewModels.Items;

namespace ZenithLauncher.Views.Pages;

public partial class ModpacksView : UserControl
{
    private bool _modpacksScrollAttached;

    public ModpacksView()
    {
        InitializeComponent();
    }

    private void OnModpackButtonHover(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ModrinthProjectItemViewModel p })
            p.IsHovered = true;
    }

    private void OnModpackButtonHoverOut(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ModrinthProjectItemViewModel p })
            p.IsHovered = false;
    }

    private void OnModpackCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null)
        {
            e.Handled = true;
            return;
        }

        if (sender is Control { DataContext: ModrinthProjectItemViewModel p } && DataContext is MainWindowViewModel vm)
            vm.ModpacksBrowser.OpenProjectPageCommand.Execute(p);
    }

    private void OnModpackButtonTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnModpacksListAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_modpacksScrollAttached) return;
        if (sender is ListBox list)
        {
            list.AddHandler(ScrollViewer.ScrollChangedEvent, OnModpacksScrollChanged);
            _modpacksScrollAttached = true;
        }
    }

    private void OnModpacksScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer sv) return;
        if (DataContext is not MainWindowViewModel vm) return;
        if (sv.Extent.Height <= 0 || sv.Viewport.Height <= 0) return;
        if (sv.Extent.Height - sv.Offset.Y - sv.Viewport.Height < 450)
            _ = vm.ModpacksBrowser.LoadMoreAsync();
    }
}
