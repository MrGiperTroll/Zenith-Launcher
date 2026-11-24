using Avalonia.Controls;
using Avalonia.Input;
using ZenithLauncher.Models;
using ZenithLauncher.ViewModels;

namespace ZenithLauncher.Views.Components;

public partial class InstanceGridControl : UserControl
{
    private bool _suppressNextTap;

    public InstanceGridControl()
    {
        InitializeComponent();
    }

    private void OnInstanceCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Routed to parent for reordering
    }

    private void OnInstanceCardTapped(object? sender, TappedEventArgs e)
    {
        if (_suppressNextTap)
        {
            _suppressNextTap = false;
            e.Handled = true;
            return;
        }

        if (sender is Control { DataContext: InstanceModel instance } && DataContext is MainWindowViewModel vm)
        {
            vm.SelectInstanceCommand.Execute(instance);
        }
    }

    private void OnInstanceCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: InstanceModel instance } && DataContext is MainWindowViewModel vm)
        {
            vm.SelectInstanceCommand.Execute(instance);
            vm.NavigateToEditInstance(instance, "Overview");
        }
    }
}
