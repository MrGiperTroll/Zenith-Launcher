using Avalonia.Controls;
using Avalonia.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views.Experimental;

public partial class ExperimentalMainWindow : Window
{
    public ExperimentalMainWindow()
    {
        InitializeComponent();
    }

    private void OnCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control c && c.DataContext is InstanceModel inst &&
            DataContext is ExperimentalShellViewModel shell)
        {
            shell.SelectInstanceCommand.Execute(inst);
        }
    }
}
