using Avalonia.Controls;
using ZenithLauncher.Services;
using ZenithLauncher.UI.Animations;

namespace ZenithLauncher.Views;

public partial class CreateProfileView : UserControl
{
    public CreateProfileView()
    {
        InitializeComponent();
        UiFx.FadeIn(this, 150, 10);
    }
}
