using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
        Opacity = 1.0;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
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
                UiFx.FadeInNow(pane, 240, 4);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
        {
            Control? pane = _subscribedVm!.CurrentPage switch
            {
                LauncherNavPage.Profiles => this.FindControl<Grid>("ProfilesPane"),
                LauncherNavPage.Modpacks => this.FindControl<Grid>("ModpacksPane"),
                LauncherNavPage.EditInstance => this.FindControl<Panel>("EditInstancePane"),
                LauncherNavPage.ContentBrowser => this.FindControl<Panel>("ContentBrowserPane"),
                LauncherNavPage.Settings => this.FindControl<Panel>("SettingsPane"),
                LauncherNavPage.ServerManager => this.FindControl<Panel>("ServerManagerPane"),
                LauncherNavPage.Accounts => this.FindControl<Panel>("AccountsPane"),
                LauncherNavPage.CreateProfile => this.FindControl<Panel>("CreateProfilePane"),
                LauncherNavPage.CreateServer => this.FindControl<Panel>("CreateServerPane"),
                _ => null
            };
            if (pane != null)
                UiFx.FadeInNow(pane, 240, 4);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsRightSidebarOpen))
        {
            AnimateSidebar(_subscribedVm!.IsRightSidebarOpen);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsAboutOpen))
        {
            if (_subscribedVm!.IsAboutOpen)
            {
                var card = this.FindControl<Border>("AboutModalCard");
                if (card != null)
                    UiFx.FadeInNow(card, 240, 6);
            }
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsJavaInstallerOpen))
        {
            if (_subscribedVm!.IsJavaInstallerOpen)
            {
                var card = this.FindControl<Border>("JavaInstallerCard");
                if (card != null)
                    UiFx.FadeInNow(card, 240, 6);
            }
        }
    }

    private async void AnimateSidebar(bool open)
    {
        var sidebar = this.FindControl<Border>("InstanceDetailsSidebar");
        if (sidebar == null) return;

        const double width = 300;
        const int ms = 240;

        // TranslateTransform (with its own X transition) drives the slide.
        var translate = sidebar.RenderTransform as TranslateTransform;
        if (translate == null)
        {
            translate = new TranslateTransform();
            sidebar.RenderTransform = translate;
        }

        if (open)
        {
            // Panel is reopening - clear any pending close so a stale delay can't hide it.
            _sidebarClosePending = false;
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

            // Slide out to the right and fade.
            translate.X = width;
            sidebar.Opacity = 0;

            // Track this close request. If it is superseded by a newer close or a
            // reopen while we wait, the panel must not be hidden afterwards.
            _sidebarClosePending = true;
            _sidebarCloseGeneration++;
            var thisClose = _sidebarCloseGeneration;

            await Task.Delay(ms);

            // Only hide if this is still the latest close AND the panel wasn't reopened.
            if (_sidebarClosePending && _sidebarCloseGeneration == thisClose)
            {
                sidebar.IsVisible = false;
                _sidebarClosePending = false;
            }
        }
    }
    private bool _sidebarClosePending;
    private int _sidebarCloseGeneration;

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

    private void OnInstanceNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            e.Handled = true;
            tb.Focus();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => tb.SelectAll());
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
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null)
        {
            e.Handled = true;
            return;
        }

        if (sender is Control { DataContext: Models.ModrinthProject p } && DataContext is MainWindowViewModel vm)
            vm.ModpacksBrowser.OpenProjectPageCommand.Execute(p);
    }

    private void OnModpackButtonTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
    }

    private bool _modpacksScrollAttached;

    private void OnModpacksListAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_modpacksScrollAttached) return;
        if (sender is ListBox list)
        {
            list.AddHandler(ScrollViewer.ScrollChangedEvent, OnModpacksScrollChanged);
            _modpacksScrollAttached = true;
        }
    }

    private void OnModpacksScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer sv) return;
        if (DataContext is not MainWindowViewModel vm) return;
        if (sv.Extent.Height <= 0 || sv.Viewport.Height <= 0) return;
        if (sv.Extent.Height - sv.Offset.Y - sv.Viewport.Height < 450)
            _ = vm.ModpacksBrowser.LoadMoreAsync();
    }

    private Border? _draggedCard;
    private InstanceModel? _draggedInstance;
    private Point _dragStartPos;
    private Point _dragCardOffset;
    private bool _isDragging;
    private bool _suppressNextTap;

    // Ghost overlay elements
    private Border? _ghostBorder;
    private Panel? _dragOverlay;

    private void OnInstanceCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && sender is Border border && border.DataContext is InstanceModel instance)
        {
            _draggedCard = border;
            _draggedInstance = instance;
            _dragStartPos = e.GetPosition(this);
            _dragCardOffset = e.GetPosition(border);
            _isDragging = false;
        }
    }

    private void OnInstanceCardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedCard == null || _draggedInstance == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ResetDrag();
            return;
        }

        var currentPos = e.GetPosition(this);
        var diff = currentPos - _dragStartPos;

        if (!_isDragging && (Math.Abs(diff.X) > 8 || Math.Abs(diff.Y) > 8))
        {
            _isDragging = true;

            // Find the drag overlay panel in XAML (x:Name="DragOverlayPanel")
            _dragOverlay = this.FindControl<Panel>("DragOverlayPanel");

            if (_dragOverlay != null)
            {
                // Create ghost: a simple semi-transparent copy
                var ghost = new Border
                {
                    Width = _draggedCard.Bounds.Width,
                    Height = _draggedCard.Bounds.Height,
                    CornerRadius = _draggedCard.CornerRadius,
                    Background = _draggedCard.Background,
                    BorderBrush = _draggedCard.BorderBrush,
                    BorderThickness = _draggedCard.BorderThickness,
                    Opacity = 0.75,
                    IsHitTestVisible = false,
                    ZIndex = 9999,
                    RenderTransform = new ScaleTransform(1.04, 1.04),
                };
                _ghostBorder = ghost;
                _dragOverlay.Children.Add(ghost);
            }

            // Collapse the real card so WrapPanel reflows without losing capture
            _draggedCard.Opacity = 0;
            _draggedCard.Width = 0;
            _draggedCard.Height = 0;
            _draggedCard.Margin = new Thickness(0);
            e.Pointer.Capture(_draggedCard);
        }

        if (_isDragging && _ghostBorder != null && _dragOverlay != null)
        {
            // Position ghost to follow cursor, offset by where user initially clicked within the card
            var ghostX = currentPos.X - _dragCardOffset.X;
            var ghostY = currentPos.Y - _dragCardOffset.Y;
            Canvas.SetLeft(_ghostBorder, ghostX);
            Canvas.SetTop(_ghostBorder, ghostY);
        }
    }

    private void OnInstanceCardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedCard == null || _draggedInstance == null)
        {
            ResetDrag();
            return;
        }

        if (_isDragging)
        {
            _suppressNextTap = true;
            try { e.Pointer.Capture(null); } catch { }

            var movedId = _draggedInstance.Id;
            var dropPos = e.GetPosition(this);

            // Restore the original card visibility before hit testing
            _draggedCard.IsVisible = true;

            // Remove ghost first to avoid interfering with hit test
            if (_dragOverlay != null && _ghostBorder != null)
            {
                _dragOverlay.Children.Remove(_ghostBorder);
                _ghostBorder = null;
            }

            // Hit test with the original card hidden via opacity trick
            _draggedCard.Opacity = 0;
            var hit = this.InputHitTest(dropPos) as Visual;
            _draggedCard.Opacity = 1.0;

            var targetCard = hit?.FindAncestorOfType<Border>(includeSelf: true);
            while (targetCard != null && !targetCard.Classes.Contains("instanceCard"))
                targetCard = targetCard.FindAncestorOfType<Border>(includeSelf: false);

            string? targetId = (targetCard?.DataContext as InstanceModel)?.Id;
            bool dropAfter = false;
            if (targetCard != null)
            {
                var relPos = e.GetPosition(targetCard);
                dropAfter = relPos.X > targetCard.Bounds.Width / 2;
            }

            ResetDrag();

            if (!string.IsNullOrEmpty(targetId) && targetId != movedId && DataContext is MainWindowViewModel vm)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try { vm.ReorderInstance(movedId, targetId, dropAfter); }
                    catch (Exception ex) { LauncherLog.Error("Failed to reorder instance", ex); }
                }, Avalonia.Threading.DispatcherPriority.Background);
            }

            e.Handled = true;
            return;
        }

        ResetDrag();
    }

    private void OnInstanceCardPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ResetDrag();
    }

    private void ResetDrag()
    {
        // Remove ghost overlay
        if (_dragOverlay != null && _ghostBorder != null)
        {
            try { _dragOverlay.Children.Remove(_ghostBorder); } catch { }
            _ghostBorder = null;
        }
        _dragOverlay = null;

        if (_draggedCard != null)
        {
            _draggedCard.IsVisible = true;
            _draggedCard.Opacity = 1.0;
            _draggedCard.Width = 150;
            _draggedCard.Height = 150;
            _draggedCard.Margin = new Thickness(2, 2, 10, 10);
            _draggedCard.RenderTransform = null;
        }
        _draggedCard = null;
        _draggedInstance = null;
        _isDragging = false;
    }

    private void OnInstanceCardTapped(object? sender, TappedEventArgs e)
    {
        if (_suppressNextTap)
        {
            _suppressNextTap = false;
            e.Handled = true;
            return;
        }

        if (sender is Control { DataContext: Models.InstanceModel instance } && DataContext is MainWindowViewModel vm)
            vm.SelectInstanceCommand.Execute(instance);
    }

    private void OnInstanceCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: Models.InstanceModel instance } && DataContext is MainWindowViewModel vm)
        {
            vm.SelectInstanceCommand.Execute(instance);
            vm.NavigateToEditInstance(instance, "Overview");
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
