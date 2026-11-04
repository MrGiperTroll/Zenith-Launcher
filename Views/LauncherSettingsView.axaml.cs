using Avalonia.Controls;
using CustomMcLauncher.ViewModels.Pages;

namespace CustomMcLauncher.Views;

public partial class LauncherSettingsView : UserControl
{
    public LauncherSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
    }

    private void OnViewDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is LauncherSettingsViewModel vm)
        {
            vm.PropertyChanged -= OnVmPropertyChanged;
            vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherSettingsViewModel.CurrentSettingsTab))
        {
            SettingsContentScroll?.ScrollToHome();
        }
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
