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

    private void AnimateSidebar(bool open)
    {
        var sidebar = this.FindControl<Border>("InstanceDetailsSidebar");
        if (sidebar == null) return;

        sidebar.IsVisible = true;
        sidebar.IsHitTestVisible = open;

        var startOpacity = open ? 0.0 : 1.0;
        var endOpacity = open ? 1.0 : 0.0;
        var startX = open ? 280.0 : 0.0;
        var endX = open ? 0.0 : 280.0;

        var transform = new TranslateTransform(startX, 0);
        sidebar.RenderTransform = transform;
        sidebar.Opacity = startOpacity;

        var sw = new Stopwatch();
        var duration = TimeSpan.FromMilliseconds(200);

        var timer = new System.Timers.Timer(16); // ~60fps
        timer.Elapsed += (_, _) =>
        {
            var t = Math.Min(sw.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 1.0);
            // Ease out cubic
            var ease = 1.0 - Math.Pow(1.0 - t, 3);

            var newOpacity = startOpacity + (endOpacity - startOpacity) * ease;
            var newX = startX + (endX - startX) * ease;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                sidebar.Opacity = newOpacity;
                sidebar.RenderTransform = new TranslateTransform(newX, 0);
            });

            if (t >= 1.0)
            {
                timer.Stop();
                timer.Dispose();
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!open) sidebar.IsVisible = false;
                });
            }
        };
        timer.AutoReset = false;
        sw.Start();
        timer.Start();
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
    // Visual DnD: dragged card follows the cursor as a semi-transparent
    // ghost overlay. Other cards shift in real-time via collection reorder.

    private const double DragThreshold = 7.0;
    private bool _dragPending;
    private bool _dragActive;
    private Point _dragStartPoint;
    private Point _dragOffsetInCard;
    private Models.InstanceModel? _draggedInstance;
    private Border? _dragGhost;

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
                    _dragOffsetInCard = e.GetPosition(border);
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

            // Start drag — hide original card, create ghost
            _dragPending = false;
            _dragActive = true;
            _draggedInstance.IsDragging = true;
            CreateDragGhost(e);
            return;
        }

        if (!_dragActive || _dragGhost == null) return;

        // Move ghost to follow cursor
        var cursorPos = e.GetPosition(this);
        Canvas.SetLeft(_dragGhost, cursorPos.X - _dragOffsetInCard.X);
        Canvas.SetTop(_dragGhost, cursorPos.Y - _dragOffsetInCard.Y);

        // Calculate target position in the collection
        var ic = this.FindControl<ItemsControl>("InstancesItemsControl");
        if (ic == null || DataContext is not MainWindowViewModel vm) return;

        var cursorInIc = e.GetPosition(ic);
        var targetIndex = GetDropIndex(ic, cursorInIc, vm.Instances);

        // Reorder collection to show real-time shifting
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
            _draggedInstance.IsDragging = false;
            vm.PersistInstanceOrder();
            RemoveDragGhost();
        }

        _dragPending = false;
        _dragActive = false;
        _draggedInstance = null;
    }

    private void CreateDragGhost(PointerEventArgs e)
    {
        if (_draggedInstance == null) return;

        var overlay = this.FindControl<Panel>("DragGhostOverlay");
        if (overlay == null) return;

        // Build a visual clone of the card
        var ghost = new Border
        {
            Width = 178,
            Height = 178,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.Parse("#0D111A")),
            BorderBrush = new SolidColorBrush(Color.Parse("#10B981")),
            BorderThickness = new Thickness(2),
            Opacity = 0.85,
            IsHitTestVisible = false,
            BoxShadow = new BoxShadows(BoxShadow.Parse("0 8 24 0 #40000000")),
            RenderTransform = new ScaleTransform(1.05, 1.05)
        };

        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

        // Icon
        var iconBorder = new Border
        {
            Width = 68, Height = 68,
            Background = new SolidColorBrush(Color.Parse("#1C2438")),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            ClipToBounds = true
        };

        if (_draggedInstance.IconBitmap != null)
        {
            var img = new Image
            {
                Source = _draggedInstance.IconBitmap,
                Stretch = Avalonia.Media.Stretch.UniformToFill,
                Width = 68, Height = 68
            };
            iconBorder.Child = img;
        }
        else
        {
            var iconText = new TextBlock
            {
                Text = _draggedInstance.Name.Length > 0 ? _draggedInstance.Name[..1].ToUpper() : "?",
                FontWeight = Avalonia.Media.FontWeight.Bold,
                FontSize = 24,
                Foreground = new SolidColorBrush(Color.Parse("#10B981")),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            iconBorder.Child = iconText;
        }
        stack.Children.Add(iconBorder);

        // Name
        var nameBlock = new TextBlock
        {
            Text = _draggedInstance.Name,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            MaxWidth = 150
        };
        stack.Children.Add(nameBlock);

        // Version
        var verBlock = new TextBlock
        {
            Text = _draggedInstance.DisplayVersion,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#8892A8")),
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        stack.Children.Add(verBlock);

        ghost.Child = stack;

        // Position at cursor
        var cursorPos = e.GetPosition(this);
        Canvas.SetLeft(ghost, cursorPos.X - _dragOffsetInCard.X);
        Canvas.SetTop(ghost, cursorPos.Y - _dragOffsetInCard.Y);

        overlay.Children.Add(ghost);
        _dragGhost = ghost;
    }

    private void RemoveDragGhost()
    {
        if (_dragGhost != null)
        {
            var overlay = this.FindControl<Panel>("DragGhostOverlay");
            overlay?.Children.Remove(_dragGhost);
            _dragGhost = null;
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

            var bounds = border.Bounds;
            var centerX = bounds.X + bounds.Width / 2;
            var centerY = bounds.Y + bounds.Height / 2;

            if (position.X > centerX || position.Y > centerY)
                return targetIndex;
            else
                return targetIndex;
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
