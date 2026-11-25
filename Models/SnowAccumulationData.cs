using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using ZenithLauncher.Services;

namespace ZenithLauncher.Models;

/// <summary>
/// Persisted quantized snow accumulation heightmaps keyed by (PageKey -> SurfaceId -> byte[]).
/// </summary>
public sealed class SnowAccumulationData
{
    public int Version { get; set; } = 1;
    public Dictionary<string, Dictionary<string, byte[]>> Pages { get; set; } = new(StringComparer.Ordinal);
}

public static class SnowAccumulationStore
{
    private static readonly object FileGate = new();

    public static SnowAccumulationData Load()
    {
        try
        {
            var path = ZenithPaths.SnowAccumulationFilePath;
            if (!File.Exists(path))
                return new SnowAccumulationData();

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
                return new SnowAccumulationData();

            var data = JsonSerializer.Deserialize(json, LauncherJsonContext.Compact.SnowAccumulationData);
            if (data == null)
                return new SnowAccumulationData();

            data.Pages ??= new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal);
            return data;
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Failed to load snow accumulation data: {ex.Message}");
            return new SnowAccumulationData();
        }
    }

    public static void Save(SnowAccumulationData data)
    {
        if (data == null) return;
        lock (FileGate)
        {
            try
            {
                ZenithPaths.EnsureAppDataExists();
                var path = ZenithPaths.SnowAccumulationFilePath;
                var tmpPath = path + ".tmp";
                var json = JsonSerializer.Serialize(data, LauncherJsonContext.Compact.SnowAccumulationData);
                File.WriteAllText(tmpPath, json, Encoding.UTF8);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Failed to save snow accumulation data: {ex.Message}");
            }
        }
    }

    public static void ClearFile()
    {
        lock (FileGate)
        {
            try
            {
                var path = ZenithPaths.SnowAccumulationFilePath;
                if (File.Exists(path))
                    File.Delete(path);
                var tmpPath = path + ".tmp";
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Failed to clear snow accumulation file: {ex.Message}");
            }
        }
    }
}
