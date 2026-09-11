using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public class StepToBrushConverter : IValueConverter
{
    public static readonly StepToBrushConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isActive = value is bool b && b;
        if (isActive)
        {
            if (Avalonia.Application.Current?.Resources.TryGetResource("AccentBrush", Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush ab)
                return ab;
            return new SolidColorBrush(Color.Parse("#10B981"));
        }
        if (Avalonia.Application.Current?.Resources.TryGetResource("BorderStrongBrush", Avalonia.Styling.ThemeVariant.Default, out var res2) == true && res2 is ISolidColorBrush bb)
            return bb;
        return new SolidColorBrush(Color.Parse("#1C2438"));
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StepToForegroundConverter : IValueConverter
{
    public static readonly StepToForegroundConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isActive = value is bool b && b;
        if (isActive)
        {
            if (Avalonia.Application.Current?.Resources.TryGetResource("OnAccentBrush", Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush ob)
                return ob;
            return new SolidColorBrush(Color.Parse("#FFFFFF"));
        }
        return new SolidColorBrush(Color.Parse("#5A667E"));
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StepToTextBrushConverter : IValueConverter
{
    public static readonly StepToTextBrushConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isActive = value is bool b && b;
        if (isActive)
        {
            if (Avalonia.Application.Current?.Resources.TryGetResource("TextPrimaryBrush", Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush tb)
                return tb;
            return new SolidColorBrush(Color.Parse("#FFFFFF"));
        }
        if (Avalonia.Application.Current?.Resources.TryGetResource("TextMutedBrush", Avalonia.Styling.ThemeVariant.Default, out var res2) == true && res2 is ISolidColorBrush mb)
            return mb;
        return new SolidColorBrush(Color.Parse("#78859E"));
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToArrowConverter : IValueConverter
{
    public static readonly BoolToArrowConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isExpanded = value is bool b && b;
        return isExpanded ? "▼" : "▶";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToErrorBrushConverter : IValueConverter
{
    public static readonly BoolToErrorBrushConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isError = value is bool b && b;
        if (isError)
        {
            if (Avalonia.Application.Current?.Resources.TryGetResource("DangerBrush", Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush db)
                return db;
            return new SolidColorBrush(Color.Parse("#EF4444"));
        }
        if (Avalonia.Application.Current?.Resources.TryGetResource("BorderStrongBrush", Avalonia.Styling.ThemeVariant.Default, out var res2) == true && res2 is ISolidColorBrush bb)
            return bb;
        return new SolidColorBrush(Color.Parse("#1C2438"));
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class EqualityToBrushConverter : IMultiValueConverter
{
    public static readonly EqualityToBrushConverter Instance = new();
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is string a && values[1] is string b && string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            if (Avalonia.Application.Current?.Resources.TryGetResource("AccentBrush", Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush ab)
                return ab;
            return new SolidColorBrush(Color.Parse("#10B981"));
        }
        if (Avalonia.Application.Current?.Resources.TryGetResource("FieldBrush", Avalonia.Styling.ThemeVariant.Default, out var res2) == true && res2 is ISolidColorBrush fb)
            return fb;
        return new SolidColorBrush(Color.Parse("#121724"));
    }
}

public class EqualityToForegroundConverter : IMultiValueConverter
{
    public static readonly EqualityToForegroundConverter Instance = new();
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is string a && values[1] is string b && string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            if (Avalonia.Application.Current?.Resources.TryGetResource("OnAccentBrush", Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush ob)
                return ob;
            return new SolidColorBrush(Color.Parse("#041E13"));
        }
        if (Avalonia.Application.Current?.Resources.TryGetResource("TextSecondaryBrush", Avalonia.Styling.ThemeVariant.Default, out var res2) == true && res2 is ISolidColorBrush tb)
            return tb;
        return new SolidColorBrush(Color.Parse("#C5CBD6"));
    }
}

/// <summary>
/// Returns the official loader logo bitmap (Assets/Loaders) for a loader name,
/// or null when no official asset exists.
/// </summary>
public class LoaderOfficialIconConverter : IValueConverter
{
    public static readonly LoaderOfficialIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => CustomMcLauncher.Services.LoaderIconService.GetIcon(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>True when the loader has NO official PNG logo (shows the vector fallback).</summary>
public class LoaderOfficialIconMissingConverter : IValueConverter
{
    public static readonly LoaderOfficialIconMissingConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => CustomMcLauncher.Services.LoaderIconService.GetIcon(value as string) == null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>
/// Official-style mod loader logos as layered vector art (24x24 grid).
/// ConverterParameter selects the layer ("1".."3"); unused layers return null so
/// the extra Path elements in XAML simply render nothing. Used as fallback when
/// the official PNG logo is unavailable.
/// </summary>
public class LoaderLogoGeometryConverter : IValueConverter
{
    public static readonly LoaderLogoGeometryConverter Instance = new();
    private static readonly Dictionary<string, StreamGeometry?> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name) return null;
        var layer = parameter as string ?? "1";
        var key = name.ToLowerInvariant() + "#" + layer;
        if (!Cache.TryGetValue(key, out var geo))
        {
            var data = name.ToLowerInvariant() switch
            {
                // Minecraft grass block — isometric cube: green top + two dirt faces
                "vanilla" => layer switch
                {
                    "1" => "M12 2.2 L21.3 6.9 L12 11.6 L2.7 6.9 Z",
                    "2" => "M2.7 6.9 L12 11.6 V21.8 L2.7 17.1 Z",
                    "3" => "M21.3 6.9 L12 11.6 V21.8 L21.3 17.1 Z",
                    _ => ""
                },
                // FabricMC — cream diamond ring with solid diamond core
                "fabric" => layer switch
                {
                    "1" => "F0 M12 1.5 L22.5 12 L12 22.5 L1.5 12 Z M12 5.6 L18.4 12 L12 18.4 L5.6 12 Z",
                    "2" => "M12 9.2 L14.8 12 L12 14.8 L9.2 12 Z",
                    _ => ""
                },
                // Forge — steel anvil with base plate
                "forge" => layer switch
                {
                    "1" => "F0 M1.5 5 H22.5 V7 C19.8 8.9 16.6 9.85 13.6 10.1 V12.4 C13.6 14 14.8 15.25 16.6 15.7 L18.4 16.1 V18.3 H5.6 V16.1 L7.4 15.7 C9.2 15.25 10.4 14 10.4 12.4 V10.1 C7.4 9.85 4.2 8.9 1.5 7 Z M6.6 19.4 H17.4 L18.6 21.2 H5.4 Z",
                    _ => ""
                },
                // NeoForge — orange anvil with bolt hole
                "neoforge" => layer switch
                {
                    "1" => "F0 M1.5 5 H22.5 V7 C19.8 8.9 16.6 9.85 13.6 10.1 V12.4 C13.6 14 14.8 15.25 16.6 15.7 L18.4 16.1 V18.3 H5.6 V16.1 L7.4 15.7 C9.2 15.25 10.4 14 10.4 12.4 V10.1 C7.4 9.85 4.2 8.9 1.5 7 Z M6.6 19.4 H17.4 L18.6 21.2 H5.4 Z M12 11.55 A1.05 1.05 0 1 0 12 13.65 A1.05 1.05 0 1 0 12 11.55 Z",
                    _ => ""
                },
                // Quilt — patchwork squares, accent corner
                "quilt" => layer switch
                {
                    "1" => "M3 3.6 H10.9 V11.5 H3 Z M13.1 3.6 H21 V11.5 H13.1 Z M3 13.1 H10.9 V21 H3 Z",
                    "2" => "M13.1 13.1 H21 V21 H13.1 Z",
                    _ => ""
                },
                // OptiFine — magnifying glass
                "optifine" => layer switch
                {
                    "1" => "F0 M10.2 3 A7.2 7.2 0 1 0 10.2 17.4 A7.2 7.2 0 1 0 10.2 3 Z M10.2 5.9 A4.3 4.3 0 1 1 10.2 14.5 A4.3 4.3 0 1 1 10.2 5.9 Z",
                    "2" => "M14.7 13.5 L20.5 19.3 A1.55 1.55 0 0 1 18.3 21.5 L12.5 15.7 Z",
                    _ => ""
                },
                _ => ""
            };
            geo = string.IsNullOrEmpty(data) ? null : StreamGeometry.Parse(data);
            Cache[key] = geo;
        }
        return geo;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class LoaderLogoBrushConverter : IValueConverter
{
    public static readonly LoaderLogoBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name) return null;
        var layer = parameter as string ?? "1";
        var color = (name.ToLowerInvariant(), layer) switch
        {
            // Grass block: top / left / right
            ("vanilla", "1") => "#7CBD50",
            ("vanilla", "2") => "#6B4A2B",
            ("vanilla", "3") => "#8A5F38",
            ("fabric", _) => "#EDE6D4",
            ("forge", _) => "#5D7FA3",
            ("neoforge", _) => "#F16436",
            // Quilt: base patches / accent patch
            ("quilt", "1") => "#3E87B5",
            ("quilt", _) => "#7FC4EA",
            ("optifine", _) => "#58A6E8",
            _ => "#9AA7BD"
        };
        return new SolidColorBrush(Color.Parse(color));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Shows a friendly label for blank filter entries ("" -> localized "All versions" / "All categories" via key in ConverterParameter).</summary>
public class BlankTextConverter : IValueConverter
{
    public static readonly BlankTextConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (!string.IsNullOrWhiteSpace(text)) return text;
        return parameter is string key ? L10n.T(key) : "…";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Pretty-prints Modrinth loader slugs ("fabric" -> "Fabric", "" -> localized "Any loader").</summary>
public class LoaderDisplayConverter : IValueConverter
{
    public static readonly LoaderDisplayConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return L10n.T("mp_any_loader");
        return s.ToLowerInvariant() switch
        {
            "neoforge" or "neoforged" => "NeoForge",
            _ => char.ToUpperInvariant(s[0]) + s[1..]
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Maps internal sort option names ("Relevance"/"Downloads"/...) to localized labels.</summary>
public class SortDisplayConverter : IValueConverter
{
    public static readonly SortDisplayConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return "";
        return s switch
        {
            "Relevance" => L10n.T("sort_relevance"),
            "Downloads" => L10n.T("sort_downloads"),
            "Newest" => L10n.T("sort_newest"),
            "Recently Updated" => L10n.T("sort_updated"),
            _ => s
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value ?? string.Empty;
}

/// <summary>Formats one argument through a localized template: string.Format(L10n.T(parameter), value).</summary>
public class L10nFormatConverter : IValueConverter
{
    public static readonly L10nFormatConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var template = parameter is string k ? L10n.T(k) : parameter as string ?? "";
        try { return string.Format(culture, template, value ?? ""); }
        catch { return template; }
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Localizes FileList panel headers ("Mods"/"Resource Packs"/"Shader Packs"/"Worlds"/"Data Packs"/"Screenshots").</summary>
public class FileListTitleConverter : IValueConverter
{
    public static readonly FileListTitleConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return "";
        return s switch
        {
            "Mods" => L10n.T("tab_mods"),
            "Resource Packs" => L10n.T("tab_resourcepacks"),
            "Shader Packs" => L10n.T("tab_shaders"),
            "Worlds" => L10n.T("tab_worlds"),
            "Data Packs" => L10n.T("tab_datapacks"),
            "Screenshots" => L10n.T("fl_title_screenshots"),
            _ => s
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Localizes the Java mode combo ("Recommended"/"System"/"Custom" stay canonical internally).</summary>
public class JavaModeDisplayConverter : IValueConverter
{
    public static readonly JavaModeDisplayConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return "";
        return s switch
        {
            "Recommended" => L10n.T("java_mode_recommended"),
            "System" => L10n.T("java_mode_system"),
            "Custom" => L10n.T("java_mode_custom"),
            _ => s
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Localizes the Launch Behavior combo ("KeepOpen"/"Hide"/"Close").</summary>
public class LaunchBehaviorDisplayConverter : IValueConverter
{
    public static readonly LaunchBehaviorDisplayConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return "";
        return s switch
        {
            "Hide" => L10n.T("lb_minimize"),
            "Close" => L10n.T("lb_close"),
            _ => L10n.T("lb_keep_open")
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>
/// Localizes category slugs for display. The raw slug (adventure, magic, ...)
/// stays canonical - it is what gets sent to the Modrinth API.
/// </summary>
public class CategoryDisplayConverter : IValueConverter
{
    public static readonly CategoryDisplayConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return "";
        if (string.IsNullOrWhiteSpace(s)) return L10n.T("mp_all_categories");
        return s switch
        {
            "adventure" => L10n.T("cat_adventure"),
            "cursed" => L10n.T("cat_cursed"),
            "magic" => L10n.T("cat_magic"),
            "technology" => L10n.T("cat_technology"),
            "exploration" => L10n.T("cat_exploration"),
            "optimization" => L10n.T("cat_optimization"),
            "utility" => L10n.T("cat_utility"),
            "library" => L10n.T("cat_library"),
            "worldgen" => L10n.T("cat_worldgen"),
            "equipment" => L10n.T("cat_equipment"),
            "game-mechanics" => L10n.T("cat_game_mechanics"),
            "mobs" => L10n.T("cat_mobs"),
            _ => s
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value ?? string.Empty;
}

/// <summary>
/// Visual-tag display converter for resource pack / shader pack categories.
/// Resolution sizes like "16x" stay untouched.
/// </summary>
public class VisualTagDisplayConverter : IValueConverter
{
    public static readonly VisualTagDisplayConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return "";
        return s switch
        {
            "realistic" => L10n.T("cat_realistic"),
            "cartoon" => L10n.T("cat_cartoon"),
            "themed" => L10n.T("cat_themed"),
            "vanilla-like" => L10n.T("cat_vanilla_like"),
            "vanilla" => L10n.T("cat_vanilla_like"),
            "fantasy" => L10n.T("cat_fantasy"),
            "vibrant" => L10n.T("cat_vibrant"),
            "soft" => L10n.T("cat_soft"),
            "performance" => L10n.T("cat_performance"),
            _ => s
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value ?? string.Empty;
}

/// <summary>Localizes FileList sort options ("Enabled first"/"Name"/"Last modified").</summary>
public class FileSortDisplayConverter : IValueConverter
{
    public static readonly FileSortDisplayConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return "";
        return s switch
        {
            "Enabled first" => L10n.T("fl_sort_enabled"),
            "Name" => L10n.T("fl_sort_name"),
            "Last modified" => L10n.T("fl_sort_modified"),
            _ => s
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value ?? string.Empty;
}

/// <summary>Localizes the directory-structure combo (canonical values kept in config).</summary>
public class DirectoryModeDisplayConverter : IValueConverter
{
    public static readonly DirectoryModeDisplayConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return "";
        return s switch
        {
            "Separate directory for each instance" => L10n.T("dir_mode_separate"),
            "Family" => L10n.T("dir_mode_family"),
            "Do not use separate directories" => L10n.T("dir_mode_none"),
            _ => s
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value ?? string.Empty;
}

