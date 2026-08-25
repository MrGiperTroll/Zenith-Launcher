using System;
using System.IO;

namespace CustomMcLauncher.Services;

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

    public static void EnsureAppDataExists()
    {
        try { Directory.CreateDirectory(AppDataDir); }
        catch (Exception ex) { LauncherLog.Error("Failed to create app data directory", ex); }
    }
}