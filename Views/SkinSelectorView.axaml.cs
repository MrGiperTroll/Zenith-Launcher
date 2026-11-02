using Avalonia.Controls;
using Avalonia.Input;
using CustomMcLauncher.ViewModels.Pages;

namespace CustomMcLauncher.Views;

public partial class SkinSelectorView : UserControl
{
    public SkinSelectorView()
    {
        InitializeComponent();
    }

    private void OnSkinCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is SkinItem skin && DataContext is SkinSelectorViewModel vm)
        {
            vm.SelectSkin(skin);
        }
    }
}
