using Avalonia.Controls;
using Avalonia.Interactivity;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class CategoryFilterDropdown : UserControl
{
    public CategoryFilterDropdown()
    {
        InitializeComponent();
    }

    private void OnCategoryItemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CategoryFilterItem item } && DataContext is CategoryFilterViewModel vm)
        {
            vm.SelectItem(item);
            DropdownTriggerBtn.Flyout?.Hide();
        }
    }
}
