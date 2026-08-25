using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Services;

/// <summary>
/// Produces real Windows .ico files for profile shortcuts.
/// A desktop shortcut cannot point at a .png, so the profile's custom image is
/// re-encoded and wrapped into the ICO container (PNG payload, supported since Vista).
/// </summary>
public static class ShortcutIconService
{
    public static string ShortcutsIcoDir => Path.Combine(ZenithPaths.AppDataDir, "icons", "shortcuts");

    /// <summary>Returns an .ico path for the instance's custom icon, or null when the
    /// profile has none (the caller then falls back to the launcher's own exe icon).</summary>
    public static string? GetOrCreateInstanceIco(InstanceModel? instance)
    {
        try
        {
            var src = instance?.IconPath;
            if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) return null;

            Directory.CreateDirectory(ShortcutsIcoDir);
            var dest = Path.Combine(ShortcutsIcoDir, $"{instance!.Id}.ico");

            // Reuse the cached ico until the source image changes.
            if (File.Exists(dest) &&
                File.GetLastWriteTimeUtc(dest) >= File.GetLastWriteTimeUtc(src))
                return dest;

            using var bmp = new Bitmap(src);
            var maxSide = Math.Max(bmp.PixelSize.Width, bmp.PixelSize.Height);
            Bitmap effective = bmp;
            var sizeByte = (byte)Math.Min(maxSide, 255);
            if (maxSide > 256)
            {
                effective = bmp.CreateScaledBitmap(new PixelSize(256, 256));
                sizeByte = 0; // 0 means 256 in the ICO directory entry
            }

            using var png = new MemoryStream();
            effective.Save(png, PngBitmapEncoderOptions.Default);
            if (!ReferenceEquals(effective, bmp)) effective.Dispose();
            var pngBytes = png.ToArray();

            using var ico = new MemoryStream();
            using (var w = new BinaryWriter(ico))
            {
                w.Write((short)0);   // reserved
                w.Write((short)1);   // type: icon
                w.Write((short)1);   // image count
                w.Write(sizeByte);   // width  (0 = 256)
                w.Write(sizeByte);   // height (0 = 256)
                w.Write((byte)0);    // palette size
                w.Write((byte)0);    // reserved
                w.Write((short)1);   // color planes
                w.Write((short)32);  // bits per pixel
                w.Write(pngBytes.Length);
                w.Write(22);         // 6-byte header + 16-byte entry
                w.Write(pngBytes);
            }

            File.WriteAllBytes(dest, ico.ToArray());
            return dest;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to build shortcut icon", ex);
            return null;
        }
    }
}
