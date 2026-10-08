using Avalonia.Controls;
using CustomMcLauncher.Services;
using CustomMcLauncher.UI.Animations;

namespace CustomMcLauncher.Views;

public partial class CreateProfileView : UserControl
{
    public CreateProfileView()
    {
        InitializeComponent();
        UiFx.FadeIn(this, 150, 10);
    }
}
