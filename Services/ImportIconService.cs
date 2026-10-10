using System;
using System.IO;
using Avalonia.Media.Imaging;

namespace CustomMcLauncher.Services;

/// <summary>
/// Serves the official Prism Launcher and MultiMC import logos
/// shipped as PNGs in Assets/Import. Loaded once and cached.
/// </summary>
public static class ImportIconService
{
    private static Bitmap? _prism;
    private static Bitmap? _multiMc;

    public static Bitmap? GetPrismIcon() => _prism ??= LoadFromDisk("prism");
    public static Bitmap? GetMultiMCIcon() => _multiMc ??= LoadFromDisk("multimc");

    private static Bitmap? LoadFromDisk(string key)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Import", key + ".png");
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
