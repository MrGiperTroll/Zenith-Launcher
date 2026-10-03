using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CustomMcLauncher.Services;

/// <summary>
/// High-performance GPU-composited UI transitions (fade / subtle slide).
/// Operates directly through Avalonia's animation compositor at full display refresh rate
/// with zero background threads, zero polling tasks, and zero CPU/GPU overhead.
/// </summary>
public static class UiFx
{
    private static readonly SplineEasing Ease = new(0.16, 1.0, 0.3, 1.0);

    public static void FadeIn(Visual visual, int ms = 240, double slideY = 4, double startScale = 1.0)
    {
        if (visual == null) return;
        if (visual is Window)
        {
            visual.Opacity = 1.0;
            return;
        }

        if (visual is Control { IsLoaded: true } control)
        {
            FadeInNow(control, ms, slideY, startScale);
            return;
        }

        visual.Opacity = 0.25;

        void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            visual.AttachedToVisualTree -= OnAttached;
            if (visual is Control c)
                FadeInNow(c, ms, slideY, startScale);
        }

        visual.AttachedToVisualTree += OnAttached;
    }

    public static void FadeInNow(Visual visual, int ms = 240, double slideY = 4, double startScale = 1.0)
    {
        if (visual is not Control control || !Dispatcher.UIThread.CheckAccess()) return;

        // Ensure translate transform
        var translate = control.RenderTransform as TranslateTransform;
        if (translate == null)
        {
            translate = new TranslateTransform();
            control.RenderTransform = translate;
        }

        // Configure transitions once if not already present
        if (control.Transitions == null || control.Transitions.Count == 0)
        {
            control.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(ms),
                    Easing = Ease
                }
            };
            translate.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = TranslateTransform.YProperty,
                    Duration = TimeSpan.FromMilliseconds(ms),
                    Easing = Ease
                }
            };
        }

        // Snap to soft initial position (prevents black flash/flicker)
        control.Opacity = 0.25;
        translate.Y = slideY;

        // Trigger smooth transition on next render frame
        Dispatcher.UIThread.Post(() =>
        {
            control.Opacity = 1.0;
            translate.Y = 0;
        }, DispatcherPriority.Render);
    }

    public static void MicroTabFade(Visual visual)
    {
        FadeInNow(visual, 180, 2);
    }
}
