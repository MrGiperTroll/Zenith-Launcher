using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public static class ContentBrowserConverters
{
    private sealed class ContentTypeDisplayConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new ContentTypeDisplayConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ContentType ct)
                return ct switch
                {
                    ContentType.Mod => L10n.T("tab_mods"),
                    ContentType.ResourcePack => L10n.T("tab_resourcepacks"),
                    ContentType.Shader => L10n.T("tab_shaders"),
                    ContentType.DataPack => L10n.T("tab_datapacks"),
                    _ => ct.DisplayName()
                };
            return value?.ToString() ?? "";
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    /// <summary>
    /// Localized chip label. The "All" reset chip gets its own key; Modrinth
    /// category slugs are translated for display while staying canonical
    /// (raw slug) for API queries.
    /// </summary>
    private sealed class TagDisplayConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new TagDisplayConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string s) return "";
            if (s == "All") return L10n.T("cb_tag_all");
            return s switch
            {
                "adventure" => L10n.T("cat_adventure"),
                "magic" => L10n.T("cat_magic"),
                "technology" => L10n.T("cat_technology"),
                "utility" => L10n.T("cat_utility"),
                "library" => L10n.T("cat_library"),
                "optimization" => L10n.T("cat_optimization"),
                "worldgen" => L10n.T("cat_worldgen"),
                "equipment" => L10n.T("cat_equipment"),
                "game-mechanics" => L10n.T("cat_game_mechanics"),
                "mobs" => L10n.T("cat_mobs"),
                "realistic" => L10n.T("cat_realistic"),
                "cartoon" => L10n.T("cat_cartoon"),
                "themed" => L10n.T("cat_themed"),
                "vanilla-like" => L10n.T("cat_vanilla_like"),
                "vanilla" => L10n.T("cat_vanilla_like"),
                "fantasy" => L10n.T("cat_fantasy"),
                "vibrant" => L10n.T("cat_vibrant"),
                "soft" => L10n.T("cat_soft"),
                "performance" => L10n.T("cat_performance"),
                "simplistic" => L10n.T("cat_simplistic"),
                "tweaks" => L10n.T("cat_tweaks"),
                _ => s
            };
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private sealed class SourceToBrushConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new SourceToBrushConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ContentSource src)
            {
                if (src == ContentSource.Modrinth)
                {
                    if (Avalonia.Application.Current?.Resources.TryGetResource("AccentBrush", Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush b)
                        return b;
                    return new SolidColorBrush(Color.Parse("#10B981"));
                }
                if (Avalonia.Application.Current?.Resources.TryGetResource("FieldBrush", Avalonia.Styling.ThemeVariant.Default, out var fb) == true && fb is ISolidColorBrush f)
                    return f;
                return new SolidColorBrush(Color.Parse("#2A2E3A"));
            }
            return new SolidColorBrush(Color.Parse("#2A2E3A"));
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private sealed class SourceToForegroundConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new SourceToForegroundConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ContentSource src)
            {
                if (src == ContentSource.Modrinth)
                {
                    if (Avalonia.Application.Current?.Resources.TryGetResource("OnAccentBrush", Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush ob)
                        return ob;
                    return new SolidColorBrush(Color.Parse("#FFFFFF"));
                }
                if (Avalonia.Application.Current?.Resources.TryGetResource("TextSecondaryBrush", Avalonia.Styling.ThemeVariant.Default, out var fb) == true && fb is ISolidColorBrush f)
                    return f;
                return new SolidColorBrush(Color.Parse("#9CA3AF"));
            }
            return new SolidColorBrush(Color.Parse("#9CA3AF"));
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private sealed class BoolToTagBackgroundConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new BoolToTagBackgroundConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isSelected = value is bool b && b;
            if (isSelected)
            {
                if (Avalonia.Application.Current?.Resources.TryGetResource("AccentBrush", Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush ab)
                    return ab;
                return new SolidColorBrush(Color.Parse("#10B981"));
            }
            if (Avalonia.Application.Current?.Resources.TryGetResource("FieldBrush", Avalonia.Styling.ThemeVariant.Default, out var fb) == true && fb is ISolidColorBrush f)
                return f;
            return new SolidColorBrush(Color.Parse("#2A2E3A"));
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private sealed class BoolToTagForegroundConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new BoolToTagForegroundConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isSelected = value is bool b && b;
            if (isSelected)
            {
                if (Avalonia.Application.Current?.Resources.TryGetResource("OnAccentBrush", Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush ob)
                    return ob;
                return new SolidColorBrush(Color.Parse("#FFFFFF"));
            }
            if (Avalonia.Application.Current?.Resources.TryGetResource("TextSecondaryBrush", Avalonia.Styling.ThemeVariant.Default, out var fb) == true && fb is ISolidColorBrush f)
                return f;
            return new SolidColorBrush(Color.Parse("#9CA3AF"));
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private sealed class BoolToTagBorderConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new BoolToTagBorderConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isSelected = value is bool b && b;
            if (isSelected)
            {
                if (Avalonia.Application.Current?.Resources.TryGetResource("AccentBrush", Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush ab)
                    return ab;
                return new SolidColorBrush(Color.Parse("#10B981"));
            }
            if (Avalonia.Application.Current?.Resources.TryGetResource("BorderEditorBrush", Avalonia.Styling.ThemeVariant.Default, out var fb) == true && fb is ISolidColorBrush f)
                return f;
            return new SolidColorBrush(Color.Parse("#374151"));
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private sealed class VersionTypeBgConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new VersionTypeBgConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                var key = s.ToLowerInvariant() switch
                {
                    "release" => "AccentSoftBrush",
                    "beta" => "WarningSoftBrush",
                    "alpha" => "DangerSoftBrush",
                    _ => "FieldBrush"
                };
                if (Avalonia.Application.Current?.Resources.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush b)
                    return b;
                return new SolidColorBrush(Color.Parse("#2A2E3A"));
            }
            return new SolidColorBrush(Color.Parse("#2A2E3A"));
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private sealed class VersionTypeFgConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new VersionTypeFgConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                var key = s.ToLowerInvariant() switch
                {
                    "release" => "AccentBrush",
                    "beta" => "WarningBrush",
                    "alpha" => "DangerBrush",
                    _ => "TextSecondaryBrush"
                };
                if (Avalonia.Application.Current?.Resources.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush b)
                    return b;
                return new SolidColorBrush(Color.Parse("#9CA3AF"));
            }
            return new SolidColorBrush(Color.Parse("#9CA3AF"));
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public static IValueConverter ContentTypeDisplay => ContentTypeDisplayConverter.Instance;
    public static IValueConverter TagDisplay => TagDisplayConverter.Instance;
    public static IValueConverter SourceToBrush => SourceToBrushConverter.Instance;
    public static IValueConverter SourceToForeground => SourceToForegroundConverter.Instance;
    public static IValueConverter TagBackground => BoolToTagBackgroundConverter.Instance;
    public static IValueConverter TagForeground => BoolToTagForegroundConverter.Instance;
    public static IValueConverter TagBorder => BoolToTagBorderConverter.Instance;
    public static IValueConverter VersionTypeBg => VersionTypeBgConverter.Instance;
    public static IValueConverter VersionTypeFg => VersionTypeFgConverter.Instance;

    /// <summary>
    /// MultiValue converter: returns true when two string values are equal.
    /// Used for green highlight: compares version row Id with SelectedVersionId.
    /// </summary>
    private sealed class VersionEqualsConverter : IMultiValueConverter
    {
        public static readonly IMultiValueConverter Instance = new VersionEqualsConverter();
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count == 2 && values[0] != null && values[1] != null)
                return string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.Ordinal);
            return false;
        }
    }

    /// <summary>
    /// MultiValue converter: returns AccentSoftBrush when two strings match, otherwise SurfaceBrush.
    /// Used for version row highlight.
    /// </summary>
    private sealed class VersionHighlightBgConverter : IMultiValueConverter
    {
        public static readonly IMultiValueConverter Instance = new VersionHighlightBgConverter();
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var isSelected = values.Count == 2 && values[0] != null && values[1] != null
                && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.Ordinal);
            var key = isSelected ? "AccentSoftBrush" : "SurfaceBrush";
            if (Avalonia.Application.Current?.Resources.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush b)
                return b;
            return new SolidColorBrush(isSelected ? Color.Parse("#0D2B1E") : Color.Parse("#0A0D15"));
        }
    }

    /// <summary>
    /// MultiValue converter: returns AccentBrush when two strings match, otherwise BorderCardSoftBrush.
    /// Used for version row border highlight.
    /// </summary>
    private sealed class VersionHighlightBorderConverter : IMultiValueConverter
    {
        public static readonly IMultiValueConverter Instance = new VersionHighlightBorderConverter();
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var isSelected = values.Count == 2 && values[0] != null && values[1] != null
                && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.Ordinal);
            var key = isSelected ? "AccentBrush" : "BorderCardSoftBrush";
            if (Avalonia.Application.Current?.Resources.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush b)
                return b;
            return new SolidColorBrush(isSelected ? Color.Parse("#10B981") : Color.Parse("#1E2530"));
        }
    }

    /// <summary>
    /// MultiValue converter: returns AccentBrush when two strings match, otherwise FieldBrush.
    /// Used for version chip highlight.
    /// </summary>
    private sealed class VersionChipHighlightConverter : IMultiValueConverter
    {
        public static readonly IMultiValueConverter Instance = new VersionChipHighlightConverter();
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var isSelected = values.Count == 2 && values[0] != null && values[1] != null
                && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.Ordinal);
            var key = isSelected ? "AccentBrush" : "FieldBrush";
            if (Avalonia.Application.Current?.Resources.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush b)
                return b;
            return new SolidColorBrush(isSelected ? Color.Parse("#10B981") : Color.Parse("#1A1D28"));
        }
    }

    /// <summary>
    /// MultiValue converter: returns OnAccentBrush when two strings match, otherwise TextSecondaryBrush.
    /// Used for version chip text color.
    /// </summary>
    private sealed class VersionChipFgConverter : IMultiValueConverter
    {
        public static readonly IMultiValueConverter Instance = new VersionChipFgConverter();
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var isSelected = values.Count == 2 && values[0] != null && values[1] != null
                && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.Ordinal);
            var key = isSelected ? "OnAccentBrush" : "TextSecondaryBrush";
            if (Avalonia.Application.Current?.Resources.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var res) == true && res is ISolidColorBrush b)
                return b;
            return new SolidColorBrush(isSelected ? Color.Parse("#FFFFFF") : Color.Parse("#C5CBD6"));
        }
    }

    public static IMultiValueConverter VersionHighlightBg => VersionHighlightBgConverter.Instance;
    public static IMultiValueConverter VersionHighlightBorder => VersionHighlightBorderConverter.Instance;
    public static IMultiValueConverter VersionChipHighlight => VersionChipHighlightConverter.Instance;
    public static IMultiValueConverter VersionChipFg => VersionChipFgConverter.Instance;
    public static IMultiValueConverter VersionEquals => VersionEqualsConverter.Instance;
}
