using Avalonia.Controls;
using Avalonia.Interactivity;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class SortFilterDropdown : UserControl
{
    public SortFilterDropdown()
    {
        InitializeComponent();
    }

    private void OnSortItemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SortFilterItem item })
        {
            item.Select();
            DropdownTriggerBtn.Flyout?.Hide();
        }
    }
}
