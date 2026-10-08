using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CustomMcLauncher.Views;

public partial class ThemeSettingsWindow : Window
{
    public ThemeSettingsWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
