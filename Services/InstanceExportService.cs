using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Services;

public static class InstanceExportService
{
    private static readonly string[] RelevantFolders =
    {
        "mods", "config", "scripts", "kubejs", "shaderpacks", "resourcepacks", "datapacks"
    };

    private static readonly string[] RelevantFiles =
    {
        "options.txt", "servers.dat"
    };

    public static async Task ExportAsync(InstanceModel instance, string format, string targetZipPath)
    {
        await Task.Run(() =>
        {
            if (File.Exists(targetZipPath))
                File.Delete(targetZipPath);

            using var archive = ZipFile.Open(targetZipPath, ZipArchiveMode.Create);

            switch (format.ToLowerInvariant())
            {
                case "curseforge":
                    ExportCurseForge(archive, instance);
                    break;
                case "modrinth":
                    ExportModrinth(archive, instance);
                    break;
                case "prism":
                case "multimc":
                    ExportPrism(archive, instance);
                    break;
                default:
                    ExportGeneric(archive, instance);
                    break;
            }
        });
    }

    private static void ExportCurseForge(ZipArchive archive, InstanceModel instance)
    {
        var loaderId = instance.LoaderType?.ToLowerInvariant() switch
        {
            "fabric" => $"fabric-{(!string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.LoaderVersion : "0.16.9")}",
            "forge" => $"forge-{(!string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.LoaderVersion : "latest")}",
            "neoforge" => $"neoforge-{(!string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.LoaderVersion : "latest")}",
            "quilt" => $"quilt-{(!string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.LoaderVersion : "latest")}",
            _ => "vanilla"
        };

        var manifestObj = new JsonObject
        {
            ["minecraft"] = new JsonObject
            {
                ["version"] = instance.Version,
                ["modLoaders"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = loaderId,
                        ["primary"] = true
                    }
                }
            },
            ["manifestType"] = "minecraftModpack",
            ["manifestVersion"] = 1,
            ["name"] = instance.Name,
            ["version"] = "1.0.0",
            ["author"] = "Zenith Launcher",
            ["files"] = new JsonArray(),
            ["overrides"] = "overrides"
        };

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var manifestEntry = archive.CreateEntry("manifest.json");
        using (var sw = new StreamWriter(manifestEntry.Open()))
        {
            sw.Write(manifestObj.ToJsonString(jsonOptions));
        }

        CopyFoldersToZip(archive, instance.Path, "overrides");
    }

    private static void ExportModrinth(ZipArchive archive, InstanceModel instance)
    {
        var deps = new JsonObject
        {
            ["minecraft"] = instance.Version
        };

        var lType = instance.LoaderType?.ToLowerInvariant();
        if (lType == "fabric")
            deps["fabric-loader"] = !string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.LoaderVersion : "0.16.9";
        else if (lType == "forge")
            deps["forge"] = !string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.LoaderVersion : "latest";
        else if (lType == "neoforge")
            deps["neoforge"] = !string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.LoaderVersion : "latest";
        else if (lType == "quilt")
            deps["quilt-loader"] = !string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.LoaderVersion : "latest";

        var indexObj = new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["versionId"] = "1.0.0",
            ["name"] = instance.Name,
            ["summary"] = $"Exported from Zenith Launcher ({instance.Name})",
            ["files"] = new JsonArray(),
            ["dependencies"] = deps
        };

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var indexEntry = archive.CreateEntry("modrinth.index.json");
        using (var sw = new StreamWriter(indexEntry.Open()))
        {
            sw.Write(indexObj.ToJsonString(jsonOptions));
        }

        CopyFoldersToZip(archive, instance.Path, "overrides");
    }

    private static void ExportPrism(ZipArchive archive, InstanceModel instance)
    {
        var cfgEntry = archive.CreateEntry("instance.cfg");
        using (var sw = new StreamWriter(cfgEntry.Open()))
        {
            sw.WriteLine("[General]");
            sw.WriteLine("ConfigVersion=1.2");
            sw.WriteLine($"name={instance.Name}");
            sw.WriteLine("iconKey=default");
            sw.WriteLine("InstanceType=OneSix");
            sw.WriteLine($"IntendedVersion={instance.Version}");
            if (instance.MaxMemoryMb >= 512)
                sw.WriteLine($"MaxMemAlloc={instance.MaxMemoryMb}");
            if (!string.IsNullOrWhiteSpace(instance.CustomJavaPath))
                sw.WriteLine($"JavaPath={instance.CustomJavaPath}");
            if (!string.IsNullOrWhiteSpace(instance.JvmArgs))
                sw.WriteLine($"JvmArgs={instance.JvmArgs}");
        }

        var comps = new JsonArray
        {
            new JsonObject
            {
                ["cachedName"] = "Minecraft",
                ["important"] = true,
                ["uid"] = "net.minecraft",
                ["version"] = instance.Version
            }
        };

        var lType = instance.LoaderType?.ToLowerInvariant();
        if (lType == "fabric")
        {
            comps.Add(new JsonObject
            {
                ["cachedName"] = "Fabric Loader",
                ["uid"] = "net.fabricmc.fabric-loader",
                ["version"] = !string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.LoaderVersion : "0.16.9"
            });
        }
        else if (lType == "forge")
        {
            comps.Add(new JsonObject
            {
                ["cachedName"] = "Forge",
                ["uid"] = "net.minecraftforge",
                ["version"] = !string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.LoaderVersion : "latest"
            });
        }
        else if (lType == "neoforge")
        {
            comps.Add(new JsonObject
            {
                ["cachedName"] = "NeoForge",
                ["uid"] = "net.neoforged",
                ["version"] = !string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.LoaderVersion : "latest"
            });
        }
        else if (lType == "quilt")
        {
            comps.Add(new JsonObject
            {
                ["cachedName"] = "Quilt Loader",
                ["uid"] = "org.quiltmc.quilt-loader",
                ["version"] = !string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.LoaderVersion : "latest"
            });
        }

        var packObj = new JsonObject
        {
            ["components"] = comps,
            ["formatVersion"] = 1
        };

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var packEntry = archive.CreateEntry("mmc-pack.json");
        using (var sw = new StreamWriter(packEntry.Open()))
        {
            sw.Write(packObj.ToJsonString(jsonOptions));
        }

        CopyFoldersToZip(archive, instance.Path, ".minecraft");
    }

    private static void ExportGeneric(ZipArchive archive, InstanceModel instance)
    {
        CopyFoldersToZip(archive, instance.Path, "");
    }

    private static void CopyFoldersToZip(ZipArchive archive, string instancePath, string prefix)
    {
        if (!Directory.Exists(instancePath)) return;

        foreach (var folder in RelevantFolders)
        {
            var dir = Path.Combine(instancePath, folder);
            if (!Directory.Exists(dir)) continue;

            var targetFolderPrefix = string.IsNullOrEmpty(prefix) ? folder : $"{prefix}/{folder}";
            AddDirectoryToZip(archive, dir, targetFolderPrefix);
        }

        foreach (var file in RelevantFiles)
        {
            var filePath = Path.Combine(instancePath, file);
            if (!File.Exists(filePath)) continue;

            var targetFile = string.IsNullOrEmpty(prefix) ? file : $"{prefix}/{file}";
            try
            {
                archive.CreateEntryFromFile(filePath, targetFile);
            }
            catch { }
        }
    }

    private static void AddDirectoryToZip(ZipArchive archive, string sourceDir, string entryPrefix)
    {
        var dirInfo = new DirectoryInfo(sourceDir);
        if (!dirInfo.Exists) return;

        foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
        {
            if (file.Name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) continue;

            var rel = Path.GetRelativePath(sourceDir, file.FullName).Replace('\\', '/');
            var entryName = $"{entryPrefix}/{rel}";
            try
            {
                archive.CreateEntryFromFile(file.FullName, entryName);
            }
            catch { }
        }
    }
}
