using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ZenithLauncher.Services;
using ZenithLauncher.UI.Animations;
using ZenithLauncher.ViewModels;

namespace ZenithLauncher.Views;

public partial class EditInstanceView : UserControl
{
    private bool _followBottom = true;
    private EditInstanceViewModel? _boundVm;

    public EditInstanceView()
    {
        InitializeComponent();
        SmoothScrollBehavior.Attach(OverviewScroll, wheelStep: 78.0, damping: 16.0);
        SmoothScrollBehavior.Attach(DataPacksList, wheelStep: 82.0, damping: 14.0);
        SmoothScrollBehavior.Attach(WorldsList, wheelStep: 82.0, damping: 14.0);
        SmoothScrollBehavior.Attach(ServersList, wheelStep: 82.0, damping: 14.0);
        SmoothScrollBehavior.Attach(SettingsScroll, wheelStep: 78.0, damping: 16.0);
        SmoothScrollBehavior.Attach(ConflictListScroll, wheelStep: 68.0, damping: 16.0);
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            if (_boundVm != null)
            {
                _boundVm.PropertyChanged -= OnVmPropertyChanged;
                _boundVm = null;
            }
        };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundVm != null)
        {
            _boundVm.PropertyChanged -= OnVmPropertyChanged;
            _boundVm = null;
        }

        if (DataContext is EditInstanceViewModel vm)
        {
            _boundVm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditInstanceViewModel.SelectedTab))
        {
            var activeTab = TabContentGrid.Children.OfType<Control>().FirstOrDefault(c => c.IsVisible);
            ZenithMotion.TransitionTab(activeTab);
        }
        else if (e.PropertyName == nameof(EditInstanceViewModel.LogText))
        {
            if (_followBottom && LogTextBox != null)
            {
                Dispatcher.UIThread.Post(() => ScrollToEnd());
            }
        }
    }

    private void ScrollToEnd()
    {
        if (LogTextBox?.Text != null)
        {
            LogTextBox.CaretIndex = LogTextBox.Text.Length;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (DataContext is EditInstanceViewModel vm)
            {
                var view = vm.SelectedTab switch
                {
                    "Mods" => ModView,
                    "Resource Packs" => ResourceView,
                    "Shader Packs" => ShaderView,
                    "Screenshots" => ScreenshotView,
                    _ => null
                };
                view?.FocusSearch();
                e.Handled = true;
            }
        }
    }

    private void OnDataPackDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: Models.InstanceFileEntry entry } && DataContext is EditInstanceViewModel vm)
            vm.ShowDataPackDetails(entry);
    }

    private void OnTitleDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            e.Handled = true;
            tb.Focus();
            Dispatcher.UIThread.Post(() => tb.SelectAll());
        }
    }

    private void OnInstanceNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditInstanceViewModel vm)
            vm.SaveInstanceName();
    }

    private void OnLogSourceOptionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string src } && DataContext is EditInstanceViewModel vm)
        {
            vm.SelectLogSourceCommand.Execute(src);
            LogSourceDropdownBtn.Flyout?.Hide();
        }
    }

    private void OnDataPackWorldOptionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Models.InstanceFileEntry worldEntry } && DataContext is EditInstanceViewModel vm)
        {
            vm.SelectWorldEntryCommand.Execute(worldEntry);
            DataPackWorldDropdownBtn.Flyout?.Hide();
        }
        else if (sender is Button { DataContext: string world } && DataContext is EditInstanceViewModel vmStr)
        {
            vmStr.SelectWorldCommand.Execute(world);
            DataPackWorldDropdownBtn.Flyout?.Hide();
        }
    }
}
