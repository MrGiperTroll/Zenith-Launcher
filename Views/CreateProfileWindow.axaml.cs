using Avalonia;
using Avalonia.Controls;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.Views;

public partial class CreateProfileWindow : Window
{
    public CreateProfileWindow()
    {
        InitializeComponent();
        if (Content is Visual root)
            UiFx.FadeIn(root, 150, 10);
    }
}
