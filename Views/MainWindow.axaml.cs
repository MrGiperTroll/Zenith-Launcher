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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;
using CustomMcLauncher.UI.Animations;
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
        
        // RAM optimization: when the window is minimized or deactivated, collect GC generation and release unneeded working set
        PropertyChanged += (s, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized)
            {
                OptimizeMemory();
            }
        };
        Deactivated += (s, e) =>
        {
            OptimizeMemory();
        };
    }

    private static void OptimizeMemory()
    {
        Task.Run(() =>
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Aggressive, false, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, false, true);
            }
            catch { }
        });
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
            
            var cardWidth = Math.Max(150.0, _draggedCard.Bounds.Width > 0 ? _draggedCard.Bounds.Width : 150.0);
            var cardHeight = Math.Max(150.0, _draggedCard.Bounds.Height > 0 ? _draggedCard.Bounds.Height : 150.0);

            var dragOverlay = this.FindControl<Canvas>("DragOverlayPanel");
            if (dragOverlay != null)
            {
                var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
                double dpi = topLevel?.RenderScaling ?? 1.0;
                double lScale = (DataContext as ViewModels.MainWindowViewModel)?.LauncherScale ?? 1.0;
                if (lScale <= 0.1) lScale = 1.0;
                double scale = dpi * lScale;
                
                int bmpW = Math.Max(1, (int)Math.Ceiling(cardWidth * scale));
                int bmpH = Math.Max(1, (int)Math.Ceiling(cardHeight * scale));
                
                try
                {
                    var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(bmpW, bmpH), new Vector(96 * scale, 96 * scale));
                    bmp.Render(_draggedCard);

                    _ghostRect = new Avalonia.Controls.Shapes.Rectangle
                    {
                        Width = cardWidth,
                        Height = cardHeight,
                        Fill = new Avalonia.Media.ImageBrush(bmp) { Stretch = Avalonia.Media.Stretch.Fill },
                        Opacity = 0.92,
                        IsHitTestVisible = false,
                        ZIndex = 9999
                    };
                    dragOverlay.Children.Add(_ghostRect);

                    var ghostStart = e.GetPosition(dragOverlay);
                    Canvas.SetLeft(_ghostRect, ghostStart.X - _dragCardOffset.X);
                    Canvas.SetTop(_ghostRect, ghostStart.Y - _dragCardOffset.Y);
                }
                catch (Exception ex)
                {
                    CustomMcLauncher.Services.LauncherLog.Error("Failed to create drag ghost", ex);
                }
            }

            _draggedCard.Classes.Add("dragging");
            _draggedCard.IsHitTestVisible = false;
            _draggedInstance.IsDragging = true;
            _draggedCard.Opacity = 0.0;
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
            
            // Spatial card hit-testing and end-of-list detection
            var (targetCard, dropAfter) = FindTargetCardAndPlacement(currentPos);
            string? targetId = (targetCard?.DataContext as Models.InstanceModel)?.Id;
            string movedId = _draggedInstance.Id;
            
            if (!string.IsNullOrEmpty(targetId) && targetId != movedId && DataContext is ViewModels.MainWindowViewModel vm)
            {
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
                        _reorderTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
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

    private (Border? targetCard, bool dropAfter) FindTargetCardAndPlacement(Point currentPos)
    {
        // 1. First attempt: Direct input hit test under pointer
        var hit = this.InputHitTest(currentPos) as Avalonia.Visual;
        var directCard = hit?.FindAncestorOfType<Border>(includeSelf: true);
        while (directCard != null && !directCard.Classes.Contains("instanceCard"))
            directCard = directCard.FindAncestorOfType<Border>(includeSelf: false);

        if (directCard != null && directCard != _draggedCard)
        {
            var relPos = directCard.TranslatePoint(currentPos, this);
            // Check position relative to the target card
            var pointInCard = this.TranslatePoint(currentPos, directCard) ?? new Point(directCard.Bounds.Width / 2, 0);
            bool after = pointInCard.X > directCard.Bounds.Width / 2;
            return (directCard, after);
        }

        // 2. Spatial proximity fallback: find closest card or handle end-of-list empty space
        var itemsCtrl = this.FindControl<ItemsControl>("InstancesItemsControl");
        if (itemsCtrl == null) return (null, false);

        var wrapPanel = itemsCtrl.FindDescendantOfType<Controls.AnimatedWrapPanel>();
        if (wrapPanel == null) return (null, false);

        var pointerInWrap = this.TranslatePoint(currentPos, wrapPanel);
        if (!pointerInWrap.HasValue) return (null, false);

        var p = pointerInWrap.Value;
        var cards = new List<Border>();
        foreach (var visual in wrapPanel.GetVisualChildren())
        {
            if (visual is Border b && b.Classes.Contains("instanceCard") && b.DataContext is Models.InstanceModel)
                cards.Add(b);
        }

        if (cards.Count == 0) return (null, false);

        // Check if pointer is past the last card in the layout
        var lastCard = cards.Last();
        if (p.Y > lastCard.Bounds.Bottom + 10 || (p.Y >= lastCard.Bounds.Top && p.X > lastCard.Bounds.Right))
        {
            return (lastCard, true);
        }

        // Check if pointer is before the first card
        var firstCard = cards.First();
        if (p.Y < firstCard.Bounds.Top - 10 || (p.Y <= firstCard.Bounds.Bottom && p.X < firstCard.Bounds.Left))
        {
            return (firstCard, false);
        }

        // Find card with closest center point to pointer
        Border? closest = null;
        double minDist = double.MaxValue;
        bool dropAfterClosest = false;

        foreach (var card in cards)
        {
            if (card == _draggedCard) continue;
            var center = new Point(card.Bounds.X + card.Bounds.Width / 2, card.Bounds.Y + card.Bounds.Height / 2);
            double dist = Math.Pow(p.X - center.X, 2) + Math.Pow(p.Y - center.Y, 2);
            if (dist < minDist)
            {
                minDist = dist;
                closest = card;
                dropAfterClosest = p.X > center.X;
            }
        }

        return (closest, dropAfterClosest);
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
