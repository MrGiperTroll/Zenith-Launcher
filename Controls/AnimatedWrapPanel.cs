using System;
using Avalonia;
using Avalonia.Controls;

namespace ZenithLauncher.Controls;

/// <summary>
/// Fast synchronous wrapping layout panel for profile cards (no card sliding or repositioning transitions).
/// </summary>
public class AnimatedWrapPanel : Panel
{
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

            child.Arrange(new Rect(currentX, currentY, desired.Width, desired.Height));

            currentX += desired.Width;
            maxRowHeight = Math.Max(maxRowHeight, desired.Height);
        }

        return finalSize;
    }
}
