using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CustomMcLauncher.ViewModels;
using CustomMcLauncher.ViewModels.Items;

namespace CustomMcLauncher.Views;

public partial class AuthorProfileView : UserControl
{
    public AuthorProfileView()
    {
        InitializeComponent();
    }

    private void OnProjectCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ModrinthProjectItemViewModel project } &&
            DataContext is AuthorProfileViewModel vm)
        {
            vm.OpenProjectCommand.Execute(project);
        }
    }
}
