using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CustomMcLauncher.Controls;

public static class SkinPixelExtractor
{
    public static Color[,] ExtractPixels(Bitmap bmp)
    {
        var pixels = new Color[64, 64];
        try
        {
            var rtb = new RenderTargetBitmap(new PixelSize(64, 64));
            using (var ctx = rtb.CreateDrawingContext())
            {
                ctx.DrawImage(bmp, new Rect(0, 0, 64, 64));
            }
            var buffer = new byte[64 * 64 * 4];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                rtb.CopyPixels(new PixelRect(0, 0, 64, 64), handle.AddrOfPinnedObject(), buffer.Length, 64 * 4);
            }
            finally
            {
                handle.Free();
            }

            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    int idx = (y * 64 + x) * 4;
                    byte b = buffer[idx + 0];
                    byte g = buffer[idx + 1];
                    byte r = buffer[idx + 2];
                    byte a = buffer[idx + 3];
                    pixels[x, y] = Color.FromArgb(a, r, g, b);
                }
            }
        }
        catch { }
        return pixels;
    }
}
