using Avalonia.Controls;

namespace CustomMcLauncher.Views;

public partial class JavaInstallerWindow : Window
{
    public JavaInstallerWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
