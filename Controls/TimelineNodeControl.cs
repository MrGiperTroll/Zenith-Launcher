using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ZenithLauncher.Controls;

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
        double circleRadius = 4.5;
        double circleCenterY = 9.0;

        var dashStyle = new DashStyle(new double[] { 3, 3 }, 0);
        var pen = new Pen(brush, 2.0, dashStyle, PenLineCap.Flat, PenLineJoin.Miter);

        // Always keep the dot at the very top of the entry with dashes flowing downward only
        context.DrawEllipse(brush, null, new Point(centerX, circleCenterY), circleRadius, circleRadius);

        double lineStartY = circleCenterY + circleRadius + 3.0;
        if (ShowBottomLine && Bounds.Height > lineStartY + 2.0)
        {
            context.DrawLine(pen, new Point(centerX, lineStartY), new Point(centerX, Bounds.Height));
        }
    }
}
