using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CustomMcLauncher.Common.Utils;

namespace CustomMcLauncher.Converters;

public class IconPathToBitmapConverter : IValueConverter
{
    public static readonly IconPathToBitmapConverter Instance = new();

    // Path -> decoded bitmap bounded with LRU eviction. Disposes bitmaps on eviction or clear.
    private static readonly LruCache<string, (Bitmap Bmp, DateTime WriteUtc)> _cache =
        new(40, entry => { try { entry.Bmp?.Dispose(); } catch { } }, StringComparer.OrdinalIgnoreCase);

    public static void ClearCache() => _cache.Clear();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
        {
            try
            {
                var writeUtc = System.IO.File.GetLastWriteTimeUtc(path);
                if (_cache.TryGet(path, out var hit) && hit.WriteUtc == writeUtc)
                    return hit.Bmp;

                var info = new System.IO.FileInfo(path);
                if (info.Length > 8 * 1024 * 1024) return null;

                using var fs = System.IO.File.OpenRead(path);
                var bmp = Bitmap.DecodeToWidth(fs, 96);
                _cache.Set(path, (bmp, writeUtc));
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

    // Bounded LRU cache for decoded byte previews, ensuring evicted bitmaps are disposed immediately.
    private static readonly LruCache<byte[], Bitmap> _cache =
        new(48, bmp => { try { bmp?.Dispose(); } catch { } }, ReferenceEqualityComparer.Instance);

    public static void ClearCache() => _cache.Clear();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte[] bytes && bytes.Length > 0)
        {
            try
            {
                if (_cache.TryGet(bytes, out var hit))
                    return hit;

                using var ms = new System.IO.MemoryStream(bytes);
                var bmp = Bitmap.DecodeToWidth(ms, 128);
                _cache.Set(bytes, bmp);
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
