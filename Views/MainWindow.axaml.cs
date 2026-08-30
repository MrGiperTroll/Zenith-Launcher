using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CustomMcLauncher.Models;
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
        if (e.PropertyName == nameof(MainWindowViewModel.IsModpacksView))
        {
            var pane = _subscribedVm!.IsModpacksView
                ? this.FindControl<Grid>("ModpacksPane")
                : this.FindControl<Grid>("ProfilesPane");
            if (pane != null)
                UiFx.FadeInNow(pane, 140, 8);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsRightSidebarOpen))
        {
            AnimateSidebar(_subscribedVm!.IsRightSidebarOpen);
        }
    }

    private async void AnimateSidebar(bool open)
    {
        var sidebar = this.FindControl<Border>("InstanceDetailsSidebar");
        if (sidebar == null) return;

        const double width = 300;
        const int ms = 180;

        // Reset any stale in-flight close task
        if (_closeDelayToken != null)
        {
            _closeDelayToken.Cancel();
            _closeDelayToken.Dispose();
        }

        // TranslateTransform (with its own X transition) drives the slide.
        var translate = sidebar.RenderTransform as TranslateTransform;
        if (translate == null)
        {
            translate = new TranslateTransform();
            sidebar.RenderTransform = translate;
        }

        if (open)
        {
            sidebar.IsVisible = true;
            sidebar.IsHitTestVisible = true;

            // Start fully off-screen/invisible, then flip to the target values so
            // the lightweight Transitions (CubicEaseOut) interpolate them smoothly.
            translate.X = width;
            sidebar.Opacity = 0;

            translate.X = 0;
            sidebar.Opacity = 1;
        }
        else
        {
            sidebar.IsHitTestVisible = false;

            // Slide out to the right and fade, then hide once the transition ends.
            translate.X = width;
            sidebar.Opacity = 0;

            _closeDelayToken = new CancellationTokenSource();
            try
            {
                await Task.Delay(ms, _closeDelayToken.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            sidebar.IsVisible = false;
        }
    }
    private CancellationTokenSource? _closeDelayToken;

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

    private void OnModpackButtonHover(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: Models.ModrinthProject p })
            p.IsHovered = true;
    }

    private void OnModpackButtonHoverOut(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: Models.ModrinthProject p })
            p.IsHovered = false;
    }

    private void OnModpackCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: Models.ModrinthProject p } && DataContext is MainWindowViewModel vm)
            vm.ModpacksBrowser.OpenProjectPageCommand.Execute(p);
    }

    private void OnModpackButtonTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnInstanceCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: Models.InstanceModel instance } && DataContext is MainWindowViewModel vm)
            vm.SelectInstanceCommand.Execute(instance);
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
