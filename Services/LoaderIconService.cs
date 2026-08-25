using System;
using System.Collections.Concurrent;
using System.IO;
using Avalonia.Media.Imaging;

namespace CustomMcLauncher.Services;

/// <summary>
/// Serves the official mod-loader logos shipped as PNGs in Assets/Loaders.
/// Loaded once per process and cached.
/// </summary>
public static class LoaderIconService
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    public static Bitmap? GetIcon(string? loaderName)
    {
        if (string.IsNullOrWhiteSpace(loaderName)) return null;
        var key = Normalize(loaderName);
        if (key.Length == 0) return null;
        if (Cache.TryGetValue(key, out var bmp)) return bmp;

        bmp = LoadFromDisk(key);
        Cache[key] = bmp;
        return bmp;
    }

    public static bool HasIcon(string? loaderName)
    {
        var key = string.IsNullOrWhiteSpace(loaderName) ? "" : Normalize(loaderName);
        return key.Length > 0 && GetIcon(loaderName) != null;
    }

    /// <summary>Maps any display-name variant ("NeoForge 1.21", "forge", ...) to its asset file.</summary>
    private static string Normalize(string loaderName)
    {
        var n = loaderName.ToLowerInvariant();
        if (n.Contains("neoforge")) return "neoforge";
        if (n.Contains("forge")) return "forge";
        if (n.Contains("fabric")) return "fabric";
        if (n.Contains("quilt")) return "quilt";
        if (n.Contains("optifine")) return "optifine";
        if (n.Contains("vanilla") || n.Contains("release") || n.Contains("minecraft")) return "vanilla";
        return "";
    }

    private static Bitmap? LoadFromDisk(string key)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Loaders", key + ".png");
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            return new Bitmap(fs);
        }
        catch
        {
            return null;
        }
    }
}
