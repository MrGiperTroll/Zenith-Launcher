using Avalonia.Controls;
using Avalonia.Input;
using ZenithLauncher.UI.Animations;
using ZenithLauncher.ViewModels;
using ZenithLauncher.ViewModels.Items;

namespace ZenithLauncher.Views;

public partial class AuthorProfileView : UserControl
{
    public AuthorProfileView()
    {
        InitializeComponent();
        SmoothScrollBehavior.Attach(AuthorProjectsList, wheelStep: 86.0, damping: 14.0);
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
