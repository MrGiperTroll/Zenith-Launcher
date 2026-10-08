import os

# 1. Update LauncherConfigData.cs
confPath = "Models/LauncherConfigData.cs"
with open(confPath, 'r', encoding='utf-8') as f:
    conf = f.read()

conf = conf.replace('public string PanelColor { get; set; } = "#121722";', 
    'public string PanelColor { get; set; } = "#121722";\n' +
    '    public string SidebarBgColor { get; set; } = "#06080D";\n' +
    '    public string CardColor { get; set; } = "#0D111A";\n' +
    '    public string TextPrimaryColor { get; set; } = "#FFFFFF";\n' +
    '    public string TextSecondaryColor { get; set; } = "#C5CBD6";\n' +
    '    public string BorderDarkColor { get; set; } = "#121622";')
with open(confPath, 'w', encoding='utf-8') as f:
    f.write(conf)

# 2. Update ZenithTheme.cs
ztPath = "Services/ZenithTheme.cs"
with open(ztPath, 'r', encoding='utf-8') as f:
    zt = f.read()

zt = zt.replace('public string PanelColor { get; set; }', 
    'public string PanelColor { get; set; }\n    public string SidebarBgColor { get; set; }\n    public string CardColor { get; set; }\n    public string TextPrimaryColor { get; set; }\n    public string TextSecondaryColor { get; set; }\n    public string BorderDarkColor { get; set; }')
zt = zt.replace('PanelColor = "#121722"', 
    'PanelColor = "#121722", SidebarBgColor = "#06080D", CardColor = "#0D111A", TextPrimaryColor = "#FFFFFF", TextSecondaryColor = "#C5CBD6", BorderDarkColor = "#121622"')
zt = zt.replace('if (TryParse(cfg.PanelColor)) r["PanelBrush"] = new SolidColorBrush(Color.Parse(cfg.PanelColor));',
    'if (TryParse(cfg.PanelColor)) r["PanelBrush"] = new SolidColorBrush(Color.Parse(cfg.PanelColor));\n' +
    '        if (TryParse(cfg.SidebarBgColor)) r["SidebarBgBrush"] = new SolidColorBrush(Color.Parse(cfg.SidebarBgColor));\n' +
    '        if (TryParse(cfg.CardColor)) r["CardBrush"] = new SolidColorBrush(Color.Parse(cfg.CardColor));\n' +
    '        if (TryParse(cfg.TextPrimaryColor)) r["TextPrimaryBrush"] = new SolidColorBrush(Color.Parse(cfg.TextPrimaryColor));\n' +
    '        if (TryParse(cfg.TextSecondaryColor)) r["TextSecondaryBrush"] = new SolidColorBrush(Color.Parse(cfg.TextSecondaryColor));\n' +
    '        if (TryParse(cfg.BorderDarkColor)) r["BorderDarkBrush"] = new SolidColorBrush(Color.Parse(cfg.BorderDarkColor));')
with open(ztPath, 'w', encoding='utf-8') as f:
    f.write(zt)

print("Done models and theme")
