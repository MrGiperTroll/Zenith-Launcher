using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CustomMcLauncher.Converters;

public class IconPathToBitmapConverter : IValueConverter
{
    public static readonly IconPathToBitmapConverter Instance = new();

    // Path -> decoded bitmap. Bounded; bitmaps die with the cache entry,
    // re-evaluation reuses instead of decoding again.
    private static readonly ConcurrentDictionary<string, (Bitmap Bmp, DateTime WriteUtc)> _cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
        {
            try
            {
                var writeUtc = System.IO.File.GetLastWriteTimeUtc(path);
                if (_cache.TryGetValue(path, out var hit) && hit.WriteUtc == writeUtc)
                    return hit.Bmp;
                var info = new System.IO.FileInfo(path);
                if (info.Length > 8 * 1024 * 1024) return null;
                if (_cache.Count >= 200) _cache.Clear();
                using var fs = System.IO.File.OpenRead(path);
                var bmp = Bitmap.DecodeToWidth(fs, 96);
                _cache[path] = (bmp, writeUtc);
                return bmp;
            }
            catch { return null; }
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

public class BytesToBitmapConverter : IValueConverter
{
    public static readonly BytesToBitmapConverter Instance = new();

    // Bitmap lives exactly as long as its source bytes: re-evaluation reuses it.
    private static readonly ConditionalWeakTable<byte[], Bitmap> _cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte[] bytes && bytes.Length > 0)
        {
            try
            {
                if (_cache.TryGetValue(bytes, out var hit))
                    return hit;
                using var ms = new System.IO.MemoryStream(bytes);
                var bmp = Bitmap.DecodeToWidth(ms, 128);
                _cache.Add(bytes, bmp);
                return bmp;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
