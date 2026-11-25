using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ZenithLauncher.UI.Animations;

namespace ZenithLauncher.Controls;

/// <summary>
/// Wrapping layout panel for profile cards that smoothly animates card repositioning
/// when interface animations and PageAndCatalog motion are enabled.
/// </summary>
public class AnimatedWrapPanel : Panel
{
    public static readonly StyledProperty<TimeSpan> AnimationDurationProperty =
        AvaloniaProperty.Register<AnimatedWrapPanel, TimeSpan>(nameof(AnimationDuration), TimeSpan.FromMilliseconds(240));

    public TimeSpan AnimationDuration
    {
        get => GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    private static readonly Easing RepositionEasing = new CubicEaseOut();
    private readonly Dictionary<Control, Rect> _previousBounds = new();
    private bool _isFirstArrange = true;

    public AnimatedWrapPanel()
    {
        AttachedToVisualTree += (_, _) => ZenithMotion.MotionConfigChanged += OnMotionConfigChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            ZenithMotion.MotionConfigChanged -= OnMotionConfigChanged;
            _previousBounds.Clear();
            _isFirstArrange = true;
        };
    }

    private static bool AreCardAnimationsActive =>
        ZenithMotion.IsEnabled && ZenithMotion.IsCategoryEnabled(MotionCategory.PageAndCatalog);

    private void OnMotionConfigChanged()
    {
        if (AreCardAnimationsActive) return;

        foreach (var child in Children)
        {
            if (child.RenderTransform is TranslateTransform t)
            {
                t.Transitions = null;
                t.X = 0;
                t.Y = 0;
            }
        }
        _previousBounds.Clear();
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
        bool animationsEnabled = AreCardAnimationsActive;

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
            else if (!_isFirstArrange
                     && !child.Classes.Contains("dragging")
                     && _previousBounds.TryGetValue(child, out var oldRect))
            {
                double offsetX = oldRect.X - finalRect.X;
                double offsetY = oldRect.Y - finalRect.Y;

                if (Math.Abs(offsetX) > 0.5 || Math.Abs(offsetY) > 0.5)
                {
                    var transform = child.RenderTransform as TranslateTransform;
                    if (transform == null)
                    {
                        transform = new TranslateTransform();
                        child.RenderTransform = transform;
                    }

                    var savedTransitions = transform.Transitions;
                    transform.Transitions = null;
                    transform.X = offsetX;
                    transform.Y = offsetY;

                    var targetTransform = transform;
                    var dur = AnimationDuration;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!AreCardAnimationsActive)
                        {
                            targetTransform.Transitions = null;
                            targetTransform.X = 0;
                            targetTransform.Y = 0;
                            return;
                        }

                        targetTransform.Transitions = savedTransitions ?? new Transitions
                        {
                            new DoubleTransition
                            {
                                Property = TranslateTransform.XProperty,
                                Duration = dur,
                                Easing = RepositionEasing
                            },
                            new DoubleTransition
                            {
                                Property = TranslateTransform.YProperty,
                                Duration = dur,
                                Easing = RepositionEasing
                            }
                        };
                        targetTransform.X = 0;
                        targetTransform.Y = 0;
                    }, DispatcherPriority.Render);
                }
            }

            child.Arrange(finalRect);
            if (animationsEnabled && !child.Classes.Contains("dragging"))
                _previousBounds[child] = finalRect;

            currentX += desired.Width;
            maxRowHeight = Math.Max(maxRowHeight, desired.Height);
        }

        _isFirstArrange = false;
        return finalSize;
    }
}
