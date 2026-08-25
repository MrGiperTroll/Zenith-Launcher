using Avalonia;
using Avalonia.Controls;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.Views;

public partial class AccountsWindow : Window
{
    public AccountsWindow()
    {
        InitializeComponent();
        if (Content is Visual root)
            UiFx.FadeIn(root, 150, 10);
    }
}
