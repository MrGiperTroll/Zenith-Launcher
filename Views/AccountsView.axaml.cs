using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CustomMcLauncher.ViewModels;
using CustomMcLauncher.ViewModels.Pages;

namespace CustomMcLauncher.Views;

public partial class AccountsView : UserControl
{
    private Border? _draggedCard;
    private AccountRow? _draggedRow;
    private Point _dragStartPos;
    private Point _dragCardOffset;
    private bool _isDragging;
    private Avalonia.Controls.Shapes.Rectangle? _ghostRect;
    private Avalonia.Threading.DispatcherTimer? _reorderTimer;
    private string? _pendingTargetId;
    private bool _pendingDropAfter;

    public AccountsView()
    {
        InitializeComponent();

        AddHandler(PointerPressedEvent, (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                var visual = e.Source as Visual;
                var delBtn = visual?.FindAncestorOfType<Button>(includeSelf: true);
                if (delBtn != null && delBtn.Classes.Contains("delAccBtn"))
                    return;

                var card = visual?.FindAncestorOfType<Border>(includeSelf: true);
                while (card != null && !card.Classes.Contains("accRowCard"))
                    card = card.FindAncestorOfType<Border>(includeSelf: false);

                if (card != null && card.DataContext is AccountRow row)
                {
                    _draggedCard = card;
                    _draggedRow = row;
                    _dragStartPos = e.GetPosition(this);
                    _dragCardOffset = e.GetPosition(card);
                    _isDragging = false;
                }
            }
        }, RoutingStrategies.Tunnel);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_draggedCard == null || _draggedRow == null) return;
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
            var cardWidth = Math.Max(100.0, _draggedCard.Bounds.Width > 0 ? _draggedCard.Bounds.Width : 200.0);
            var cardHeight = Math.Max(30.0, _draggedCard.Bounds.Height > 0 ? _draggedCard.Bounds.Height : 50.0);

            var dragOverlay = this.FindControl<Canvas>("DragOverlayPanel");
            if (dragOverlay != null)
            {
                var topLevel = TopLevel.GetTopLevel(this);
                double dpi = topLevel?.RenderScaling ?? 1.0;
                int bmpW = Math.Max(1, (int)Math.Ceiling(cardWidth * dpi));
                int bmpH = Math.Max(1, (int)Math.Ceiling(cardHeight * dpi));
                try
                {
                    var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(bmpW, bmpH), new Vector(96 * dpi, 96 * dpi));
                    bmp.Render(_draggedCard);

                    _ghostRect = new Avalonia.Controls.Shapes.Rectangle
                    {
                        Width = cardWidth,
                        Height = cardHeight,
                        Fill = new Avalonia.Media.ImageBrush(bmp) { Stretch = Avalonia.Media.Stretch.Fill },
                        Opacity = 0.9,
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
                    CustomMcLauncher.Services.LauncherLog.Error("Failed to create account drag ghost", ex);
                }
            }

            _draggedCard.Classes.Add("dragging");
            _draggedCard.IsHitTestVisible = false;
            _draggedRow.IsDragging = true;
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
            string? targetId = (targetCard?.DataContext as AccountRow)?.Model.Id;
            string movedId = _draggedRow.Model.Id;

            if (!string.IsNullOrEmpty(targetId) && targetId != movedId && DataContext is AccountsViewModel vm)
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
                        _reorderTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                        _reorderTimer.Tick += (s, args) =>
                        {
                            _reorderTimer.Stop();
                            if (_pendingTargetId != null && DataContext is AccountsViewModel currentVm)
                            {
                                try { currentVm.ReorderAccount(movedId, _pendingTargetId, _pendingDropAfter, save: false); }
                                catch (Exception ex) { CustomMcLauncher.Services.LauncherLog.Error("Account reorder failed", ex); }
                            }
                        };
                    }
                    _reorderTimer.Start();
                }
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragging && _draggedRow != null && _pendingTargetId != null && DataContext is AccountsViewModel vm)
        {
            vm.ReorderAccount(_draggedRow.Model.Id, _pendingTargetId, _pendingDropAfter, save: true);
        }
        ResetDrag();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ResetDrag();
    }

    private void ResetDrag()
    {
        _reorderTimer?.Stop();
        _pendingTargetId = null;
        if (_draggedCard != null)
        {
            _draggedCard.Classes.Remove("dragging");
            _draggedCard.IsHitTestVisible = true;
            _draggedCard.Opacity = 1.0;
            _draggedCard = null;
        }
        if (_draggedRow != null)
        {
            _draggedRow.IsDragging = false;
            _draggedRow = null;
        }
        if (_ghostRect != null)
        {
            var dragOverlay = this.FindControl<Canvas>("DragOverlayPanel");
            dragOverlay?.Children.Remove(_ghostRect);
            _ghostRect = null;
        }
        _isDragging = false;
    }

    private (Border? targetCard, bool dropAfter) FindTargetCardAndPlacement(Point currentPos)
    {
        var itemsCtrl = this.FindControl<ItemsControl>("AccountsItemsControl");
        if (itemsCtrl == null) return (null, false);

        var cards = new List<Border>();
        foreach (var border in itemsCtrl.GetVisualDescendants())
        {
            if (border is Border b && b.Classes.Contains("accRowCard") && b != _draggedCard)
            {
                cards.Add(b);
            }
        }

        if (cards.Count == 0) return (null, false);

        Border? closest = null;
        double minDistance = double.MaxValue;
        bool dropAfter = false;

        foreach (var card in cards)
        {
            var boundsInView = card.TransformToVisual(this);
            if (boundsInView == null) continue;

            var cardTopLeft = boundsInView.Value.Transform(new Point(0, 0));
            var cardBounds = new Rect(cardTopLeft.X, cardTopLeft.Y, card.Bounds.Width, card.Bounds.Height);

            double cardMidY = cardBounds.Y + cardBounds.Height / 2.0;
            double dist = Math.Abs(currentPos.Y - cardMidY);

            if (dist < minDistance)
            {
                minDistance = dist;
                closest = card;
                dropAfter = currentPos.Y > cardMidY;
            }
        }

        return (closest, dropAfter);
    }
}
