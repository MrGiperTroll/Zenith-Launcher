using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CustomMcLauncher.Services;

/// <summary>Small helpers for smooth UI transitions (fade / slide / scale).</summary>
public static class UiFx
{
    /// <summary>
    /// Fades a visual in (sliding up and scaling smoothly from 0.995) once it attaches to the visual
    /// tree. Safe to call from a window constructor right after InitializeComponent.
    /// Synchronously initializes Opacity = 0 to prevent single-frame pop-in.
    /// </summary>
    public static void FadeIn(Visual visual, int ms = 180, double slideY = 6, double startScale = 0.995)
    {
        if (visual == null) return;
        if (visual is Window)
        {
            visual.Opacity = 1.0;
            return;
        }

        if (visual is Control { IsLoaded: true })
        {
            FadeInNow(visual, ms, slideY, startScale);
            return;
        }

        visual.Opacity = 0;

        void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            visual.AttachedToVisualTree -= OnAttached;
            visual.Opacity = 0;
            var transforms = CreateTransforms(visual, slideY, startScale);
            _ = RunAsync(visual, transforms, slideY, startScale, ms);
        }

        visual.AttachedToVisualTree += OnAttached;
    }

    /// <summary>
    /// Starts the transition immediately for visuals that are already in the tree (page navigation).
    /// Synchronously sets Opacity = 0 and initial transforms on the UI thread before kicking off async interpolation,
    /// eliminating single-frame pop-in.
    /// </summary>
    public static void FadeInNow(Visual visual, int ms = 180, double slideY = 6, double startScale = 0.995)
    {
        if (visual == null || !Dispatcher.UIThread.CheckAccess()) return;
        visual.Opacity = 0;
        var transforms = CreateTransforms(visual, slideY, startScale);
        _ = RunAsync(visual, transforms, slideY, startScale, ms);
    }

    /// <summary>
    /// Snappy micro-transition for rapid internal tab switching with zero perceived latency.
    /// </summary>
    public static void MicroTabFade(Visual visual)
    {
        FadeInNow(visual, 90, 3, 0.998);
    }

    private static (TranslateTransform? Translate, ScaleTransform? Scale) CreateTransforms(Visual visual, double slideY, double startScale)
    {
        if (visual is not Control control) return (null, null);

        var group = new TransformGroup();
        ScaleTransform? scale = null;
        if (Math.Abs(startScale - 1.0) > 0.0001)
        {
            scale = new ScaleTransform(startScale, startScale);
            group.Children.Add(scale);
        }

        TranslateTransform? translate = null;
        if (Math.Abs(slideY) >= 0.5)
        {
            translate = new TranslateTransform(0, slideY);
            group.Children.Add(translate);
        }

        control.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        control.RenderTransform = group;
        return (translate, scale);
    }

    private static async Task RunAsync(Visual visual, (TranslateTransform? Translate, ScaleTransform? Scale) transforms, double slideY, double startScale, int ms)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            while (sw.ElapsedMilliseconds < ms)
            {
                await Task.Delay(14);
                var t = Math.Min(1.0, sw.ElapsedMilliseconds / (double)ms);
                // easeOutCubic: fast start, gentle organic landing
                var eased = 1.0 - Math.Pow(1.0 - t, 3);

                visual.Opacity = eased;
                if (transforms.Translate != null)
                    transforms.Translate.Y = slideY * (1.0 - eased);
                if (transforms.Scale != null)
                {
                    var curScale = startScale + (1.0 - startScale) * eased;
                    transforms.Scale.ScaleX = curScale;
                    transforms.Scale.ScaleY = curScale;
                }
            }
        }
        catch
        {
            // Control gone mid-animation - safe exit.
        }
        finally
        {
            visual.Opacity = 1.0;
            if (transforms.Translate != null) transforms.Translate.Y = 0;
            if (transforms.Scale != null)
            {
                transforms.Scale.ScaleX = 1.0;
                transforms.Scale.ScaleY = 1.0;
            }
        }
    }
}
