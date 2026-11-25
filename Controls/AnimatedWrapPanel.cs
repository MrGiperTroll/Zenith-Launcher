using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Threading;
using ZenithLauncher.UI.Animations;

namespace ZenithLauncher.Controls;

public class AnimatedWrapPanel : Panel
{
    public static readonly StyledProperty<TimeSpan> AnimationDurationProperty =
        AvaloniaProperty.Register<AnimatedWrapPanel, TimeSpan>(nameof(AnimationDuration), TimeSpan.FromMilliseconds(220));

    public TimeSpan AnimationDuration
    {
        get => GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    private readonly Dictionary<Control, Rect> _previousBounds = new();
    private bool _isFirstArrange = true;

    public AnimatedWrapPanel()
    {
        AttachedToVisualTree += (_, _) => ZenithMotion.EnabledChanged += OnMotionEnabledChanged;
        DetachedFromVisualTree += (_, _) => ZenithMotion.EnabledChanged -= OnMotionEnabledChanged;
    }

    private void OnMotionEnabledChanged(bool enabled)
    {
        if (enabled) return;
        foreach (var child in Children)
        {
            if (child.RenderTransform is TranslateTransform t)
            {
                t.Transitions = null;
                t.X = 0;
                t.Y = 0;
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double currentX = 0;
        double currentY = 0;
        double maxRowHeight = 0;
        double maxWidth = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            child.Measure(availableSize);
            var desired = child.DesiredSize;

            if (currentX + desired.Width > availableSize.Width && currentX > 0)
            {
                currentX = 0;
                currentY += maxRowHeight;
                maxRowHeight = 0;
            }

            currentX += desired.Width;
            maxRowHeight = Math.Max(maxRowHeight, desired.Height);
            maxWidth = Math.Max(maxWidth, currentX);
        }

        return new Size(maxWidth, currentY + maxRowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double currentX = 0;
        double currentY = 0;
        double maxRowHeight = 0;
        bool animationsEnabled = ZenithMotion.IsEnabled;

        // Prune entries for removed cards so the map cannot grow unbounded.
        if (_previousBounds.Count > Children.Count)
        {
            foreach (var key in _previousBounds.Keys.Where(k => !Children.Contains(k)).ToList())
                _previousBounds.Remove(key);
        }

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            var desired = child.DesiredSize;

            if (currentX + desired.Width > finalSize.Width && currentX > 0)
            {
                currentX = 0;
                currentY += maxRowHeight;
                maxRowHeight = 0;
            }

            var finalRect = new Rect(currentX, currentY, desired.Width, desired.Height);

            if (!animationsEnabled)
            {
                if (child.RenderTransform is TranslateTransform existingTransform)
                {
                    existingTransform.Transitions = null;
                    existingTransform.X = 0;
                    existingTransform.Y = 0;
                }
            }
            else if (!_isFirstArrange && _previousBounds.TryGetValue(child, out var oldRect) && oldRect != finalRect && !child.Classes.Contains("dragging"))
            {
                var transform = child.RenderTransform as TranslateTransform;
                if (transform == null)
                {
                    transform = new TranslateTransform();
                    child.RenderTransform = transform;
                }

                var offsetX = oldRect.X - finalRect.X;
                var offsetY = oldRect.Y - finalRect.Y;

                transform.X = offsetX;
                transform.Y = offsetY;

                if (transform.Transitions == null)
                {
                    transform.Transitions = new Transitions
                    {
                        new DoubleTransition
                        {
                            Property = TranslateTransform.XProperty,
                            Duration = AnimationDuration,
                            Easing = new QuarticEaseOut()
                        },
                        new DoubleTransition
                        {
                            Property = TranslateTransform.YProperty,
                            Duration = AnimationDuration,
                            Easing = new QuarticEaseOut()
                        }
                    };
                }

                Dispatcher.UIThread.Post(() =>
                {
                    transform.X = 0;
                    transform.Y = 0;
                });
            }

            child.Arrange(finalRect);
            if (!child.Classes.Contains("dragging"))
                _previousBounds[child] = finalRect;

            currentX += desired.Width;
            maxRowHeight = Math.Max(maxRowHeight, desired.Height);
        }

        _isFirstArrange = false;
        return finalSize;
    }
}
