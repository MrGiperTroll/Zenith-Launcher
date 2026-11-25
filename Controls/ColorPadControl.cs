using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace ZenithLauncher.Controls;

/// <summary>
/// Stretchable, rounded HSV Saturation/Value pad synchronized cleanly with <see cref="HsvColor"/>.
/// </summary>
public class ColorPadControl : Control
{
    public static readonly StyledProperty<HsvColor> HsvColorProperty =
        AvaloniaProperty.Register<ColorPadControl, HsvColor>(
            nameof(HsvColor),
            new HsvColor(1.0, 262.0, 0.0, 1.0),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<ColorPadControl, double>(nameof(CornerRadius), 10.0);

    private bool _isDragging;

    static ColorPadControl()
    {
        AffectsRender<ColorPadControl>(HsvColorProperty, CornerRadiusProperty);
    }

    public ColorPadControl()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        ClipToBounds = false;
    }

    public HsvColor HsvColor
    {
        get => GetValue(HsvColorProperty);
        set => SetValue(HsvColorProperty, value);
    }

    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var rect = new Rect(0, 0, w, h);
        var radius = Math.Clamp(CornerRadius, 0, Math.Min(w, h) / 2.0);
        var hsv = HsvColor;
        var pureHue = new HsvColor(1.0, hsv.H, 1.0, 1.0).ToRgb();

        using (context.PushClip(new RoundedRect(rect, radius)))
        {
            var hueBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Colors.White, 0.0),
                    new GradientStop(pureHue, 1.0)
                }
            };
            context.FillRectangle(hueBrush, rect);

            var shadeBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0),
                    new GradientStop(Color.FromArgb(255, 0, 0, 0), 1.0)
                }
            };
            context.FillRectangle(shadeBrush, rect);
        }

        // Thumb stays visible cleanly inside the pad perimeter without clipping at corners
        var cx = Math.Clamp(hsv.S, 0.0, 1.0) * w;
        var cy = (1.0 - Math.Clamp(hsv.V, 0.0, 1.0)) * h;
        var center = new Point(Math.Clamp(cx, 7, Math.Max(7, w - 7)), Math.Clamp(cy, 7, Math.Max(7, h - 7)));

        var outerShadowPen = new Pen(new SolidColorBrush(Color.FromArgb(145, 0, 0, 0)), 3.2);
        var ringPen = new Pen(Brushes.White, 2.0);
        context.DrawEllipse(null, outerShadowPen, center, 6.0, 6.0);
        context.DrawEllipse(null, ringPen, center, 6.0, 6.0);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _isDragging = true;
        e.Pointer.Capture(this);
        UpdateFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;
        UpdateFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDragging) return;
        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDragging = false;
    }

    private void UpdateFromPoint(Point pt)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var s = Math.Clamp(pt.X / w, 0.0, 1.0);
        var v = Math.Clamp(1.0 - (pt.Y / h), 0.0, 1.0);
        SetCurrentValue(HsvColorProperty, new HsvColor(1.0, HsvColor.H, s, v));
    }
}
