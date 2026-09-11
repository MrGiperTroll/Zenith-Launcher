namespace CustomMcLauncher.Models;

public class LauncherConfigData
{
    public string JvmArgs { get; set; } = string.Empty;
    public int MinRamMb { get; set; } = 1024;
    public int RamMb { get; set; } = 4096;
    public int GameWidth { get; set; } = 1280;
    public int GameHeight { get; set; } = 720;
    public bool IsFullscreen { get; set; } = false;
    public string LaunchBehavior { get; set; } = "KeepOpen";
    public string SelectedDirectoryMode { get; set; } = "Separate directory for each instance";
    public string JavaMode { get; set; } = "Recommended";
    public string CustomJavaPath { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "";
    public bool UseFlatVersionList { get; set; } = false;

    // Automatically download the required JRE (Adoptium) when no suitable local Java matches.
    public bool AutoManageJava { get; set; } = true;

    // Discord Rich Presence. Empty application id keeps the integration dormant.
    public bool DiscordRpcEnabled { get; set; } = true;

    // UI language display name ("" = auto-detect from OS on first run).
    public string Language { get; set; } = "";

    public bool IsSetupCompleted { get; set; }
}
