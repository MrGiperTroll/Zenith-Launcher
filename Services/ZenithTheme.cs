using System;
using Avalonia;
using Avalonia.Media;

namespace CustomMcLauncher.Services;

public static class ZenithTheme
{
    public const string DefaultAccent = "#10B981";

    public static string AccentHex { get; private set; } = DefaultAccent;

    public static void Apply(string? hex)
    {
        AccentHex = TryParse(hex) ? hex! : DefaultAccent;

        if (Application.Current is not { } app) return;
        var r = app.Resources;

        var accent = Color.Parse(AccentHex);
        r["AccentBrush"] = new SolidColorBrush(accent);
        r["AccentBrushHover"] = new SolidColorBrush(Lighten(accent, 0.16f));
        r["AccentPressed"] = new SolidColorBrush(Darken(accent, 0.2f));
        r["OnAccentBrush"] = new SolidColorBrush(Color.Parse("#041E13"));
        r["AccentSoftBrush"] = new SolidColorBrush(Blend(accent, Color.Parse("#080A0F"), 0.16f));
        r["AccentDimBrush"] = new SolidColorBrush(Blend(accent, Color.Parse("#080A0F"), 0.10f));
        r["AccentBorderBrush"] = new SolidColorBrush(Blend(accent, Color.Parse("#1B2236"), 0.5f));
    }

    private static bool TryParse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { Color.Parse(hex); return true; }
        catch { return false; }
    }

    private static Color Lighten(Color c, float amount)
        => Color.FromRgb(
            (byte)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255),
            (byte)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255),
            (byte)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255));

    private static Color Darken(Color c, float amount)
        => Color.FromRgb(
            (byte)Math.Clamp(c.R * (1 - amount), 0, 255),
            (byte)Math.Clamp(c.G * (1 - amount), 0, 255),
            (byte)Math.Clamp(c.B * (1 - amount), 0, 255));

    private static Color Blend(Color fg, Color bg, float alpha)
        => Color.FromRgb(
            (byte)(fg.R * alpha + bg.R * (1 - alpha)),
            (byte)(fg.G * alpha + bg.G * (1 - alpha)),
            (byte)(fg.B * alpha + bg.B * (1 - alpha)));
}