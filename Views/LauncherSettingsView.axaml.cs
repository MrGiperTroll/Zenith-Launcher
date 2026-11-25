using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using ZenithLauncher.UI.Animations;
using ZenithLauncher.ViewModels.Pages;

namespace ZenithLauncher.Views;

public partial class LauncherSettingsView : UserControl
{
    private LauncherSettingsViewModel? _boundVm;
    private string? _lastAnimatedTab;
    private int _lastTabIndex;

    public LauncherSettingsView()
    {
        InitializeComponent();
        SmoothScrollBehavior.Attach(SettingsContentScroll, wheelStep: 78.0, damping: 16.0);

        TabGamePanel.PropertyChanged += OnTabPanelPropertyChanged;
        TabJavaPanel.PropertyChanged += OnTabPanelPropertyChanged;
        TabGeneralPanel.PropertyChanged += OnTabPanelPropertyChanged;
        TabAppearancePanel.PropertyChanged += OnTabPanelPropertyChanged;

        DataContextChanged += OnViewDataContextChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            if (_boundVm != null)
            {
                _boundVm = null;
            }
            ZenithMotion.CancelControl(TabGamePanel);
            ZenithMotion.CancelControl(TabJavaPanel);
            ZenithMotion.CancelControl(TabGeneralPanel);
            ZenithMotion.CancelControl(TabAppearancePanel);
        };
    }

    private void OnViewDataContextChanged(object? sender, System.EventArgs e)
    {
        _boundVm = DataContext as LauncherSettingsViewModel;
        _lastAnimatedTab = _boundVm?.CurrentSettingsTab;
        _lastTabIndex = GetSettingsTabIndex(_lastAnimatedTab);
    }

    private void OnTabPanelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || sender is not Control panel)
            return;

        if (!e.GetNewValue<bool>())
        {
            ZenithMotion.CancelControl(panel);
            return;
        }

        if (_boundVm != null && _boundVm.CurrentSettingsTab != _lastAnimatedTab)
        {
            int newIndex = GetSettingsTabIndex(_boundVm.CurrentSettingsTab);
            int dir = newIndex >= _lastTabIndex ? 1 : -1;
            _lastAnimatedTab = _boundVm.CurrentSettingsTab;
            _lastTabIndex = newIndex;
            if (SettingsContentScroll != null)
            {
                SettingsContentScroll.Offset = Vector.Zero;
            }
            ZenithMotion.AnimateDirectionalTab(panel, dir, distance: 48.0, durationMs: 285);
        }
    }

    private static int GetSettingsTabIndex(string? tab) => (tab ?? "").ToLowerInvariant() switch
    {
        "game" => 0,
        "java" => 1,
        "general" or "launcher" => 2,
        "appearance" => 3,
        _ => 0
    };

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

    private void OnAnimationHeaderTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is LauncherSettingsViewModel vm)
            vm.ToggleAnimationDetailsCommand.Execute(null);
    }

    private void OnParticleSliderPointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        if (DataContext is LauncherSettingsViewModel vm)
            vm.CommitParticleSettings();
    }

    private void OnParticleSliderPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (DataContext is LauncherSettingsViewModel vm)
            vm.CommitParticleSettings();
    }

    private void OnParticleSliderKeyUp(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (DataContext is LauncherSettingsViewModel vm)
            vm.CommitParticleSettings();
    }

    private void OnCustomColorFlyoutClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is LauncherSettingsViewModel vm)
        {
            vm.SaveCustomThemeImmediately();
        }
    }
}
