namespace CustomMcLauncher.Models;

public class LauncherConfigData
{
    public string JvmArgs { get; set; } = string.Empty;
    public int MinRamMb { get; set; } = 512;
    public int RamMb { get; set; } = 4096;
    public int GameWidth { get; set; } = 1280;
    public int GameHeight { get; set; } = 720;
    public bool IsFullscreen { get; set; } = false;
    public string LaunchBehavior { get; set; } = "KeepOpen";
    public string SelectedDirectoryMode { get; set; } = "Separate directory for each instance";
    public string JavaMode { get; set; } = "Recommended";
    public string CustomJavaPath { get; set; } = string.Empty;
    public string Java25Path { get; set; } = string.Empty;
    public string Java21Path { get; set; } = string.Empty;
    public string Java17Path { get; set; } = string.Empty;
    public string Java8Path { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "";
    public string SimpleAccentColor { get; set; } = "#3B82F6";
    public string BgColor { get; set; } = "#131416";
    public string SurfaceColor { get; set; } = "#18191C";
    public string PanelColor { get; set; } = "#181A1E";
    public string SidebarBgColor { get; set; } = "#101113";
    public string CardColor { get; set; } = "#18191C";
    public string TextPrimaryColor { get; set; } = "#FFFFFF";
    public string TextSecondaryColor { get; set; } = "#8E929B";
    public string BorderDarkColor { get; set; } = "#202227";
    public string FieldColor { get; set; } = "#1B1C20";
    public string HoverColor { get; set; } = "#222429";
    public string LauncherName { get; set; } = "Zenith Launcher";
    public bool UseFlatVersionList { get; set; } = false;

    // Automatically download the required JRE (Adoptium) when no suitable local Java matches.
    public bool AutoManageJava { get; set; } = true;

    // Discord Rich Presence. Empty application id keeps the integration dormant.
    public bool DiscordRpcEnabled { get; set; } = true;

    // UI language display name ("" = auto-detect from OS on first run).
    public string Language { get; set; } = "";
    public string CurseForgeApiKey { get; set; } = string.Empty;

    public string SelectedParticle { get; set; } = "None";
    public int ParticleCount { get; set; } = 50;
    public double ParticleScale { get; set; } = 1.0;
    public double ParticleSpeed { get; set; } = 1.0;
    public string ParticleColorHex { get; set; } = "";
    public double ParticleAngleDeg { get; set; } = 90.0;
    public string ParticleTexturePath { get; set; } = "";
    public double LauncherScale { get; set; } = 1.0;
    public bool IsSidebarExpanded { get; set; } = false;

    public string ThemeSubTab { get; set; } = "Simple";
    public string ThemeMode { get; set; } = "Dark";
    public string SelectedThemeCardId { get; set; } = "forest";
    public bool IsCustomTheme { get; set; } = false;
    public string FontStyle { get; set; } = "Sans";
    public string CustomFontPath { get; set; } = string.Empty;

    public bool IsSetupCompleted { get; set; }
}
