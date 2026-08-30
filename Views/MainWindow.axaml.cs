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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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

        // Cancel any in-flight animation
        if (_sidebarAnim != null)
        {
            _sidebarAnimCts?.Cancel();
            _sidebarAnimCts?.Dispose();
        }
        _sidebarAnimCts = new CancellationTokenSource();
        _sidebarAnim = _sidebarAnimCts;

        sidebar.IsVisible = true;
        sidebar.IsHitTestVisible = open;

        const double width = 340;
        const int ms = 220;
        var translate = new TranslateTransform { X = open ? width : 0 };
        sidebar.RenderTransform = translate;
        sidebar.Opacity = open ? 0 : 1;

        var sw = Stopwatch.StartNew();
        try
        {
            while (sw.ElapsedMilliseconds < ms)
            {
                _sidebarAnimCts.Token.ThrowIfCancellationRequested();
                await Task.Delay(16, _sidebarAnimCts.Token);
                var t = Math.Min(1.0, sw.ElapsedMilliseconds / (double)ms);
                var eased = 1 - Math.Pow(1 - t, 3); // easeOutCubic

                var opacity = open ? eased : 1 - eased;
                var x = open ? width * (1 - eased) : width * eased;

                sidebar.Opacity = opacity;
                translate.X = x;
            }
        }
        catch (OperationCanceledException)
        {
            return; // superseded by a newer animation
        }
        finally
        {
            sidebar.Opacity = open ? 1 : 0;
            translate.X = open ? 0 : width;
            if (!open) sidebar.IsVisible = false;
        }
    }
    private CancellationTokenSource? _sidebarAnimCts;
    private CancellationTokenSource? _sidebarAnim;

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

    // --- Drag-and-Drop for instance reordering ---
    // Simple, reliable reorder: the dragged card stays in the collection and
    // the item is moved via Move(oldIndex, newIndex) as the cursor crosses other
    // cards. The model object is never recreated, so icons/names/paths persist.

    private const double DragThreshold = 7.0;
    private bool _dragPending;
    private bool _dragActive;
    private Point _dragStartPoint;
    private Models.InstanceModel? _draggedInstance;
    private Border? _dragSourceBorder;

    private Border? FindInstanceBorder(Visual hit)
    {
        var current = hit as Visual;
        while (current != null)
        {
            if (current is Border border && border.Tag is Models.InstanceModel)
                return border;
            current = current.GetVisualParent();
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var source = e.Source as Visual;
            if (source != null)
            {
                var border = FindInstanceBorder(source);
                if (border?.Tag is Models.InstanceModel instance)
                {
                    _dragPending = true;
                    _dragActive = false;
                    _dragStartPoint = e.GetPosition(this);
                    _draggedInstance = instance;
                    _dragSourceBorder = border;
                    e.Handled = true;
                    return;
                }
            }
        }

        _dragPending = false;
        _draggedInstance = null;
        _dragSourceBorder = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggedInstance == null || DataContext is not MainWindowViewModel vm) return;

        if (_dragPending && !_dragActive)
        {
            var pos = e.GetPosition(this);
            var dx = pos.X - _dragStartPoint.X;
            var dy = pos.Y - _dragStartPoint.Y;
            if (dx * dx + dy * dy < DragThreshold * DragThreshold) return;

            // Start the drag
            _dragPending = false;
            _dragActive = true;
            if (_dragSourceBorder != null)
                _dragSourceBorder.BorderBrush = new SolidColorBrush(Color.Parse("#10B981"));
            return;
        }

        if (!_dragActive) return;

        // Compute the target index from the cursor position over the items control
        var ic = this.FindControl<ItemsControl>("InstancesItemsControl");
        if (ic == null) return;

        var cursorInIc = e.GetPosition(ic);
        var targetIndex = GetDropIndex(ic, cursorInIc, vm.Instances);

        var currentIndex = vm.Instances.IndexOf(_draggedInstance);
        if (currentIndex < 0) return;

        // Move the item cleanly (preserves the model reference → icons intact)
        if (targetIndex >= 0 && targetIndex < vm.Instances.Count && targetIndex != currentIndex)
            vm.Instances.Move(currentIndex, targetIndex);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragActive && _draggedInstance != null && DataContext is MainWindowViewModel vm)
        {
            vm.PersistInstanceOrder();
            ClearDragHighlight();
        }

        _dragPending = false;
        _dragActive = false;
        _draggedInstance = null;
        _dragSourceBorder = null;
    }

    private void ClearDragHighlight()
    {
        if (_dragSourceBorder == null) return;
        if (_dragSourceBorder.Tag is InstanceModel inst)
        {
            var brush = inst.IsSelected
                ? (Color.TryParse("#10B981", out var sel) ? new SolidColorBrush(sel)
                    : new SolidColorBrush(Color.Parse("#10B981")))
                : new SolidColorBrush(Color.Parse("#1C2438"));
            _dragSourceBorder.BorderBrush = brush;
        }
        else
            _dragSourceBorder.BorderBrush = new SolidColorBrush(Color.Parse("#1C2438"));
    }

    private int GetDropIndex(ItemsControl ic, Point position, ObservableCollection<InstanceModel> instances)
    {
        // Determine which card the cursor is over and whether to insert before
        // or after it based on the cursor's position within that card.
        var hit = ic.GetVisualAt(position);
        if (hit != null)
        {
            var border = FindInstanceBorder(hit);
            if (border?.Tag is InstanceModel target)
            {
                var targetIndex = instances.IndexOf(target);
                if (targetIndex < 0) return instances.Count - 1;

                var bounds = border.Bounds;
                var centerX = bounds.X + bounds.Width / 2;
                var centerY = bounds.Y + bounds.Height / 2;

                // Cards flow left-to-right into rows. Insert before the target
                // when the cursor is in the card's upper-left half, otherwise after.
                var towardTop = position.Y < centerY;
                var sameHalfX = Math.Abs(position.X - centerX) <= bounds.Width / 2;
                bool before = sameHalfX && position.X < centerX ? position.Y < centerY : towardTop;

                return before ? targetIndex : targetIndex + 1;
            }
        }

        return instances.Count - 1;
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
