using System;
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

    public static IValueConverter ContentTypeDisplay => ContentTypeDisplayConverter.Instance;
    public static IValueConverter TagDisplay => TagDisplayConverter.Instance;
    public static IValueConverter SourceToBrush => SourceToBrushConverter.Instance;
    public static IValueConverter SourceToForeground => SourceToForegroundConverter.Instance;
    public static IValueConverter TagBackground => BoolToTagBackgroundConverter.Instance;
    public static IValueConverter TagForeground => BoolToTagForegroundConverter.Instance;
    public static IValueConverter TagBorder => BoolToTagBorderConverter.Instance;
}
