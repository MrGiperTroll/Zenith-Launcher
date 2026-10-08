import re

with open('Models/LauncherConfigData.cs', 'r', encoding='utf-8') as f:
    text = f.read()

props = '''    public string BgColor { get; set; } = "#080A0F";
    public string SurfaceColor { get; set; } = "#0D111A";
    public string PanelColor { get; set; } = "#121722";'''

text = text.replace('public string AccentColor { get; set; } = "";', 'public string AccentColor { get; set; } = "";\n' + props)

with open('Models/LauncherConfigData.cs', 'w', encoding='utf-8') as f:
    f.write(text)

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    vm_text = f.read()

vm_props = '''    [ObservableProperty] private string _bgHex = "#080A0F";
    [ObservableProperty] private string _surfaceHex = "#0D111A";
    [ObservableProperty] private string _panelHex = "#121722";

    partial void OnBgHexChanged(string value) => SaveLauncherConfig();
    partial void OnSurfaceHexChanged(string value) => SaveLauncherConfig();
    partial void OnPanelHexChanged(string value) => SaveLauncherConfig();'''

vm_text = vm_text.replace('[ObservableProperty] private string _customThemeHex = "";', '[ObservableProperty] private string _customThemeHex = "";\n' + vm_props)

vm_text = vm_text.replace('AccentColor = CurrentAccentHex,', 'AccentColor = CurrentAccentHex,\n                BgColor = BgHex,\n                SurfaceColor = SurfaceHex,\n                PanelColor = PanelHex,')

vm_text = vm_text.replace('if (!string.IsNullOrWhiteSpace(cfg.AccentColor))', '''
                    if (!string.IsNullOrWhiteSpace(cfg.BgColor)) BgHex = cfg.BgColor;
                    if (!string.IsNullOrWhiteSpace(cfg.SurfaceColor)) SurfaceHex = cfg.SurfaceColor;
                    if (!string.IsNullOrWhiteSpace(cfg.PanelColor)) PanelHex = cfg.PanelColor;
                    if (!string.IsNullOrWhiteSpace(cfg.AccentColor))''')

vm_text = vm_text.replace('ZenithTheme.Apply(cfg.AccentColor);', 'ZenithTheme.Apply(cfg);')

# Also in RequestApplyThemeCommand we should do ZenithTheme.Apply(cfg)
vm_text = vm_text.replace('ZenithTheme.Apply(CustomThemeHex);', 'ZenithTheme.Apply(GetLauncherConfigSnapshot());')

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(vm_text)

with open('Services/ZenithTheme.cs', 'r', encoding='utf-8') as f:
    z_text = f.read()

# Replace Apply signature
z_text = z_text.replace('public static void Apply(string? hex)', 'public static void Apply(Models.LauncherConfigData cfg)')
z_text = z_text.replace('AccentHex = TryParse(hex) ? hex! : DefaultAccent;', 'AccentHex = TryParse(cfg.AccentColor) ? cfg.AccentColor! : DefaultAccent;')

color_app = '''
        if (TryParse(cfg.BgColor)) r["BgBrush"] = new SolidColorBrush(Color.Parse(cfg.BgColor));
        if (TryParse(cfg.SurfaceColor)) r["SurfaceBrush"] = new SolidColorBrush(Color.Parse(cfg.SurfaceColor));
        if (TryParse(cfg.PanelColor)) r["PanelBrush"] = new SolidColorBrush(Color.Parse(cfg.PanelColor));
        if (TryParse(cfg.PanelColor)) {
            // Need a transparent version of panel brush for sidebar
            var pc = Color.Parse(cfg.PanelColor);
            r["PanelBrushTransparent"] = new SolidColorBrush(Color.FromArgb(0, pc.R, pc.G, pc.B));
        }
'''
z_text = z_text.replace('r["AccentBrush"] = new SolidColorBrush(accent);', color_app + '\n        r["AccentBrush"] = new SolidColorBrush(accent);')

with open('Services/ZenithTheme.cs', 'w', encoding='utf-8') as f:
    f.write(z_text)
