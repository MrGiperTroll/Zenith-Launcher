using Avalonia.Controls;
using Avalonia.Interactivity;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class SourceFilterDropdown : UserControl
{
    public SourceFilterDropdown()
    {
        InitializeComponent();
    }

    private void OnModrinthClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SourceFilterViewModel vm)
        {
            vm.SelectSource("modrinth");
            DropdownTriggerBtn.Flyout?.Hide();
        }
    }

    private void OnCurseForgeClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SourceFilterViewModel vm)
        {
            vm.SelectSource("curseforge");
            DropdownTriggerBtn.Flyout?.Hide();
        }
    }
}
