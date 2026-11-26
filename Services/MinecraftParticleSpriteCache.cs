using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SkiaSharp;
using ZenithLauncher.Controls;

namespace ZenithLauncher.Services;

/// <summary>
/// Extracts original ambient particle textures (cherry_*, leaf_*, snow.png components)
/// from locally installed Minecraft client JARs into %APPDATA%\.zenith\cache\particles\
/// and decodes tiny crisp pixel-art SKBitmaps for the active particle effect only.
/// Provides lightweight procedural pixel-art fallbacks if no client JAR exists locally.
/// </summary>
public static class MinecraftParticleSpriteCache
{
    private const int MaxSpritesPerEffect = 12;
    private const int MaxCandidateJarsToScan = 8;

    private static readonly object DiskExtractLock = new();
    private static readonly HashSet<ParticleKind> DiskCacheAttempted = new();

    public static string CacheRootDir => Path.Combine(ZenithPaths.AppDataDir, "cache", "particles");

    /// <summary>
    /// Returns freshly allocated crisp SKBitmaps for the requested mode.
    /// Caller owns the returned list and must Dispose() all bitmaps.
    /// </summary>
    public static List<SKBitmap> LoadSpritesForMode(ParticleKind kind)
    {
        var result = new List<SKBitmap>();
        if (kind is ParticleKind.Off or ParticleKind.Custom)
            return result;

        try
        {
            EnsureDiskCacheForMode(kind);
            var folder = GetModeCacheFolder(kind);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                var files = Directory.GetFiles(folder, "*.png")
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxSpritesPerEffect);

                foreach (var file in files)
                {
                    var bmp = DecodeCrispPng(file, normalizeLeafTint: kind == ParticleKind.Leaves);
                    if (bmp != null)
                        result.Add(bmp);
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Particle sprite cache load fallback for {kind}: {ex.Message}");
        }

        if (result.Count == 0)
        {
            PopulateProceduralPixelFallback(kind, result);
        }

        return result;
    }

    private static string GetModeCacheFolder(ParticleKind kind) => kind switch
    {
        ParticleKind.Snow => Path.Combine(CacheRootDir, "snow"),
        ParticleKind.Leaves => Path.Combine(CacheRootDir, "leaves"),
        ParticleKind.Sakura => Path.Combine(CacheRootDir, "sakura"),
        _ => string.Empty
    };

    private static void EnsureDiskCacheForMode(ParticleKind kind)
    {
        var folder = GetModeCacheFolder(kind);
        if (string.IsNullOrEmpty(folder)) return;

        lock (DiskExtractLock)
        {
            if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.png").Any())
            {
                DiskCacheAttempted.Add(kind);
                return;
            }

            if (DiskCacheAttempted.Contains(kind))
                return;

            DiskCacheAttempted.Add(kind);

            foreach (var jarPath in EnumerateCandidateClientJars().Take(MaxCandidateJarsToScan))
            {
                try
                {
                    using var zip = ZipFile.OpenRead(jarPath);
                    bool extracted = kind switch
                    {
                        ParticleKind.Sakura => ExtractIndexedParticles(zip, folder, "cherry_", "cherry", 12),
                        ParticleKind.Leaves => ExtractIndexedParticles(zip, folder, "leaf_", "leaf", 12)
                                            || ExtractIndexedParticles(zip, folder, "tinted_leaf_", "leaf", 12),
                        ParticleKind.Snow => ExtractSnowflakes(zip, folder),
                        _ => false
                    };

                    if (extracted)
                    {
                        LauncherLog.Info($"Extracted {kind} particle textures from {Path.GetFileName(jarPath)}");
                        return;
                    }
                }
                catch
                {
                    // Skip unreadable/corrupt archive and try older local versions.
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidateClientJars()
    {
        var dirs = new List<string>
        {
            Path.Combine(ZenithPaths.AppDataDir, "versions"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "versions")
        };

        var found = new List<FileInfo>();
        foreach (var root in dirs)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                var di = new DirectoryInfo(root);
                foreach (var fi in di.EnumerateFiles("*.jar", SearchOption.AllDirectories))
                {
                    // Client jars with assets are > 4 MB; tiny library jars are skipped early.
                    if (fi.Length > 4 * 1024 * 1024)
                        found.Add(fi);
                }
            }
            catch { }
        }

        // Prefer newest client jars without loader wrappers first, then fall back to any valid client jar.
        return found
            .OrderBy(f => f.Name.Contains("loader", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ExtractIndexedParticles(ZipArchive zip, string targetFolder, string jarPrefix, string outPrefix, int maxCount)
    {
        var entries = new List<(int idx, ZipArchiveEntry entry)>();
        for (int i = 0; i < maxCount; i++)
        {
            var path = $"assets/minecraft/textures/particle/{jarPrefix}{i}.png";
            var entry = zip.GetEntry(path);
            if (entry != null)
                entries.Add((i, entry));
        }

        if (entries.Count == 0) return false;

        Directory.CreateDirectory(targetFolder);
        foreach (var (idx, entry) in entries)
        {
            var dest = Path.Combine(targetFolder, $"{outPrefix}_{idx:D2}.png");
            using var src = entry.Open();
            using var dst = File.Create(dest);
            src.CopyTo(dst);
        }

        return true;
    }

    private static bool ExtractSnowflakes(ZipArchive zip, string targetFolder)
    {
        var snowEntry = zip.GetEntry("assets/minecraft/textures/environment/snow.png");
        if (snowEntry == null) return false;

        using var stream = snowEntry.Open();
        using var sheet = SKBitmap.Decode(stream);
        if (sheet == null || sheet.Width <= 0 || sheet.Height <= 0)
            return false;

        int w = sheet.Width;
        int h = sheet.Height;
        var visited = new bool[w * h];
        var components = new List<(int x, int y, int bw, int bh, int pxCount)>();
        var queue = new Queue<(int px, int py)>();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (visited[idx] || sheet.GetPixel(x, y).Alpha <= 40)
                    continue;

                int minX = x, maxX = x, minY = y, maxY = y, count = 0;
                visited[idx] = true;
                queue.Enqueue((x, y));

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    count++;
                    if (cx < minX) minX = cx;
                    if (cx > maxX) maxX = cx;
                    if (cy < minY) minY = cy;
                    if (cy > maxY) maxY = cy;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = cy + dy;
                        if (ny < 0 || ny >= h) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx;
                            if (nx < 0 || nx >= w) continue;
                            int nIdx = ny * w + nx;
                            if (!visited[nIdx] && sheet.GetPixel(nx, ny).Alpha > 40)
                            {
                                visited[nIdx] = true;
                                queue.Enqueue((nx, ny));
                            }
                        }
                    }
                }

                int bw = maxX - minX + 1;
                int bh = maxY - minY + 1;
                if (bw >= 3 && bh >= 3 && bw <= 8 && bh <= 8 && count >= 3)
                {
                    components.Add((minX, minY, bw, bh, count));
                }
            }
        }

        if (components.Count == 0) return false;

        // Pick varied distinct snowflake shapes spread across the sheet without dark outlines.
        var selected = components
            .OrderByDescending(c => c.pxCount)
            .Take(MaxSpritesPerEffect)
            .ToList();

        Directory.CreateDirectory(targetFolder);
        int saved = 0;
        foreach (var comp in selected)
        {
            using var flake = new SKBitmap(new SKImageInfo(comp.bw, comp.bh, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            for (int py = 0; py < comp.bh; py++)
            {
                for (int px = 0; px < comp.bw; px++)
                {
                    var orig = sheet.GetPixel(comp.x + px, comp.y + py);
                    flake.SetPixel(px, py, orig.Alpha > 20 ? new SKColor(255, 255, 255, orig.Alpha) : SKColors.Transparent);
                }
            }

            using var image = SKImage.FromBitmap(flake);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data != null)
            {
                using var fs = File.Create(Path.Combine(targetFolder, $"snow_{saved:D2}.png"));
                data.SaveTo(fs);
                saved++;
            }
        }

        return saved > 0;
    }

    private static SKBitmap? DecodeCrispPng(string path, bool normalizeLeafTint)
    {
        try
        {
            using var bmp = SKBitmap.Decode(path);
            if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0)
                return null;

            var normalized = new SKBitmap(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Rgba8888, SKAlphaType.Premul));

            byte maxLuma = 0;
            if (normalizeLeafTint)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        var c = bmp.GetPixel(x, y);
                        if (c.Alpha > 10)
                        {
                            byte l = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
                            if (l > maxLuma) maxLuma = l;
                        }
                    }
                }
            }

            float boost = normalizeLeafTint && maxLuma > 10 && maxLuma < 220
                ? 245.0f / maxLuma
                : 1.0f;

            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.Alpha == 0)
                    {
                        normalized.SetPixel(x, y, SKColors.Transparent);
                        continue;
                    }

                    byte r = c.Red;
                    byte g = c.Green;
                    byte b = c.Blue;

                    if (boost > 1.01f)
                    {
                        r = (byte)Math.Clamp((int)MathF.Round(r * boost), 0, 255);
                        g = (byte)Math.Clamp((int)MathF.Round(g * boost), 0, 255);
                        b = (byte)Math.Clamp((int)MathF.Round(b * boost), 0, 255);
                    }

                    // Store premultiplied RGBA8888 so SKColorFilter Modulate blends cleanly without dark fringes.
                    if (c.Alpha < 255)
                    {
                        r = (byte)(r * c.Alpha / 255);
                        g = (byte)(g * c.Alpha / 255);
                        b = (byte)(b * c.Alpha / 255);
                    }

                    normalized.SetPixel(x, y, new SKColor(r, g, b, c.Alpha));
                }
            }

            return normalized;
        }
        catch
        {
            return null;
        }
    }

    private static void PopulateProceduralPixelFallback(ParticleKind kind, List<SKBitmap> target)
    {
        if (kind == ParticleKind.Snow)
        {
            target.Add(BuildPatternBitmap(new[]
            {
                ".#.",
                "###",
                ".#."
            }, new SKColor(255, 255, 255, 235), new SKColor(255, 255, 255, 235)));
            target.Add(BuildPatternBitmap(new[]
            {
                "#.#",
                ".#.",
                "#.#"
            }, new SKColor(255, 255, 255, 225), new SKColor(255, 255, 255, 225)));
        }
        else if (kind == ParticleKind.Sakura)
        {
            var light = new SKColor(247, 185, 220);
            var shade = new SKColor(239, 167, 205);
            target.Add(BuildPatternBitmap(new[]
            {
                ".#*",
                "###",
                "##."
            }, light, shade));
            target.Add(BuildPatternBitmap(new[]
            {
                "*#.",
                "###",
                ".#*"
            }, light, shade));
        }
        else if (kind == ParticleKind.Leaves)
        {
            var light = new SKColor(242, 242, 242);
            var shade = new SKColor(194, 194, 194);
            target.Add(BuildPatternBitmap(new[]
            {
                "....*",
                ".##*.",
                "##**.",
                "#***.",
                "***.."
            }, light, shade));
            target.Add(BuildPatternBitmap(new[]
            {
                ".##..",
                "##*#.",
                ".**##",
                "..*#*",
                "...*."
            }, light, shade));
        }
    }

    private static SKBitmap BuildPatternBitmap(string[] rows, SKColor primary, SKColor secondary)
    {
        int h = rows.Length;
        int w = rows[0].Length;
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                char ch = rows[y][x];
                bmp.SetPixel(x, y, ch switch
                {
                    '#' => primary,
                    '*' => secondary,
                    _ => SKColors.Transparent
                });
            }
        }
        return bmp;
    }
}
