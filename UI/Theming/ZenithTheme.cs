using System;
using Avalonia;
using Avalonia.Media;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.UI.Theming;

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

        var bg = TryParse(cfg.BgColor) ? Color.Parse(cfg.BgColor) : Color.Parse("#080A0F");
        var sidebarBg = TryParse(cfg.SidebarBgColor) ? Color.Parse(cfg.SidebarBgColor) : Color.Parse("#06080D");
        var surface = TryParse(cfg.SurfaceColor) ? Color.Parse(cfg.SurfaceColor) : Color.Parse("#0A0D15");
        var card = TryParse(cfg.CardColor) ? Color.Parse(cfg.CardColor) : Color.Parse("#0D111A");
        var panel = TryParse(cfg.PanelColor) ? Color.Parse(cfg.PanelColor) : Color.Parse("#151C2C");
        var borderDark = TryParse(cfg.BorderDarkColor) ? Color.Parse(cfg.BorderDarkColor) : Color.Parse("#121622");
        var field = TryParse(cfg.FieldColor) ? Color.Parse(cfg.FieldColor) : Blend(panel, card, 0.5f);
        var hoverTint = TryParse(cfg.HoverColor) ? Color.Parse(cfg.HoverColor) : Blend(accent, card, 0.22f);
        var textPrimary = TryParse(cfg.TextPrimaryColor) ? Color.Parse(cfg.TextPrimaryColor) : Colors.White;
        var textSecondary = TryParse(cfg.TextSecondaryColor) ? Color.Parse(cfg.TextSecondaryColor) : Color.Parse("#C5CBD6");

        // Surfaces
        SetBrushColor(r, "BgBrush", bg);
        SetBrushColor(r, "SidebarBgBrush", sidebarBg);
        SetBrushColor(r, "SurfaceBrush", surface);
        SetBrushColor(r, "ContentSurfaceBrush", Blend(surface, bg, 0.4f));
        SetBrushColor(r, "CardBrush", card);
        SetBrushColor(r, "CardAltBrush", Blend(card, surface, 0.45f));
        SetBrushColor(r, "PanelBrush", panel);
        SetBrushColor(r, "PanelBrushTransparent", Color.FromArgb(0, panel.R, panel.G, panel.B));
        SetBrushColor(r, "FieldBrush", field);
        SetBrushColor(r, "InputFocusBrush", Blend(field, bg, 0.35f));
        SetBrushColor(r, "ChipBrush", Blend(card, panel, 0.5f));
        SetBrushColor(r, "IconChipBrush", Blend(card, bg, 0.5f));
        SetBrushColor(r, "LogSidebarBrush", Blend(bg, sidebarBg, 0.5f));

        // Hovers and Tabs
        SetBrushColor(r, "RowHoverBrush", hoverTint);
        SetBrushColor(r, "RowHoverBrushTransparent", Color.FromArgb(0, hoverTint.R, hoverTint.G, hoverTint.B));
        var tabActive = Blend(accent, sidebarBg, 0.22f);
        SetBrushColor(r, "TabActiveBrush", tabActive);
        SetBrushColor(r, "TabActiveBrushTransparent", Color.FromArgb(0, tabActive.R, tabActive.G, tabActive.B));
        SetBrushColor(r, "TabHoverBrush", Blend(accent, sidebarBg, 0.14f));
        SetBrushColor(r, "TabPressedBrush", Blend(pressed, sidebarBg, 0.28f));

        // Borders
        SetBrushColor(r, "BorderDarkBrush", borderDark);
        SetBrushColor(r, "BorderStrongBrush", Lighten(borderDark, 0.08f));
        SetBrushColor(r, "BorderCardSoftBrush", Lighten(borderDark, 0.05f));
        SetBrushColor(r, "BorderInputBrush", Lighten(borderDark, 0.07f));
        SetBrushColor(r, "BorderHoverBrush", Blend(accent, borderDark, 0.55f));
        SetBrushColor(r, "BorderEditorBrush", Lighten(borderDark, 0.09f));
        SetBrushColor(r, "BorderIconBrush", Lighten(borderDark, 0.06f));
        SetBrushColor(r, "BorderSoftBrush", Lighten(borderDark, 0.04f));

        // Accents
        SetBrushColor(r, "AccentBrush", accent);
        SetBrushColor(r, "AccentBrushHover", hover);
        SetBrushColor(r, "AccentPressed", pressed);
        SetBrushColor(r, "OnAccentBrush", Colors.White);
        SetBrushColor(r, "AccentSoftBrush", Blend(accent, bg, 0.22f));
        SetBrushColor(r, "AccentDimBrush", Blend(accent, bg, 0.12f));
        SetBrushColor(r, "AccentBorderBrush", Blend(accent, borderDark, 0.55f));
        SetBrushColor(r, "AccentTranslucentBrush", Color.FromArgb(0x22, accent.R, accent.G, accent.B));

        // Text
        SetBrushColor(r, "TextPrimaryBrush", textPrimary);
        SetBrushColor(r, "TextSecondaryBrush", textSecondary);
        SetBrushColor(r, "TextSoftBrush", Blend(textPrimary, textSecondary, 0.5f));
        SetBrushColor(r, "TextLabelBrush", Blend(textSecondary, bg, 0.8f));
        SetBrushColor(r, "TextMutedBrush", Blend(textSecondary, bg, 0.65f));
        SetBrushColor(r, "TextSubtleBrush", Blend(textSecondary, bg, 0.55f));
        SetBrushColor(r, "TextFaintBrush", Blend(textSecondary, bg, 0.45f));
        SetBrushColor(r, "TextPlaceholderBrush", Blend(textSecondary, bg, 0.40f));
        SetBrushColor(r, "TextCaptionBrush", Blend(textSecondary, bg, 0.30f));
        SetBrushColor(r, "TextDisabledBrush", Blend(textSecondary, bg, 0.35f));

        // Text selection dynamic highlights
        r["TextControlSelectionHighlightColor"] = accent;
        SetBrushColor(r, "TextSelectionBackground", accent);
        SetBrushColor(r, "TextControlSelectionHighlightBrush", accent);
        SetBrushColor(r, "TextControlSelectionHighlightColorBrush", accent);
        SetBrushColor(r, "TextSelectionHighlightColor", accent);
        SetBrushColor(r, "TextControlBorderBrushFocused", accent);
        SetBrushColor(r, "TextControlBackgroundFocused", field);
        SetBrushColor(r, "TextControlBackgroundPointerOver", surface);
        SetBrushColor(r, "TextControlBorderBrushPointerOver", Blend(accent, borderDark, 0.55f));

        // Slider dynamic brush overrides (filled = accent, unfilled = subtle, thumb = white)
        SetBrushColor(r, "SliderTrackFill", Lighten(borderDark, 0.10f));
        SetBrushColor(r, "SliderTrackFillPointerOver", Lighten(borderDark, 0.18f));
        SetBrushColor(r, "SliderTrackFillPressed", Lighten(borderDark, 0.10f));
        SetBrushColor(r, "SliderTrackValueFill", accent);
        SetBrushColor(r, "SliderTrackValueFillPointerOver", hover);
        SetBrushColor(r, "SliderTrackValueFillPressed", pressed);
        SetBrushColor(r, "SliderThumbBackground", Colors.White);
        SetBrushColor(r, "SliderThumbBackgroundPointerOver", Colors.White);
        SetBrushColor(r, "SliderThumbBackgroundPressed", Colors.White);

        // CheckBox dynamic overrides (follow surfaces + accent)
        SetBrushColor(r, "CheckBoxCheckBackgroundFillUnchecked", field);
        SetBrushColor(r, "CheckBoxCheckBackgroundStrokeUnchecked", Lighten(borderDark, 0.20f));
        SetBrushColor(r, "CheckBoxCheckBackgroundFillUncheckedPointerOver", field);
        SetBrushColor(r, "CheckBoxCheckBackgroundStrokeUncheckedPointerOver", accent);
        SetBrushColor(r, "CheckBoxCheckBackgroundFillUncheckedPressed", field);
        SetBrushColor(r, "CheckBoxCheckBackgroundStrokeUncheckedPressed", accent);
        SetBrushColor(r, "CheckBoxCheckBackgroundFillChecked", accent);
        SetBrushColor(r, "CheckBoxCheckBackgroundFillCheckedPointerOver", hover);
        SetBrushColor(r, "CheckBoxCheckBackgroundFillCheckedPressed", accent);
        SetBrushColor(r, "CheckBoxCheckGlyphForegroundChecked", Colors.White);
        SetBrushColor(r, "CheckBoxCheckGlyphForegroundCheckedPointerOver", Colors.White);
        SetBrushColor(r, "CheckBoxCheckGlyphForegroundCheckedPressed", Colors.White);

        // ToggleSwitch dynamic overrides (follow surfaces + accent, white knobs)
        SetBrushColor(r, "ToggleSwitchFillOff", field);
        SetBrushColor(r, "ToggleSwitchStrokeOff", Lighten(borderDark, 0.20f));
        SetBrushColor(r, "ToggleSwitchFillOffPointerOver", field);
        SetBrushColor(r, "ToggleSwitchStrokeOffPointerOver", accent);
        SetBrushColor(r, "ToggleSwitchFillOn", accent);
        SetBrushColor(r, "ToggleSwitchStrokeOn", accent);
        SetBrushColor(r, "ToggleSwitchFillOnPointerOver", hover);
        SetBrushColor(r, "ToggleSwitchStrokeOnPointerOver", hover);
        SetBrushColor(r, "ToggleSwitchKnobFillOff", Colors.White);
        SetBrushColor(r, "ToggleSwitchKnobFillOffPointerOver", Colors.White);
        SetBrushColor(r, "ToggleSwitchKnobFillOn", Colors.White);
        SetBrushColor(r, "ToggleSwitchKnobFillOnPointerOver", Colors.White);

        // Gradients
        r["AccentGradientStart"] = hover;
        r["AccentGradientMid"] = accent;
        r["AccentGradientEnd"] = pressed;

        // Subtle logo dynamic gradient
        if (r.TryGetValue("SubtleEmeraldBrush", out var seb) && seb is LinearGradientBrush lgb && lgb.GradientStops.Count >= 2)
        {
            lgb.GradientStops[0].Color = hover;
            lgb.GradientStops[1].Color = pressed;
        }
        else
        {
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
    }

    private static void SetBrushColor(Avalonia.Controls.IResourceDictionary r, string key, Color color)
    {
        if (r.TryGetValue(key, out var obj) && obj is SolidColorBrush brush)
        {
            brush.Color = color;
        }
        else
        {
            r[key] = new SolidColorBrush(color);
        }
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
        var field = Blend(accent, Color.Parse("#121724"), 0.10f);
        var hover = Blend(accent, Color.Parse("#1B2338"), 0.20f);

        return new Models.LauncherConfigData
        {
            AccentColor = accentHex,
            BgColor = ColorToHex(bg),
            SidebarBgColor = ColorToHex(sidebarBg),
            SurfaceColor = ColorToHex(surface),
            CardColor = ColorToHex(card),
            PanelColor = ColorToHex(panel),
            FieldColor = ColorToHex(field),
            HoverColor = ColorToHex(hover),
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
        new("sky", "Sky Blue", "Голубой", "#0284C7"),
        new("custom", "Custom", "Свой", "", true)
    };
}

public class ThemePreset
{
    public string Id { get; }
    public string NameEn { get; }
    public string NameRu { get; }
    public string HexColor { get; }
    public IBrush ColorBrush { get; }
    public bool IsCustomPicker { get; }
    public string DisplayName => L10n.CurrentLanguage == "ru" ? NameRu : NameEn;

    public ThemePreset(string id, string nameEn, string nameRu, string hexColor, bool isCustomPicker = false)
    {
        Id = id;
        NameEn = nameEn;
        NameRu = nameRu;
        HexColor = hexColor;
        IsCustomPicker = isCustomPicker;
        ColorBrush = isCustomPicker
            ? Brushes.Transparent
            : new SolidColorBrush(Color.Parse(hexColor));
    }
}