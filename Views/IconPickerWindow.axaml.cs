using Avalonia.Controls;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class IconPickerWindow : Window
{
    public IconPickerWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is IconPickerViewModel vm)
        {
            vm.IconSelected += _ => Close();
            vm.CustomIconPicked += _ => Close();
        }
    }
}
