using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CustomMcLauncher.Services;

public class IconOption
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Blocks"; // Blocks or Mobs
    public string? FilePath { get; set; } // For custom icons, path to file
    public string? GeometryData { get; set; } // For vector icons
    public string Color { get; set; } = "#3A4556"; // For background
    public bool IsCustom { get; set; }
}

public static class IconGalleryService
{
    private static readonly List<IconOption> BuiltInIcons = new()
    {
        // Blocks
        new IconOption { Name = "Grass Block", Category = "Blocks", GeometryData = "M12 2L2 7L12 12L22 7Z M2 7V17L12 22L22 17V7", Color = "#7CB342" },
        new IconOption { Name = "Crafting Table", Category = "Blocks", GeometryData = "M3 3H21V21H3V3M8 8H16V16H8V8", Color = "#8D6E63" },
        new IconOption { Name = "TNT", Category = "Blocks", GeometryData = "M3 3H21V21H3V3M3 8H21M3 16H21", Color = "#F44336" },
        new IconOption { Name = "Diamond Block", Category = "Blocks", GeometryData = "M12 2L22 12L12 22L2 12Z M12 2L12 22M2 12H22", Color = "#00BCD4" },
        new IconOption { Name = "Redstone", Category = "Blocks", GeometryData = "M12 2A10 10 0 0 0 2 12A10 10 0 0 0 12 22A10 10 0 0 0 22 12A10 10 0 0 0 12 2M12 6V18M6 12H18", Color = "#D32F2F" },
        new IconOption { Name = "Gold Block", Category = "Blocks", GeometryData = "M3 3H21V21H3V3M12 8L16 12L12 16L8 12Z", Color = "#FFC107" },
        new IconOption { Name = "Iron Block", Category = "Blocks", GeometryData = "M3 3H21V21H3V3M7 7H17V17H7V7", Color = "#B0BEC5" },
        new IconOption { Name = "Obsidian", Category = "Blocks", GeometryData = "M3 3H21V21H3V3M12 7V17M7 12H17", Color = "#212121" },
        new IconOption { Name = "Chest", Category = "Blocks", GeometryData = "M3 8H21V20H3V8M3 8L12 3L21 8M12 12V20", Color = "#8D6E63" },
        new IconOption { Name = "Furnace", Category = "Blocks", GeometryData = "M3 3H21V21H3V3M8 8H16V12H8V8M8 14H16V18H8V14", Color = "#616161" },
        new IconOption { Name = "Beacon", Category = "Blocks", GeometryData = "M12 2L2 8L12 14L22 8Z M12 14V22M5 20H19", Color = "#00ACC1" },
        new IconOption { Name = "Enchanting Table", Category = "Blocks", GeometryData = "M4 18H20V20H4V18M12 3L4 8L12 13L20 8Z M12 13V18", Color = "#7B1FA2" },
        // Mobs / Characters
        new IconOption { Name = "Creeper", Category = "Mobs", GeometryData = "M9 8H11V10H9V8M13 8H15V10H13V8M12 13C10.5 13 9.5 14 9 15H15C14.5 14 13.5 13 12 13M7 3H17V17H7V3", Color = "#4CAF50" },
        new IconOption { Name = "Cat", Category = "Mobs", GeometryData = "M12 8C12 8 8 4 4 8C4 12 8 16 12 16C16 16 20 12 20 8C20 4 16 8 12 8M9 11A1 1 0 0 1 10 12A1 1 0 0 1 9 11M15 11A1 1 0 0 1 16 12A1 1 0 0 1 15 11", Color = "#FF9800" },
        new IconOption { Name = "Steve", Category = "Mobs", GeometryData = "M12 4A4 4 0 0 1 16 8A4 4 0 0 1 12 12A4 4 0 0 1 8 8A4 4 0 0 1 12 4M12 13C14.67 13 20 14.33 20 17V20H4V17C4 14.33 9.33 13 12 13Z", Color = "#2196F3" },
        new IconOption { Name = "Enderman", Category = "Mobs", GeometryData = "M12 4C14 4 16 6 16 8C16 10 14 12 12 12C10 12 8 10 8 8C8 6 10 4 12 4M12 13C15 13 19 14.5 19 17V20H5V17C5 14.5 9 13 12 13M9 8H11V10H9V8M13 8H15V10H13V8", Color = "#212121" },
        new IconOption { Name = "Skeleton", Category = "Mobs", GeometryData = "M12 4A4 4 0 0 1 16 8C16 9.5 15 11 12 12C9 11 8 9.5 8 8A4 4 0 0 1 12 4M12 13C13.5 13 16 13.5 16 16V18H8V16C8 13.5 10.5 13 12 13M10 9H11V11H10V9M13 9H14V11H13V9", Color = "#BDBDBD" },
        new IconOption { Name = "Zombie", Category = "Mobs", GeometryData = "M12 4A4 4 0 0 1 16 8C16 10 14 12 12 12C10 12 8 10 8 8A4 4 0 0 1 12 4M12 14C14.5 14 17 15 17 17V19H7V17C7 15 9.5 14 12 14M9 8H11V10H9V8", Color = "#4CAF50" },
        new IconOption { Name = "Pig", Category = "Mobs", GeometryData = "M12 8C13.5 8 15 9 15 11C15 13 13.5 14 12 14C10.5 14 9 13 9 11C9 9 10.5 8 12 8M8 10H10V12H8V10M14 10H16V12H14V10M12 15L10 17H14L12 15Z", Color = "#F48FB1" },
        new IconOption { Name = "Sheep", Category = "Mobs", GeometryData = "M12 6C14 6 16 7.5 16 10C16 12.5 14 14 12 14C10 14 8 12.5 8 10C8 7.5 10 6 12 6M8 11H10V13H8V11M14 11H16V13H14V11M12 15C13 15 15 16 15 18V20H9V18C9 16 11 15 12 15Z", Color = "#FFFFFF" },
    };

    public static IReadOnlyList<IconOption> GetBuiltInIcons() => BuiltInIcons;

    public static string CustomIconsDir => Path.Combine(ZenithPaths.AppDataDir, "icons", "custom");

    public static List<IconOption> GetAllIcons()
    {
        var list = new List<IconOption>(BuiltInIcons);
        try
        {
            var dir = CustomIconsDir;
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir).Where(f => IsImageFile(f)).OrderBy(f => f))
                {
                    list.Add(new IconOption
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Category = "Custom",
                        FilePath = file,
                        IsCustom = true
                    });
                }
            }
        }
        catch { }
        return list;
    }

    public static string SaveCustomIcon(string sourcePath)
    {
        var dir = CustomIconsDir;
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var dest = Path.Combine(dir, fileName);
        File.Copy(sourcePath, dest, true);
        return dest;
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
    }
}
