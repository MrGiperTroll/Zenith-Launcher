using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace CustomMcLauncher.Converters;

public class BoolToBorderBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            if (Application.Current?.Resources.TryGetResource("AccentBrush", ThemeVariant.Default, out var res) == true && res is ISolidColorBrush brush)
                return new SolidColorBrush(brush.Color);
            return new SolidColorBrush(Color.Parse("#10B981"));
        }
        if (Application.Current?.Resources.TryGetResource("BorderCardSoftBrush", ThemeVariant.Default, out var softRes) == true && softRes is ISolidColorBrush softBrush)
            return new SolidColorBrush(softBrush.Color);
        return new SolidColorBrush(Color.Parse("#26282E"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
