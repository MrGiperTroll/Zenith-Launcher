using System;
using Avalonia;
using Avalonia.Media;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.UI.Theming;

public static class ZenithTheme
{
    public const string DefaultAccent = "#8B5CF6";

    public static string AccentHex { get; private set; } = DefaultAccent;

    public static void ApplyFont(string? fontStyle, string? customFontPath = null)
    {
        if (Application.Current is not { } app) return;
        FontFamily family;
        if (!string.IsNullOrWhiteSpace(customFontPath) && System.IO.File.Exists(customFontPath))
        {
            try
            {
                var fullPath = System.IO.Path.GetFullPath(customFontPath).Replace('\\', '/');
                var fontName = System.IO.Path.GetFileNameWithoutExtension(customFontPath);
                family = new FontFamily($"file:///{fullPath}#{fontName}");
            }
            catch
            {
                family = FontFamily.Default;
            }
        }
        else
        {
            if (string.Equals(fontStyle, "Minecraft", StringComparison.OrdinalIgnoreCase))
            {
                family = new FontFamily("avares://Zenith Launcher/Assets/Fonts#Monocraft, avares://Zenith Launcher/Assets/Fonts#Minecraft, avares://Zenith%20Launcher/Assets/Fonts#Monocraft, avares://Zenith%20Launcher/Assets/Fonts#Minecraft, Monocraft, Minecraft, Segoe UI, sans-serif");
            }
            else
            {
                family = fontStyle switch
                {
                    "Serif" => new FontFamily("Georgia, Cambria, 'Times New Roman', serif"),
                    "Monospace" => new FontFamily("Consolas, 'Cascadia Code', 'Courier New', monospace"),
                    "System Default" or "System" => FontFamily.Default,
                    _ => new FontFamily("Segoe UI, Inter, -apple-system, BlinkMacSystemFont, sans-serif")
                };
            }
        }
        app.Resources["AppFontFamily"] = family;
    }

    public static void RestartLauncher()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

            if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exePath)
                });
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to restart launcher: {ex.Message}");
        }
        finally
        {
            Environment.Exit(0);
        }
    }

    public static void Apply(Models.LauncherConfigData cfg)
    {
        AccentHex = TryParse(cfg.AccentColor) ? cfg.AccentColor! : DefaultAccent;

        if (Application.Current is not { } app) return;
        var r = app.Resources;

        ApplyFont(cfg.FontStyle, cfg.CustomFontPath);

        try
        {
            app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        }
        catch { }

        var accent = Color.Parse(AccentHex);
        var hover = Lighten(accent, 0.16f);
        var pressed = Darken(accent, 0.2f);

        var harmonized = GenerateHarmonizedPalette(AccentHex, "Dark");
        var bg = Color.Parse(harmonized.BgColor!);
        var sidebarBg = Color.Parse(harmonized.SidebarBgColor!);
        var surface = Color.Parse(harmonized.SurfaceColor!);
        var card = Color.Parse(harmonized.CardColor!);
        var panel = Color.Parse(harmonized.PanelColor!);
        var field = Color.Parse(harmonized.FieldColor!);
        var borderDark = Color.Parse(harmonized.BorderDarkColor!);
        var hoverTint = Color.Parse(harmonized.HoverColor!);
        var textPrimary = Color.Parse(harmonized.TextPrimaryColor!);
        var textSecondary = Color.Parse(harmonized.TextSecondaryColor!);

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

        // Borders (Neutral dark style)
        var borderStrong = Color.Parse("#2E3038");
        var borderCardSoft = Color.Parse("#26282E");
        var borderInput = Color.Parse("#282A30");
        var borderHover = Color.Parse("#3A3D46");
        var borderEditor = Color.Parse("#282A30");
        var borderIcon = Color.Parse("#26282E");
        var borderSoft = Color.Parse("#222428");

        SetBrushColor(r, "BorderDarkBrush", borderDark);
        SetBrushColor(r, "BorderStrongBrush", borderStrong);
        SetBrushColor(r, "BorderCardSoftBrush", borderCardSoft);
        SetBrushColor(r, "BorderInputBrush", borderInput);
        SetBrushColor(r, "BorderHoverBrush", borderHover);
        SetBrushColor(r, "BorderEditorBrush", borderEditor);
        SetBrushColor(r, "BorderIconBrush", borderIcon);
        SetBrushColor(r, "BorderSoftBrush", borderSoft);

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

        // Expander overrides
        SetBrushColor(r, "ExpanderBorderBrush", Color.Parse("#26282E"));
        SetBrushColor(r, "ExpanderBorderBrushPointerOver", Color.Parse("#26282E"));
        SetBrushColor(r, "ExpanderBorderBrushPressed", Color.Parse("#26282E"));
        SetBrushColor(r, "ExpanderHeaderBackground", field);
        SetBrushColor(r, "ExpanderHeaderBackgroundPointerOver", hoverTint);
        SetBrushColor(r, "ExpanderHeaderBackgroundPressed", field);
        SetBrushColor(r, "ExpanderHeaderBorderBrushPointerOver", Colors.Transparent);
        SetBrushColor(r, "ExpanderHeaderBorderBrushPressed", Colors.Transparent);
        SetBrushColor(r, "ExpanderBackground", field);
        SetBrushColor(r, "ExpanderBackgroundPointerOver", field);
        SetBrushColor(r, "ExpanderBackgroundPressed", field);

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

    public static Models.LauncherConfigData GenerateHarmonizedPalette(string accentHex, string themeMode = "Dark")
    {
        if (!TryParse(accentHex)) accentHex = DefaultAccent;

        // Neutral charcoal dark base: crisp, matte, perfectly neutral dark borders
        return new Models.LauncherConfigData
        {
            AccentColor = accentHex,
            SimpleAccentColor = accentHex,
            ThemeMode = "Dark",
            BgColor = "#131416",
            SidebarBgColor = "#101113",
            SurfaceColor = "#18191C",
            CardColor = "#18191C",
            PanelColor = "#181A1E",
            FieldColor = "#1B1C20",
            HoverColor = "#222429",
            BorderDarkColor = "#202227",
            TextPrimaryColor = "#FFFFFF",
            TextSecondaryColor = "#8E929B"
        };
    }

    public static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static readonly ThemePreset[] Presets =
    {
        new("zenith", "Zenith", "Zenith", "#8B5CF6"),
        new("cobalt", "Cobalt", "Кобальт", "#3B82F6"),
        new("forest", "Forest", "Лес", "#10B981"),
        new("mint", "Mint", "Мята", "#06B6D4"),
        new("bee", "Bee", "Пчела", "#EAB308"),
        new("parchment", "Parchment", "Пергамент", "#FB923C"),
        new("crimson", "Crimson", "Багрянец", "#E11D48"),
        new("rose", "Rose", "Роза", "#EC4899")
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
        ColorBrush = isCustomPicker ? Brushes.Transparent : new SolidColorBrush(Color.Parse(hexColor));
    }
}