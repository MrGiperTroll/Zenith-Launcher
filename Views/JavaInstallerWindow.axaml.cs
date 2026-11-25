using Avalonia.Controls;
using ZenithLauncher.UI.Animations;

namespace ZenithLauncher.Views;

public partial class JavaInstallerWindow : Window
{
    public JavaInstallerWindow()
    {
        InitializeComponent();
        ZenithMotion.RegisterWindow(this);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
