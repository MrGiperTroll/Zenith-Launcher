using System;
using Avalonia;
using Avalonia.Media;

namespace CustomMcLauncher.Services;

public static class ZenithTheme
{
    public const string DefaultAccent = "#3B82F6";

    public static string AccentHex { get; private set; } = DefaultAccent;

    public static void Apply(Models.LauncherConfigData cfg)
    {
        AccentHex = TryParse(cfg.AccentColor) ? cfg.AccentColor! : DefaultAccent;

        if (Application.Current is not { } app) return;
        var r = app.Resources;

        var accent = Color.Parse(AccentHex);
        var hover = Lighten(accent, 0.16f);
        var pressed = Darken(accent, 0.2f);

        
        if (TryParse(cfg.BgColor)) r["BgBrush"] = new SolidColorBrush(Color.Parse(cfg.BgColor));
        if (TryParse(cfg.SurfaceColor)) r["SurfaceBrush"] = new SolidColorBrush(Color.Parse(cfg.SurfaceColor));
        if (TryParse(cfg.PanelColor)) r["PanelBrush"] = new SolidColorBrush(Color.Parse(cfg.PanelColor));
        if (TryParse(cfg.SidebarBgColor)) r["SidebarBgBrush"] = new SolidColorBrush(Color.Parse(cfg.SidebarBgColor));
        if (TryParse(cfg.CardColor)) r["CardBrush"] = new SolidColorBrush(Color.Parse(cfg.CardColor));
        if (TryParse(cfg.TextPrimaryColor)) r["TextPrimaryBrush"] = new SolidColorBrush(Color.Parse(cfg.TextPrimaryColor));
        if (TryParse(cfg.TextSecondaryColor)) r["TextSecondaryBrush"] = new SolidColorBrush(Color.Parse(cfg.TextSecondaryColor));
        if (TryParse(cfg.BorderDarkColor)) r["BorderDarkBrush"] = new SolidColorBrush(Color.Parse(cfg.BorderDarkColor));
        if (TryParse(cfg.PanelColor)) {
            // Need a transparent version of panel brush for sidebar
            var pc = Color.Parse(cfg.PanelColor);
            r["PanelBrushTransparent"] = new SolidColorBrush(Color.FromArgb(0, pc.R, pc.G, pc.B));
        }

        r["AccentBrush"] = new SolidColorBrush(accent);
        r["AccentBrushHover"] = new SolidColorBrush(hover);
        r["AccentPressed"] = new SolidColorBrush(pressed);
        r["OnAccentBrush"] = new SolidColorBrush(Colors.White);
        r["AccentSoftBrush"] = new SolidColorBrush(Blend(accent, Color.Parse("#080A0F"), 0.16f));
        r["AccentDimBrush"] = new SolidColorBrush(Blend(accent, Color.Parse("#080A0F"), 0.10f));
        r["AccentBorderBrush"] = new SolidColorBrush(Blend(accent, Color.Parse("#1B2236"), 0.5f));
        r["AccentTranslucentBrush"] = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B));

        // Text selection dynamic highlights
        r["TextControlSelectionHighlightColor"] = accent;
        r["TextSelectionBackground"] = new SolidColorBrush(accent);

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

    public static Models.LauncherConfigData GenerateHarmonizedPalette(string accentHex)
    {
        if (!TryParse(accentHex)) accentHex = DefaultAccent;
        var accent = Color.Parse(accentHex);

        // Harmonize dark surfaces with a subtle tint of the chosen accent color (Anti-AI-slop: clean, restrained, high-contrast)
        var bg = Blend(accent, Color.Parse("#080A0F"), 0.05f);
        var sidebarBg = Blend(accent, Color.Parse("#06080D"), 0.04f);
        var surface = Blend(accent, Color.Parse("#0D111A"), 0.08f);
        var card = Blend(accent, Color.Parse("#0D111A"), 0.09f);
        var panel = Blend(accent, Color.Parse("#121722"), 0.12f);
        var borderDark = Blend(accent, Color.Parse("#121622"), 0.16f);

        return new Models.LauncherConfigData
        {
            AccentColor = accentHex,
            BgColor = ColorToHex(bg),
            SidebarBgColor = ColorToHex(sidebarBg),
            SurfaceColor = ColorToHex(surface),
            CardColor = ColorToHex(card),
            PanelColor = ColorToHex(panel),
            BorderDarkColor = ColorToHex(borderDark),
            TextPrimaryColor = "#FFFFFF",
            TextSecondaryColor = "#C5CBD6"
        };
    }

    public static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static readonly ThemePreset[] Presets =
    {
        new("blue", "Blue", "Синий", "#3B82F6"),
        new("green", "Green", "Зелёный", "#10B981"),
        new("purple", "Purple", "Фиолетовый", "#8B5CF6"),
        new("dark_purple", "Dark Purple", "Тёмно-фиолетовый", "#6D28D9"),
        new("pink", "Pink", "Розовый", "#EC4899"),
        new("orange", "Orange", "Оранжевый", "#F97316"),
        new("cyan", "Cyan", "Бирюзовый", "#06B6D4"),
        new("red", "Red", "Красный", "#EF4444"),
        new("yellow", "Yellow", "Жёлтый", "#F59E0B"),
        new("sky", "Sky Blue", "Голубой", "#0284C7")
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