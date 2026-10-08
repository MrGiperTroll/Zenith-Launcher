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
    private Avalonia.Controls.Shapes.Rectangle? _ghostRect;
    private Avalonia.Threading.DispatcherTimer? _reorderTimer;
    private string? _pendingTargetId;
    private bool _pendingDropAfter;

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

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
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
            _draggedCard.Classes.Add("dragging");
            _draggedCard.IsHitTestVisible = false;
            
            var dragOverlay = this.FindControl<Canvas>("DragOverlayPanel");
            if (dragOverlay != null)
            {
                var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
                double dpi = topLevel?.RenderScaling ?? 1.0;
                double lScale = (DataContext as ViewModels.MainWindowViewModel)?.LauncherScale ?? 1.0;
                double scale = dpi * lScale;
                var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Math.Ceiling(_draggedCard.Bounds.Width * scale), (int)Math.Ceiling(_draggedCard.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
                bmp.Render(_draggedCard);
                
                _ghostRect = new Avalonia.Controls.Shapes.Rectangle
                {
                    Width = _draggedCard.Bounds.Width,
                    Height = _draggedCard.Bounds.Height,
                    Fill = new Avalonia.Media.ImageBrush(bmp) { Stretch = Avalonia.Media.Stretch.Fill },
                    Opacity = 0.90,
                    IsHitTestVisible = false,
                    ZIndex = 9999
                };
                dragOverlay.Children.Add(_ghostRect);
            }
            
            _draggedInstance.IsDragging = true;
            _draggedCard.Opacity = 0.4; // Hide the real card but keep it barely visible for bounding box
        }

        if (_isDragging && _ghostRect != null)
        {
            var dragOverlay = this.FindControl<Canvas>("DragOverlayPanel");
            if (dragOverlay != null) 
            {
                var ghostStart = e.GetPosition(dragOverlay);
                Canvas.SetLeft(_ghostRect, ghostStart.X - _dragCardOffset.X);
                Canvas.SetTop(_ghostRect, ghostStart.Y - _dragCardOffset.Y);
            }
            
            var hit = this.InputHitTest(currentPos) as Avalonia.Visual;
            var targetCard = hit?.FindAncestorOfType<Border>(includeSelf: true);
            while (targetCard != null && !targetCard.Classes.Contains("instanceCard"))
                targetCard = targetCard.FindAncestorOfType<Border>(includeSelf: false);
                
            string? targetId = (targetCard?.DataContext as Models.InstanceModel)?.Id;
            string movedId = _draggedInstance.Id;
            
            if (!string.IsNullOrEmpty(targetId) && targetId != movedId && DataContext is ViewModels.MainWindowViewModel vm)
            {
                bool dropAfter = false;
                if (targetCard != null)
                {
                    var relPos = e.GetPosition(targetCard);
                    dropAfter = relPos.X > targetCard.Bounds.Width / 2;
                }
                
                if (_pendingTargetId != targetId || _pendingDropAfter != dropAfter)
                {
                    _pendingTargetId = targetId;
                    _pendingDropAfter = dropAfter;
                    
                    if (_reorderTimer != null)
                    {
                        _reorderTimer.Stop();
                    }
                    else
                    {
                        _reorderTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                        _reorderTimer.Tick += (s, args) => 
                        {
                            _reorderTimer.Stop();
                            if (_pendingTargetId != null && DataContext is ViewModels.MainWindowViewModel currentVm)
                            {
                                try { currentVm.ReorderInstance(movedId, _pendingTargetId, _pendingDropAfter, save: false); }
                                catch (Exception ex) { CustomMcLauncher.Services.LauncherLog.Error("Real-time reorder failed", ex); }
                            }
                        };
                    }
                    _reorderTimer.Start();
                }
            }
            else
            {
                if (_reorderTimer != null)
                {
                    _reorderTimer.Stop();
                }
                _pendingTargetId = null;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_draggedCard == null || _draggedInstance == null)
        {
            ResetDrag();
            return;
        }

        if (_isDragging)
        {
            _suppressNextTap = true;
            if (DataContext is ViewModels.MainWindowViewModel vm)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try { vm.PersistInstanceOrder(); }
                    catch (Exception ex) { CustomMcLauncher.Services.LauncherLog.Error("Failed to persist order", ex); }
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
            e.Handled = true;
        }
        ResetDrag();
    }

    private void OnInstanceCardPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ResetDrag();
    }

    private void ResetDrag()
    {
        if (_reorderTimer != null)
        {
            _reorderTimer.Stop();
            _reorderTimer = null;
        }
        _pendingTargetId = null;

        var dragOverlay = this.FindControl<Canvas>("DragOverlayPanel");
        if (dragOverlay != null && _ghostRect != null)
        {
            dragOverlay.Children.Remove(_ghostRect);
        }
        _ghostRect = null;

        if (_draggedCard != null)
        {
            _draggedCard.Classes.Remove("dragging");
            _draggedCard.IsHitTestVisible = true;
            _draggedCard.ZIndex = 0;
            _draggedCard.Opacity = 1.0;
            
            var transform = _draggedCard.RenderTransform as Avalonia.Media.TranslateTransform;
            if (transform != null)
            {
                transform.X = 0;
                transform.Y = 0;
            }
        }
        
        if (_draggedInstance != null)
        {
            _draggedInstance.IsDragging = false;
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













