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
using System.Linq;
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

        var ic = this.FindControl<ItemsControl>("InstancesItemsControl");
        if (ic != null)
        {
            DragDrop.SetAllowDrop(ic, true);
            ic.AddHandler(DragDrop.DragEnterEvent, OnInstancesDragEnter);
            ic.AddHandler(DragDrop.DragLeaveEvent, OnInstancesDragLeave);
            ic.AddHandler(DragDrop.DragOverEvent, OnInstancesDragOver);
            ic.AddHandler(DragDrop.DropEvent, OnInstancesDrop);
            ic.ContainerPrepared += OnInstanceContainerPrepared;
        }
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

    // --- Drag-and-Drop for instance reordering ---

    private const double DragThreshold = 7.0;
    private bool _isDragging;
    private bool _dragPending;
    private Point _dragStartPoint;
    private Border? _dragSourceBorder;
    private PointerPressedEventArgs? _dragPressedArgs;

    private void OnInstanceContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is ContentPresenter { Child: Border border })
        {
            border.PointerPressed += OnInstancePointerPressed;
            border.PointerMoved += OnInstancePointerMoved;
            border.PointerReleased += OnInstancePointerReleased;
        }
    }

    private Border? FindInstanceBorderAtPoint(ItemsControl ic, Point position)
    {
        var hit = ic.GetVisualAt(position);
        while (hit != null && hit != ic)
        {
            if (hit is Border border && border.Tag is Models.InstanceModel)
                return border;
            hit = hit.GetVisualParent();
        }
        return null;
    }

    private void OnInstancePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            _isDragging = false;
            _dragPending = true;
            _dragStartPoint = e.GetPosition(border);
            _dragSourceBorder = border;
            _dragPressedArgs = e;
            e.Pointer.Capture(border);
        }
    }

    private async void OnInstancePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragPending || _dragSourceBorder == null || _isDragging) return;

        var pos = e.GetPosition(_dragSourceBorder);
        var dx = pos.X - _dragStartPoint.X;
        var dy = pos.Y - _dragStartPoint.Y;
        if (dx * dx + dy * dy < DragThreshold * DragThreshold) return;

        if (_dragSourceBorder.Tag is Models.InstanceModel instance && _dragPressedArgs != null)
        {
            _isDragging = true;
            _dragPending = false;
            e.Pointer.Capture(null);
            _dragSourceBorder = null;

            var dragData = new DataTransfer();
            dragData.Add(DataTransferItem.CreateText(instance.Id));
            await DragDrop.DoDragDropAsync(_dragPressedArgs, dragData, DragDropEffects.Move);
            _dragPressedArgs = null;
        }
    }

    private void OnInstancePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragSourceBorder != null)
            e.Pointer.Capture(null);
        _dragPending = false;
        _isDragging = false;
        _dragSourceBorder = null;
        _dragPressedArgs = null;
    }

    private void OnInstancesDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is ItemsControl ic)
        {
            var border = FindInstanceBorderAtPoint(ic, e.GetPosition(ic));
            if (border != null) border.Opacity = 0.6;
        }
        e.DragEffects = DragDropEffects.Move;
    }

    private void OnInstancesDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is ItemsControl ic)
        {
            var border = FindInstanceBorderAtPoint(ic, e.GetPosition(ic));
            if (border != null) border.Opacity = 1.0;
        }
    }

    private void OnInstancesDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Move;
    }

    private void OnInstancesDrop(object? sender, DragEventArgs e)
    {
        if (sender is ItemsControl ic && DataContext is MainWindowViewModel vm)
        {
            var pos = e.GetPosition(ic);
            var targetBorder = FindInstanceBorderAtPoint(ic, pos);
            if (targetBorder?.Tag is Models.InstanceModel targetInstance)
            {
                targetBorder.Opacity = 1.0;

                var draggedId = e.DataTransfer.TryGetText();
                if (!string.IsNullOrEmpty(draggedId) && draggedId != targetInstance.Id)
                {
                    var dropAfter = pos.X > targetBorder.Bounds.Left + targetBorder.Bounds.Width / 2
                                 || pos.Y > targetBorder.Bounds.Top + targetBorder.Bounds.Height / 2;
                    vm.ReorderInstance(draggedId, targetInstance.Id, dropAfter);
                    e.DragEffects = DragDropEffects.Move;
                    return;
                }
            }
            e.DragEffects = DragDropEffects.None;
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
