using System;
using System.Linq;
using Avalonia.Controls;
using ZenithLauncher.ViewModels.Pages;

namespace ZenithLauncher.Views;

public partial class LauncherSettingsView : UserControl
{
    private LauncherSettingsViewModel? _boundVm;

    public LauncherSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
    }

    private void OnViewDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_boundVm != null)
        {
            _boundVm.PropertyChanged -= OnVmPropertyChanged;
            _boundVm = null;
        }

        if (DataContext is LauncherSettingsViewModel vm)
        {
            _boundVm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherSettingsViewModel.CurrentSettingsTab))
        {
            if (SettingsContentScroll != null)
            {
                SettingsContentScroll.Offset = Avalonia.Vector.Zero;
            }
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

    private void OnScaleInputKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            if (DataContext is LauncherSettingsViewModel vm && sender is TextBox tb)
            {
                vm.ApplyScalePercentInput(tb.Text);
            }
            TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            if (DataContext is LauncherSettingsViewModel vm)
            {
                vm.ScalePercentText = Math.Round(vm.PendingLauncherScale * 100).ToString("0");
            }
            TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
            e.Handled = true;
        }
    }

    private void OnScaleInputLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is LauncherSettingsViewModel vm && sender is TextBox tb)
        {
            vm.ApplyScalePercentInput(tb.Text);
        }
    }

    private void OnThemeCardClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ThemeCardModel card && card.IsCustom)
        {
            OpenCustomColorFlyout(btn);
        }
    }

    private void OpenCustomColorFlyout(Control target)
    {
        if (this.TryFindResource("CustomThemeColorFlyout", out var res) && res is Flyout flyout)
        {
            if (flyout.Content is Control content)
                content.DataContext = DataContext;
            flyout.ShowAt(target);
        }
    }

    private void OnCustomColorApplyClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.TryFindResource("CustomThemeColorFlyout", out var res) && res is Flyout flyout)
            flyout.Hide();
        if (DataContext is LauncherSettingsViewModel vm)
            vm.ApplyCustomThemeCard();
    }

    private void OnCustomColorFlyoutClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is LauncherSettingsViewModel vm)
        {
            vm.SaveCustomThemeImmediately();
        }
    }
}
