using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Threading;

namespace CustomMcLauncher.Controls;

public class AnimatedWrapPanel : Panel
{
    public static readonly StyledProperty<TimeSpan> AnimationDurationProperty =
        AvaloniaProperty.Register<AnimatedWrapPanel, TimeSpan>(nameof(AnimationDuration), TimeSpan.FromMilliseconds(250));

    public TimeSpan AnimationDuration
    {
        get => GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    private readonly Dictionary<Control, Rect> _previousBounds = new();
    private bool _isFirstArrange = true;

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

            if (!_isFirstArrange && _previousBounds.TryGetValue(child, out var oldRect) && oldRect != finalRect && !child.Classes.Contains("dragging"))
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
