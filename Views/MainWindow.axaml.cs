using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using CustomMcLauncher.Services;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _subscribedVm;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        UiFx.FadeIn(this, 200);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
        _subscribedVm = null;

        if (DataContext is not MainWindowViewModel vm) return;
        _subscribedVm = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsModpacksView)) return;

        var pane = _subscribedVm!.IsModpacksView
            ? this.FindControl<Grid>("ModpacksPane")
            : this.FindControl<Grid>("ProfilesPane");
        if (pane != null)
            UiFx.FadeInNow(pane, 140, 8);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return;
        if (FocusManager?.GetFocusedElement() is TextBox) return;
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.CanLaunch && vm.SelectedInstance != null)
        {
            vm.LaunchOrCancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnInstanceNameLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.OnInstanceNameLostFocus();
    }

    private void OnLinkTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { }
        }
    }
}

public class BoolToBorderBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            if (Avalonia.Application.Current?.Resources.TryGetResource("AccentBrush", ThemeVariant.Default, out var res) == true && res is ISolidColorBrush brush)
                return new SolidColorBrush(brush.Color);
            return new SolidColorBrush(Color.Parse("#10B981"));
        }
        return new SolidColorBrush(Color.Parse("#1C2438"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
