using Avalonia.Controls;
using Avalonia.Interactivity;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class TagFilterDropdown : UserControl
{
    public TagFilterDropdown()
    {
        InitializeComponent();
    }

    private void OnTagItemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TagFilterItem item })
        {
            item.Select();
            DropdownTriggerBtn.Flyout?.Hide();
        }
    }
}
