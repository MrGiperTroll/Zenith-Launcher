using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CustomMcLauncher.ViewModels;

public class IconPathToBitmapConverter : IValueConverter
{
    public static readonly IconPathToBitmapConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
        {
            try { return new Bitmap(path); } catch { return null; }
        }
        return null;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            try { return new SolidColorBrush(Color.Parse(s)); } catch { }
        }
        return new SolidColorBrush(Color.Parse("#3A4556"));
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class IsNullConverter : IValueConverter
{
    public static readonly IsNullConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value == null || (value is string str && string.IsNullOrWhiteSpace(str));
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class IsNotNullConverter : IValueConverter
{
    public static readonly IsNotNullConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value != null && !(value is string str2 && string.IsNullOrWhiteSpace(str2));
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
