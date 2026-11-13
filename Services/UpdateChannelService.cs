using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CustomMcLauncher.Services;

public enum UpdateChannel
{
    Stable,
    Beta
}

public static class UpdateChannelService
{
    /// <summary>
    /// Determines the active update channel.
    /// Priority:
    /// 1. Command-line argument: --beta
    /// 2. Local config file next to the executable (launcher_config.json or config.json) with "channel": "beta"
    /// 3. Default: Stable
    /// </summary>
    public static UpdateChannel GetCurrentChannel()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            if (args != null && args.Any(a => string.Equals(a, "--beta", StringComparison.OrdinalIgnoreCase)))
            {
                return UpdateChannel.Beta;
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to parse command-line args for update channel.", ex);
        }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var candidateFiles = new[]
            {
                Path.Combine(baseDir, "launcher_config.json"),
                Path.Combine(baseDir, "config.json")
            };

            foreach (var path in candidateFiles)
            {
                if (!File.Exists(path)) continue;

                var text = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("channel", out var prop) ||
                    doc.RootElement.TryGetProperty("Channel", out prop))
                {
                    var val = prop.GetString()?.Trim();
                    if (string.Equals(val, "beta", StringComparison.OrdinalIgnoreCase))
                    {
                        return UpdateChannel.Beta;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to read local config for update channel.", ex);
        }

        return UpdateChannel.Stable;
    }
}
