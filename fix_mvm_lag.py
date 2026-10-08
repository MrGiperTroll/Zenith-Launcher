import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('    partial void OnBgHexChanged(string value) { OnPropertyChanged(nameof(BgColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); SaveLauncherConfig(); }',
                    '    partial void OnBgHexChanged(string value) { OnPropertyChanged(nameof(BgColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); }')

text = text.replace('    partial void OnSurfaceHexChanged(string value) { OnPropertyChanged(nameof(SurfaceColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); SaveLauncherConfig(); }',
                    '    partial void OnSurfaceHexChanged(string value) { OnPropertyChanged(nameof(SurfaceColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); }')

text = text.replace('    partial void OnPanelHexChanged(string value) { OnPropertyChanged(nameof(PanelColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); SaveLauncherConfig(); }',
                    '    partial void OnPanelHexChanged(string value) { OnPropertyChanged(nameof(PanelColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); }')

text = text.replace('    partial void OnCustomThemeHexChanged(string value)\n    {\n        UpdatePreviewBrushes(value);\n        OnPropertyChanged(nameof(CustomThemeColorValue));\n        ZenithTheme.Apply(GetLauncherConfigSnapshot());\n    }',
                    '    partial void OnCustomThemeHexChanged(string value)\n    {\n        UpdatePreviewBrushes(value);\n        OnPropertyChanged(nameof(CustomThemeColorValue));\n        ZenithTheme.Apply(GetLauncherConfigSnapshot());\n    }')


with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
