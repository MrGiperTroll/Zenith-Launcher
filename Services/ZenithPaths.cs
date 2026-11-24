using System;
using System.IO;

namespace ZenithLauncher.Services;

public static class ZenithPaths
{
    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".zenith");

    public static string CrashLogPath => Path.Combine(AppDataDir, "crash.log");

    private static bool _configMigrated;

    public static string ConfigFilePath
    {
        get
        {
            var path = Path.Combine(AppDataDir, "launcher_config.json");
            MigrateLegacyConfig(path);
            return path;
        }
    }

    private static void MigrateLegacyConfig(string newPath)
    {
        if (_configMigrated) return;
        _configMigrated = true;

        try
        {
            if (File.Exists(newPath)) return;

            var legacy = Path.Combine(AppContext.BaseDirectory, "launcher_config.json");
            if (File.Exists(legacy))
            {
                Directory.CreateDirectory(AppDataDir);
                File.Copy(legacy, newPath);
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to migrate legacy config, falling back to defaults", ex);
        }
    }

    private static bool _appDataMigrated;

    public static void EnsureAppDataExists()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            MigrateLegacyAppData();
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to create app data directory", ex);
        }
    }

    private static void MigrateLegacyAppData()
    {
        if (_appDataMigrated) return;
        _appDataMigrated = true;

        try
        {
            var legacyAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CustomMcLauncher");
            if (Directory.Exists(legacyAppData))
            {
                foreach (var file in Directory.GetFiles(legacyAppData, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(legacyAppData, file);
                    var target = Path.Combine(AppDataDir, rel);
                    var targetDir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(targetDir))
                        Directory.CreateDirectory(targetDir);

                    if (!File.Exists(target))
                    {
                        File.Copy(file, target, false);
                    }
                }
                LauncherLog.Info($"Migrated legacy user data from {legacyAppData} to {AppDataDir}");
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to migrate legacy CustomMcLauncher AppData", ex);
        }
    }
}