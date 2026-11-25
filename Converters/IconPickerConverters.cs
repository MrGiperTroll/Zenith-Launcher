using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ZenithLauncher.Common.Utils;

namespace ZenithLauncher.Converters;

public class IconPathToBitmapConverter : IValueConverter
{
    public static readonly IconPathToBitmapConverter Instance = new();

    // Bounded LRU cache of decoded profile icons. Evicted entries are NOT disposed synchronously
    // because visible Image controls may still hold a reference; GC finalizes unused bitmaps safely.
    private static readonly LruCache<string, (Bitmap Bmp, DateTime WriteUtc)> _cache =
        new(20, null, StringComparer.OrdinalIgnoreCase);

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
                var bmp = Bitmap.DecodeToWidth(fs, 96, BitmapInterpolationMode.MediumQuality);
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
    private static readonly SolidColorBrush DefaultBrush = new(Color.Parse("#3A4556"));
    private static readonly LruCache<string, SolidColorBrush> _brushCache = new(32, null, StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            if (_brushCache.TryGet(s, out var cached))
                return cached;
            try
            {
                var brush = new SolidColorBrush(Color.Parse(s));
                _brushCache.Set(s, brush);
                return brush;
            }
            catch { }
        }
        return DefaultBrush;
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

    private readonly record struct BytesCacheKey(ulong Hash, int Length, int DecodeWidth);

    // Content-addressed bounded LRU cache. Never calls Bitmap.Dispose() on eviction because
    // active UI Image controls may still reference an evicted bitmap.
    private static readonly LruCache<BytesCacheKey, Bitmap> _cache = new(16, null);

    public static void ClearCache() => _cache.Clear();

    private static ulong ComputeHash(byte[] data)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        for (int i = 0; i < data.Length; i++)
        {
            hash ^= data[i];
            hash *= prime;
        }
        return hash;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte[] bytes && bytes.Length > 0)
        {
            try
            {
                int decodeWidth = 400;
                if (parameter is int pInt) decodeWidth = pInt;
                else if (parameter is string pStr && int.TryParse(pStr, out var pParsed)) decodeWidth = pParsed;

                var key = new BytesCacheKey(ComputeHash(bytes), bytes.Length, decodeWidth);
                if (_cache.TryGet(key, out var hit))
                    return hit;

                using var ms = new System.IO.MemoryStream(bytes);
                var bmp = decodeWidth > 0
                    ? Bitmap.DecodeToWidth(ms, decodeWidth, BitmapInterpolationMode.MediumQuality)
                    : Bitmap.DecodeToWidth(ms, 800, BitmapInterpolationMode.MediumQuality);
                _cache.Set(key, bmp);
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

public class FullImageConverter : IValueConverter
{
    public static readonly FullImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
        {
            try
            {
                int decodeWidth = 1280;
                if (parameter is int pInt && pInt > 0) decodeWidth = pInt;
                else if (parameter is string pStr && int.TryParse(pStr, out var pParsed) && pParsed > 0) decodeWidth = pParsed;

                using var fs = System.IO.File.OpenRead(path);
                return Bitmap.DecodeToWidth(fs, decodeWidth, BitmapInterpolationMode.MediumQuality);
            }
            catch { return null; }
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
