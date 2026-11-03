using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CustomMcLauncher.Controls;

public class TimelineNodeControl : Control
{
    public static readonly StyledProperty<IBrush?> NodeBrushProperty =
        AvaloniaProperty.Register<TimelineNodeControl, IBrush?>(nameof(NodeBrush));

    public static readonly StyledProperty<bool> ShowTopLineProperty =
        AvaloniaProperty.Register<TimelineNodeControl, bool>(nameof(ShowTopLine), true);

    public static readonly StyledProperty<bool> ShowBottomLineProperty =
        AvaloniaProperty.Register<TimelineNodeControl, bool>(nameof(ShowBottomLine), true);

    static TimelineNodeControl()
    {
        AffectsRender<TimelineNodeControl>(NodeBrushProperty, ShowTopLineProperty, ShowBottomLineProperty);
    }

    public IBrush? NodeBrush
    {
        get => GetValue(NodeBrushProperty);
        set => SetValue(NodeBrushProperty, value);
    }

    public bool ShowTopLine
    {
        get => GetValue(ShowTopLineProperty);
        set => SetValue(ShowTopLineProperty, value);
    }

    public bool ShowBottomLine
    {
        get => GetValue(ShowBottomLineProperty);
        set => SetValue(ShowBottomLineProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var brush = NodeBrush;
        if (brush == null) return;

        double centerX = Math.Round(Bounds.Width / 2.0);
        double circleCenterY = 9.0;
        double circleRadius = 4.5;

        var dashStyle = new DashStyle(new double[] { 3, 3 }, 0);
        var pen = new Pen(brush, 2.0, dashStyle, PenLineCap.Flat, PenLineJoin.Miter);

        // 1. Top dashed line (from Y=0 to top of circle node)
        if (ShowTopLine)
        {
            context.DrawLine(pen, new Point(centerX, 0), new Point(centerX, circleCenterY - circleRadius));
        }

        // 2. Circle node strictly centered on the timeline axis
        context.DrawEllipse(brush, null, new Point(centerX, circleCenterY), circleRadius, circleRadius);

        // 3. Bottom dashed line (from bottom of circle node to bottom of row)
        if (ShowBottomLine && Bounds.Height > circleCenterY + circleRadius)
        {
            context.DrawLine(pen, new Point(centerX, circleCenterY + circleRadius), new Point(centerX, Bounds.Height));
        }
    }
}
