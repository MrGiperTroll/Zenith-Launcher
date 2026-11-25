using Avalonia;
using Avalonia.Controls;

namespace ZenithLauncher.UI.Animations;

/// <summary>
/// Compatibility entry point delegating UI entrance transitions to the render-thread ZenithMotion engine.
/// </summary>
public static class UiFx
{
    public static void FadeIn(Visual visual, int ms = 275, double slideY = 0, double startScale = 0.96)
    {
        if (visual is Control control)
            ZenithMotion.AnimateEntrance(control, offsetX: 0, offsetY: slideY, startScale: startScale, durationMs: ms);
    }

    public static void FadeInNow(Visual visual, int ms = 275, double slideY = 0, double startScale = 0.96)
    {
        if (visual is Control control)
            ZenithMotion.AnimateEntrance(control, offsetX: 0, offsetY: slideY, startScale: startScale, durationMs: ms);
    }

    public static void MicroTabFade(Visual visual)
    {
        if (visual is Control control)
            ZenithMotion.TransitionTab(control);
    }
}
