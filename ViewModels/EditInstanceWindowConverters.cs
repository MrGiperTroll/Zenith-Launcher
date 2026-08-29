using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace CustomMcLauncher.ViewModels;

public static class EditInstanceWindowConverters
{
    private sealed class BoolToBrushConverter : IValueConverter
    {
        private readonly string _inactiveHex;

        public BoolToBrushConverter(string inactiveHex)
        {
            _inactiveHex = inactiveHex;
        }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => new SolidColorBrush(Color.Parse(value is bool b && b ? ActiveHex() : _inactiveHex));

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    private static string ActiveHex()
    {
        if (Avalonia.Application.Current?.Resources.TryGetResource("AccentBrush", ThemeVariant.Default, out var res) == true && res is ISolidColorBrush brush)
            return brush.Color.ToString();
        return "#10B981";
    }

    private sealed class StringNotEmptyConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new StringNotEmptyConverter();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is string s && !string.IsNullOrWhiteSpace(s);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    private sealed class BoolToZeroConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new BoolToZeroConverter();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is int i && i == 0;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    private sealed class BoolToNonZeroConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new BoolToNonZeroConverter();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is int i && i != 0;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    private sealed class StringEmptyConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new StringEmptyConverter();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is not string s || string.IsNullOrWhiteSpace(s);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    private sealed class LongIsNotZeroConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new LongIsNotZeroConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is long l && l != 0;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private sealed class DateTimeIsNotDefaultConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new DateTimeIsNotDefaultConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is DateTime dt && dt != default && dt > DateTime.MinValue;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private sealed class CollectionIsEmptyConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new CollectionIsEmptyConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is System.Collections.IEnumerable enumerable)
                return !enumerable.GetEnumerator().MoveNext();
            return true;
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    private sealed class IsNotNullConverter : IValueConverter
    {
        public static readonly IValueConverter Instance = new IsNotNullConverter();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value != null;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public static IValueConverter ActiveIcon { get; } = new BoolToBrushConverter("#5A667E");
    public static IValueConverter ActiveText { get; } = new BoolToBrushConverter("#78859E");
    public static IValueConverter IsZero { get; } = BoolToZeroConverter.Instance;
    public static IValueConverter IsNonZero { get; } = BoolToNonZeroConverter.Instance;
    public static IValueConverter IsNotEmpty { get; } = StringNotEmptyConverter.Instance;
    public static IValueConverter IsEmpty { get; } = StringEmptyConverter.Instance;
    public static IValueConverter IsNotZero { get; } = LongIsNotZeroConverter.Instance;
    public static IValueConverter IsNotDefault { get; } = DateTimeIsNotDefaultConverter.Instance;
    public static IValueConverter IsCollectionEmpty { get; } = CollectionIsEmptyConverter.Instance;
    public static IValueConverter IsNotNull { get; } = IsNotNullConverter.Instance;
}