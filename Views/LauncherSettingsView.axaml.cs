using Avalonia.Controls;
using CustomMcLauncher.ViewModels.Pages;

namespace CustomMcLauncher.Views;

public partial class LauncherSettingsView : UserControl
{
    public LauncherSettingsView()
    {
        InitializeComponent();
    }

    private void OnScaleSliderPointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        if (DataContext is ViewModels.Pages.LauncherSettingsViewModel vm)
        {
            vm.CommitLauncherScale();
        }
    }

    private void OnScaleSliderPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (DataContext is ViewModels.Pages.LauncherSettingsViewModel vm)
        {
            vm.CommitLauncherScale();
        }
    }

    private void OnScaleSliderKeyUp(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (DataContext is ViewModels.Pages.LauncherSettingsViewModel vm)
        {
            vm.CommitLauncherScale();
        }
    }
}
