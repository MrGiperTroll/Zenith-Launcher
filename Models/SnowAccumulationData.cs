using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using ZenithLauncher.Services;

namespace ZenithLauncher.Models;

/// <summary>
/// Legacy v1/v2 snow-only heightmap schema (kept for transparent migration into weather_accumulation.json).
/// </summary>
public sealed class SnowAccumulationData
{
    public int Version { get; set; } = 1;
    public Dictionary<string, Dictionary<string, byte[]>> Pages { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Persisted quantized weather accumulation heightmaps keyed by
/// (WeatherMode -> ContextKey -> SurfaceId -> byte[]).
/// </summary>
public sealed class WeatherAccumulationData
{
    public int Version { get; set; } = 3;
    public Dictionary<string, Dictionary<string, Dictionary<string, byte[]>>> Modes { get; set; } = new(StringComparer.Ordinal);
}

public static class WeatherAccumulationStore
{
    private static readonly object FileGate = new();

    public static WeatherAccumulationData Load()
    {
        try
        {
            var path = ZenithPaths.WeatherAccumulationFilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var data = JsonSerializer.Deserialize(json, LauncherJsonContext.Compact.WeatherAccumulationData);
                    if (data != null)
                    {
                        data.Modes ??= new Dictionary<string, Dictionary<string, Dictionary<string, byte[]>>>(StringComparer.Ordinal);
                        return data;
                    }
                }
            }

            // One-time fallback migration from legacy snow_accumulation.json
            var legacyPath = ZenithPaths.SnowAccumulationFilePath;
            if (File.Exists(legacyPath))
            {
                var legacyJson = File.ReadAllText(legacyPath, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(legacyJson))
                {
                    var legacy = JsonSerializer.Deserialize(legacyJson, LauncherJsonContext.Compact.SnowAccumulationData);
                    if (legacy?.Pages is { Count: > 0 })
                    {
                        var migrated = new WeatherAccumulationData { Version = legacy.Version >= 2 ? 3 : 1 };
                        migrated.Modes["Snow"] = legacy.Pages;
                        return migrated;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Failed to load weather accumulation data: {ex.Message}");
        }
        return new WeatherAccumulationData();
    }

    public static void Save(WeatherAccumulationData data)
    {
        if (data == null) return;
        lock (FileGate)
        {
            try
            {
                ZenithPaths.EnsureAppDataExists();
                var path = ZenithPaths.WeatherAccumulationFilePath;
                var tmpPath = path + ".tmp";
                var json = JsonSerializer.Serialize(data, LauncherJsonContext.Compact.WeatherAccumulationData);
                File.WriteAllText(tmpPath, json, Encoding.UTF8);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Failed to save weather accumulation data: {ex.Message}");
            }
        }
    }

    public static void ClearFile()
    {
        lock (FileGate)
        {
            try
            {
                DeleteIfExists(ZenithPaths.WeatherAccumulationFilePath);
                DeleteIfExists(ZenithPaths.WeatherAccumulationFilePath + ".tmp");
                DeleteIfExists(ZenithPaths.SnowAccumulationFilePath);
                DeleteIfExists(ZenithPaths.SnowAccumulationFilePath + ".tmp");
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Failed to clear weather accumulation file: {ex.Message}");
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
