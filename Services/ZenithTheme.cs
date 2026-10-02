using System;
using Avalonia;
using Avalonia.Media;

namespace CustomMcLauncher.Services;

public static class ZenithTheme
{
    public const string DefaultAccent = "#3B82F6";

    public static string AccentHex { get; private set; } = DefaultAccent;

    public static void Apply(string? hex)
    {
        AccentHex = TryParse(hex) ? hex! : DefaultAccent;

        if (Application.Current is not { } app) return;
        var r = app.Resources;

        var accent = Color.Parse(AccentHex);
        var hover = Lighten(accent, 0.16f);
        var pressed = Darken(accent, 0.2f);

        r["AccentBrush"] = new SolidColorBrush(accent);
        r["AccentBrushHover"] = new SolidColorBrush(hover);
        r["AccentPressed"] = new SolidColorBrush(pressed);
        r["OnAccentBrush"] = new SolidColorBrush(Colors.White);
        r["AccentSoftBrush"] = new SolidColorBrush(Blend(accent, Color.Parse("#080A0F"), 0.16f));
        r["AccentDimBrush"] = new SolidColorBrush(Blend(accent, Color.Parse("#080A0F"), 0.10f));
        r["AccentBorderBrush"] = new SolidColorBrush(Blend(accent, Color.Parse("#1B2236"), 0.5f));
        r["AccentTranslucentBrush"] = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B));

        // Slider dynamic brush overrides
        r["SliderTrackFill"] = new SolidColorBrush(accent);
        r["SliderTrackFillPointerOver"] = new SolidColorBrush(hover);
        r["SliderTrackFillPressed"] = new SolidColorBrush(pressed);
        r["SliderThumbBackground"] = new SolidColorBrush(accent);
        r["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(hover);
        r["SliderThumbBackgroundPressed"] = new SolidColorBrush(pressed);

        // Gradients
        r["AccentGradientStart"] = hover;
        r["AccentGradientMid"] = accent;
        r["AccentGradientEnd"] = pressed;

        // Subtle logo dynamic gradient
        r["SubtleEmeraldBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(hover, 0),
                new GradientStop(pressed, 1)
            }
        };
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

    public static readonly ThemePreset[] Presets =
    {
        new("blue", "Classic Blue", "Классическая синяя", "#3B82F6"),
        new("green", "Emerald Green", "Стандартная зелёная", "#10B981"),
        new("purple", "Vivid Purple", "Фиолетовая", "#8B5CF6"),
        new("dark_purple", "Deep Purple", "Тёмно-фиолетовая", "#6D28D9"),
        new("sakura", "Sakura Pink", "Сакуровая", "#EC4899"),
        new("orange", "Sunset Orange", "Закатная оранжевая", "#F97316"),
        new("cyan", "Cyber Cyan", "Бирюзовая", "#06B6D4"),
        new("crimson", "Crimson Red", "Рубиновая", "#EF4444"),
        new("amber", "Amber Gold", "Янтарная", "#F59E0B"),
        new("deep_sky", "Deep Sky", "Лазурная", "#0284C7")
    };
}

public class ThemePreset
{
    public string Id { get; }
    public string NameEn { get; }
    public string NameRu { get; }
    public string HexColor { get; }
    public IBrush ColorBrush { get; }
    public string DisplayName => L10n.CurrentLanguage == "ru" ? NameRu : NameEn;

    public ThemePreset(string id, string nameEn, string nameRu, string hexColor)
    {
        Id = id;
        NameEn = nameEn;
        NameRu = nameRu;
        HexColor = hexColor;
        ColorBrush = new SolidColorBrush(Color.Parse(hexColor));
    }
}