using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CustomMcLauncher.Services;

/// <summary>Small helpers for smooth UI transitions (fade / slide).</summary>
public static class UiFx
{
    /// <summary>
    /// Fades a visual in (optionally sliding up) once it attaches to the visual
    /// tree. Safe to call from a window constructor right after InitializeComponent.
    /// Balanced 180ms ease-out slide &amp; fade (8px offset) eliminates molasses delay.
    /// </summary>
    public static void FadeIn(Visual visual, int ms = 180, double slideY = 8)
    {
        if (visual == null) return;

        void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            visual.AttachedToVisualTree -= OnAttached;
            _ = RunAsync(visual, CreateTranslate(visual, slideY), slideY, ms);
        }

        visual.AttachedToVisualTree += OnAttached;
    }

    /// <summary>
    /// Starts the fade immediately - for visuals that are already rendered
    /// (e.g. switching main view pages or live dialogs). Must be called on the UI thread.
    /// </summary>
    public static void FadeInNow(Visual visual, int ms = 180, double slideY = 8)
    {
        if (visual == null || !Dispatcher.UIThread.CheckAccess()) return;
        _ = RunAsync(visual, CreateTranslate(visual, slideY), slideY, ms);
    }

    /// <summary>
    /// Snappy micro-transition (80ms, 4px) for rapid internal tab switching with zero perceived latency.
    /// </summary>
    public static void MicroTabFade(Visual visual)
    {
        FadeInNow(visual, 80, 4);
    }

    private static TranslateTransform? CreateTranslate(Visual visual, double slideY)
    {
        if (Math.Abs(slideY) < 0.5 || visual is not Control control) return null;

        var translate = new TranslateTransform { Y = slideY };
        control.RenderTransform = translate;
        return translate;
    }

    private static async Task RunAsync(Visual visual, TranslateTransform? translate, double slideY, int ms)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            while (sw.ElapsedMilliseconds < ms)
            {
                await Task.Delay(16);
                var t = Math.Min(1.0, sw.ElapsedMilliseconds / (double)ms);
                // easeOutCubic - fast start, gentle landing
                var eased = 1 - Math.Pow(1 - t, 3);

                visual.Opacity = eased;
                if (translate != null)
                    translate.Y = slideY * (1 - eased);
            }
        }
        catch
        {
            // Control gone mid-animation (window closed) - nothing to do.
        }
        finally
        {
            visual.Opacity = 1;
            if (translate != null) translate.Y = 0;
        }
    }
}
