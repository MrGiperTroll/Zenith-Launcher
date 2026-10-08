import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

props = '''    public Avalonia.Media.Color BgColorValue {
        get => Avalonia.Media.Color.TryParse(BgHex, out var c) ? c : Avalonia.Media.Colors.Black;
        set { BgHex = value.ToString(); OnPropertyChanged(nameof(BgColorValue)); }
    }
    public Avalonia.Media.Color SurfaceColorValue {
        get => Avalonia.Media.Color.TryParse(SurfaceHex, out var c) ? c : Avalonia.Media.Colors.Black;
        set { SurfaceHex = value.ToString(); OnPropertyChanged(nameof(SurfaceColorValue)); }
    }
    public Avalonia.Media.Color PanelColorValue {
        get => Avalonia.Media.Color.TryParse(PanelHex, out var c) ? c : Avalonia.Media.Colors.Black;
        set { PanelHex = value.ToString(); OnPropertyChanged(nameof(PanelColorValue)); }
    }
    public Avalonia.Media.Color CustomThemeColorValue {
        get => Avalonia.Media.Color.TryParse(CustomThemeHex, out var c) ? c : Avalonia.Media.Colors.Black;
        set { CustomThemeHex = value.ToString(); OnPropertyChanged(nameof(CustomThemeColorValue)); }
    }'''

text = text.replace('    [ObservableProperty] private string _bgHex = "#080A0F";', props + '\n    [ObservableProperty] private string _bgHex = "#080A0F";')

methods = '''    partial void OnBgHexChanged(string value) { OnPropertyChanged(nameof(BgColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); }
    partial void OnSurfaceHexChanged(string value) { OnPropertyChanged(nameof(SurfaceColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); }
    partial void OnPanelHexChanged(string value) { OnPropertyChanged(nameof(PanelColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); }'''

text = text.replace('    partial void OnCustomThemeHexChanged(string value)\n    {\n        UpdatePreviewBrushes(value);\n    }',
                    '    partial void OnCustomThemeHexChanged(string value)\n    {\n        UpdatePreviewBrushes(value);\n        OnPropertyChanged(nameof(CustomThemeColorValue));\n        ZenithTheme.Apply(GetLauncherConfigSnapshot());\n    }\n' + methods)

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
