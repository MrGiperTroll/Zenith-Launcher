using Avalonia.Controls;
using Avalonia.Input;
using ZenithLauncher.Views;

namespace ZenithLauncher.Views.Components;

public partial class InstanceGridControl : UserControl
{
    public InstanceGridControl()
    {
        InitializeComponent();
    }

    private void OnInstanceCardPointerPressed(object? sender, PointerPressedEventArgs e) =>
        (VisualRoot as MainWindow)?.OnInstanceCardPointerPressed(sender, e);

    private void OnInstanceCardTapped(object? sender, TappedEventArgs e) =>
        (VisualRoot as MainWindow)?.OnInstanceCardTapped(sender, e);

    private void OnInstanceCardDoubleTapped(object? sender, TappedEventArgs e) =>
        (VisualRoot as MainWindow)?.OnInstanceCardDoubleTapped(sender, e);
}
