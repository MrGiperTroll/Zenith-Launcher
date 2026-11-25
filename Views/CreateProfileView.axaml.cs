using Avalonia.Controls;
using ZenithLauncher.UI.Animations;

namespace ZenithLauncher.Views;

public partial class CreateProfileView : UserControl
{
    public CreateProfileView()
    {
        InitializeComponent();
        SmoothScrollBehavior.Attach(GroupedVersionsScroll, wheelStep: 76.0, damping: 16.0);
        SmoothScrollBehavior.Attach(FlatVersionsList, wheelStep: 76.0, damping: 14.0);
        SmoothScrollBehavior.Attach(ProfileDetailsScroll, wheelStep: 76.0, damping: 16.0);
    }
}
