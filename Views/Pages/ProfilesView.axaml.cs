using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ZenithLauncher.Models;
using ZenithLauncher.Services;
using ZenithLauncher.UI.Animations;
using ZenithLauncher.ViewModels;

namespace ZenithLauncher.Views.Pages;

public partial class ProfilesView : UserControl
{
    private MainWindowViewModel? _subscribedVm;

    private Border? _draggedCard;
    private InstanceModel? _draggedInstance;
    private Point _dragStartPos;
    private Point _dragCardOffset;
    private bool _isDragging;
    public bool SuppressNextTap { get; set; }

    // Ghost overlay elements
    private Avalonia.Controls.Shapes.Rectangle? _ghostRect;
    private DispatcherTimer? _reorderTimer;
    private string? _pendingTargetId;
    private bool _pendingDropAfter;

    public ProfilesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
        _subscribedVm = null;

        if (DataContext is MainWindowViewModel vm)
        {
            _subscribedVm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
            var sidebar = this.FindControl<Border>("InstanceDetailsSidebar");
            if (sidebar != null)
            {
                ZenithMotion.SyncVisibilityImmediate(sidebar, vm.IsRightSidebarOpen);
            }
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsRightSidebarOpen) && _subscribedVm != null)
        {
            AnimateSidebar(_subscribedVm.IsRightSidebarOpen);
        }
    }

    private void AnimateSidebar(bool open)
    {
        var sidebar = this.FindControl<Border>("InstanceDetailsSidebar");
        if (sidebar == null) return;

        if (open)
            Controls.SnowCollider.NotifySubtreeShowing(sidebar);
        else
            Controls.SnowCollider.NotifySubtreeHiding(sidebar);

        ZenithMotion.AnimateSlidePanel(sidebar, open, slideX: 84, slideY: 0, enterMs: 285, exitMs: 190);
    }

    private void OnInstanceNameDoubleTapped(object? sender, TappedEventArgs e)
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
        if (DataContext is MainWindowViewModel vm)
            vm.OnInstanceNameLostFocus();
    }

    public void StartDrag(Border border, InstanceModel instance, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
                var topLevel = TopLevel.GetTopLevel(this);
                double dpi = topLevel?.RenderScaling ?? 1.0;
                double lScale = (DataContext as MainWindowViewModel)?.LauncherScale ?? 1.0;
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
                        Fill = new ImageBrush(bmp) { Stretch = Stretch.Fill },
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
                    LauncherLog.Error("Failed to create drag ghost", ex);
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

            var (targetCard, dropAfter) = FindTargetCardAndPlacement(currentPos);
            string? targetId = (targetCard?.DataContext as InstanceModel)?.Id;
            string movedId = _draggedInstance.Id;

            if (!string.IsNullOrEmpty(targetId) && targetId != movedId && DataContext is MainWindowViewModel vm)
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
                        _reorderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                        _reorderTimer.Tick += (s, args) =>
                        {
                            _reorderTimer?.Stop();
                            if (_pendingTargetId != null && DataContext is MainWindowViewModel currentVm)
                            {
                                try { currentVm.ReorderInstance(movedId, _pendingTargetId, _pendingDropAfter, save: false); }
                                catch (Exception ex) { LauncherLog.Error("Real-time reorder failed", ex); }
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
        var hit = this.InputHitTest(currentPos) as Visual;
        var directCard = hit?.FindAncestorOfType<Border>(includeSelf: true);
        while (directCard != null && !directCard.Classes.Contains("instanceCard"))
            directCard = directCard.FindAncestorOfType<Border>(includeSelf: false);

        if (directCard != null && directCard != _draggedCard)
        {
            var pointInCard = this.TranslatePoint(currentPos, directCard) ?? new Point(directCard.Bounds.Width / 2, 0);
            bool after = pointInCard.X > directCard.Bounds.Width / 2;
            return (directCard, after);
        }

        var itemsCtrl = this.FindControl<ItemsControl>("InstancesItemsControl");
        Controls.AnimatedWrapPanel? wrapPanel = itemsCtrl is { IsVisible: true }
            ? itemsCtrl.FindDescendantOfType<Controls.AnimatedWrapPanel>()
            : this.FindDescendantOfType<Controls.AnimatedWrapPanel>();
        if (wrapPanel == null) return (null, false);

        var pointerInWrap = this.TranslatePoint(currentPos, wrapPanel);
        if (!pointerInWrap.HasValue) return (null, false);

        var p = pointerInWrap.Value;
        var cards = new List<Border>();
        foreach (var visual in wrapPanel.GetVisualChildren())
        {
            if (visual is Border b && b.Classes.Contains("instanceCard") && b.DataContext is InstanceModel)
                cards.Add(b);
        }

        if (cards.Count == 0) return (null, false);

        var lastCard = cards.Last();
        if (p.Y > lastCard.Bounds.Bottom + 10 || (p.Y >= lastCard.Bounds.Top && p.X > lastCard.Bounds.Right))
        {
            return (lastCard, true);
        }

        var firstCard = cards.First();
        if (p.Y < firstCard.Bounds.Top - 10 || (p.Y <= firstCard.Bounds.Bottom && p.X < firstCard.Bounds.Left))
        {
            return (firstCard, false);
        }

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
            SuppressNextTap = true;
            if (DataContext is MainWindowViewModel vm)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try { vm.PersistInstanceOrder(); }
                    catch (Exception ex) { LauncherLog.Error("Failed to persist order", ex); }
                }, DispatcherPriority.Background);
            }
            e.Handled = true;
        }
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
            try
            {
                if (_ghostRect.Fill is ImageBrush ib)
                {
                    (ib.Source as Avalonia.Media.Imaging.Bitmap)?.Dispose();
                    ib.Source = null;
                }
                _ghostRect.Fill = null;
            }
            catch { }
        }
        _ghostRect = null;

        if (_draggedCard != null)
        {
            _draggedCard.Classes.Remove("dragging");
            _draggedCard.IsHitTestVisible = true;
            _draggedCard.ZIndex = 0;
            _draggedCard.Opacity = 1.0;

            var transform = _draggedCard.RenderTransform as TranslateTransform;
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
}
