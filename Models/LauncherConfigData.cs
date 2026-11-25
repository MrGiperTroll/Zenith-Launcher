namespace ZenithLauncher.Models;

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

    public bool MinimizeToTrayOnClose { get; set; } = false;
    public bool HardwareAcceleration { get; set; } = true;
    public bool UiAnimationsEnabled { get; set; } = true;
    public bool UiAnimPageTransitionsEnabled { get; set; } = true;
    public bool UiAnimTabsEnabled { get; set; } = true;
    public bool UiAnimModalsEnabled { get; set; } = true;
    public bool UiAnimSidebarsEnabled { get; set; } = true;
    public bool UiAnimPopupsEnabled { get; set; } = true;
    public bool SmoothScrollEnabled { get; set; } = true;

    public string ParticleType { get; set; } = "Off";
    public string ParticleLeafPalette { get; set; } = "Autumn";
    public string ParticleCustomImagePath { get; set; } = string.Empty;
    public int ParticleCount { get; set; } = 45;
    public double ParticleSpeed { get; set; } = 1.0;
    public double ParticleWind { get; set; } = 0.25;
    public double ParticleSize { get; set; } = 1.0;
    public double ParticleOpacity { get; set; } = 0.65;
    public bool IsLockAspectRatio { get; set; } = false;
    public bool ShowReleases { get; set; } = true;
    public bool ShowSnapshots { get; set; } = false;
    public bool ShowBetas { get; set; } = false;
    public bool ShowAlphas { get; set; } = false;

    public double LauncherScale { get; set; } = 1.0;
    public bool IsSidebarExpanded { get; set; } = false;

    public string ThemeSubTab { get; set; } = "Simple";
    public string ThemeMode { get; set; } = "Dark";
    public string SelectedThemeCardId { get; set; } = "zenith";
    public bool IsCustomTheme { get; set; } = false;
    public string CustomAccentColor { get; set; } = string.Empty;
    public string FontStyle { get; set; } = "Sans";
    public string CustomFontPath { get; set; } = string.Empty;

    public bool IsSetupCompleted { get; set; }

    private static LauncherConfigData? _cached;
    private static System.DateTime _cachedWriteUtc;
    private static readonly object CacheGate = new();

    public static void UpdateCache(LauncherConfigData config)
    {
        try
        {
            var path = ZenithLauncher.Services.ZenithPaths.ConfigFilePath;
            var writeUtc = System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTimeUtc(path) : System.DateTime.UtcNow;
            lock (CacheGate)
            {
                _cached = config;
                _cachedWriteUtc = writeUtc;
            }
        }
        catch { }
    }

    public static LauncherConfigData Load()
    {
        try
        {
            var path = ZenithLauncher.Services.ZenithPaths.ConfigFilePath;
            if (System.IO.File.Exists(path))
            {
                var writeUtc = System.IO.File.GetLastWriteTimeUtc(path);
                lock (CacheGate)
                {
                    if (_cached != null && _cachedWriteUtc == writeUtc)
                        return _cached;
                }

                var json = System.IO.File.ReadAllText(path);
                var parsed = System.Text.Json.JsonSerializer.Deserialize(json, LauncherJsonContext.Default.LauncherConfigData) ?? new LauncherConfigData();
                lock (CacheGate)
                {
                    _cached = parsed;
                    _cachedWriteUtc = writeUtc;
                }
                return parsed;
            }
        }
        catch { }
        return new LauncherConfigData();
    }
}
