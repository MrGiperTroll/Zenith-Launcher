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
    // Custom pointer-based DnD with real-time visual reordering.
    // Instead of system DnD (which shows a file-drag cursor), we track
    // pointer movement, temporarily reorder the Instances collection
    // so the WrapPanel reflows in real-time, and highlight the drop target.

    private const double DragThreshold = 7.0;
    private bool _dragPending;
    private bool _dragActive;
    private Point _dragStartPoint;
    private Models.InstanceModel? _draggedInstance;
    private Border? _dragHoverBorder;

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
                    _dragStartPoint = e.GetPosition(border);
                    _draggedInstance = instance;
                    e.Handled = true;
                    return;
                }
            }
        }

        _dragPending = false;
        _draggedInstance = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggedInstance == null) return;

        if (_dragPending && !_dragActive)
        {
            var pos = e.GetPosition(this);
            var dx = pos.X - _dragStartPoint.X;
            var dy = pos.Y - _dragStartPoint.Y;
            if (dx * dx + dy * dy < DragThreshold * DragThreshold) return;

            // Start drag
            _dragPending = false;
            _dragActive = true;
            return;
        }

        if (!_dragActive) return;

        // Calculate which position the cursor is over in the Instances collection
        var ic = this.FindControl<ItemsControl>("InstancesItemsControl");
        if (ic == null || DataContext is not MainWindowViewModel vm) return;

        var cursorInIc = e.GetPosition(ic);
        var targetIndex = GetDropIndex(ic, cursorInIc, vm.Instances);

        // If hovering over a different card, highlight it
        var hoverBorder = FindInstanceBorderAtPoint(ic, cursorInIc);
        if (hoverBorder != _dragHoverBorder)
        {
            ClearDropHighlight();
            _dragHoverBorder = hoverBorder;
            if (hoverBorder != null)
                hoverBorder.BorderBrush = new SolidColorBrush(Color.Parse("#10B981"));
        }

        // Temporarily reorder Instances to show real-time WrapPanel reflow
        var currentIndex = vm.Instances.IndexOf(_draggedInstance);
        if (targetIndex != currentIndex && targetIndex >= 0)
        {
            vm.Instances.RemoveAt(currentIndex);
            vm.Instances.Insert(targetIndex, _draggedInstance);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragActive && _draggedInstance != null && DataContext is MainWindowViewModel vm)
        {
            // Persist the final order
            vm.PersistInstanceOrder();

            ClearDropHighlight();
        }

        _dragPending = false;
        _dragActive = false;
        _draggedInstance = null;
        _dragHoverBorder = null;
    }

    private void ClearDropHighlight()
    {
        if (_dragHoverBorder != null)
        {
            // Restore the original BorderBrush from the selection converter
            if (_dragHoverBorder.Tag is InstanceModel inst)
            {
                if (inst.IsSelected)
                {
                    if (Avalonia.Application.Current?.Resources.TryGetResource("AccentBrush", ThemeVariant.Default, out var res) == true && res is ISolidColorBrush brush)
                        _dragHoverBorder.BorderBrush = new SolidColorBrush(brush.Color);
                    else
                        _dragHoverBorder.BorderBrush = new SolidColorBrush(Color.Parse("#10B981"));
                }
                else
                    _dragHoverBorder.BorderBrush = new SolidColorBrush(Color.Parse("#1C2438"));
            }
            else
                _dragHoverBorder.BorderBrush = new SolidColorBrush(Color.Parse("#1C2438"));
        }
    }

    private int GetDropIndex(ItemsControl ic, Point position, ObservableCollection<InstanceModel> instances)
    {
        var hit = ic.GetVisualAt(position);
        if (hit == null) return instances.Count - 1;

        var border = FindInstanceBorder(hit);
        if (border?.Tag is InstanceModel target)
        {
            var targetIndex = instances.IndexOf(target);
            if (targetIndex < 0) return instances.Count - 1;

            // Determine if dropping before or after based on cursor position
            var bounds = border.Bounds;
            var centerX = bounds.X + bounds.Width / 2;
            var centerY = bounds.Y + bounds.Height / 2;

            if (position.X > centerX || position.Y > centerY)
                return targetIndex; // drop after (at this position)
            else
                return targetIndex; // drop before this position
        }

        return instances.Count - 1;
    }

    private Border? FindInstanceBorderAtPoint(ItemsControl ic, Point position)
    {
        var hit = ic.GetVisualAt(position);
        if (hit != null) return FindInstanceBorder(hit);
        return null;
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
