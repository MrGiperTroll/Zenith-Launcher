import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('    partial void OnBgHexChanged(string value) { OnPropertyChanged(nameof(BgColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); }',
                    '    partial void OnBgHexChanged(string value) { OnPropertyChanged(nameof(BgColorValue)); if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }')

text = text.replace('    partial void OnSurfaceHexChanged(string value) { OnPropertyChanged(nameof(SurfaceColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); }',
                    '    partial void OnSurfaceHexChanged(string value) { OnPropertyChanged(nameof(SurfaceColorValue)); if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }')

text = text.replace('    partial void OnPanelHexChanged(string value) { OnPropertyChanged(nameof(PanelColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); }',
                    '    partial void OnPanelHexChanged(string value) { OnPropertyChanged(nameof(PanelColorValue)); if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }')

text = text.replace('    partial void OnCustomThemeHexChanged(string value)\n    {\n        UpdatePreviewBrushes(value);\n        OnPropertyChanged(nameof(CustomThemeColorValue));\n        ZenithTheme.Apply(GetLauncherConfigSnapshot());\n    }',
                    '    partial void OnCustomThemeHexChanged(string value)\n    {\n        UpdatePreviewBrushes(value);\n        OnPropertyChanged(nameof(CustomThemeColorValue));\n        if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot());\n    }')

# Now add _isLoadingConfig to LoadLauncherConfig
text = text.replace('    private void LoadLauncherConfig()\n    {',
                    '    private bool _isLoadingConfig = false;\n    private void LoadLauncherConfig()\n    {\n        _isLoadingConfig = true;')
text = text.replace('            catch (Exception ex)\n            {\n                Console.WriteLine($"[Config] Failed to load launcher config: {ex}");\n            }\n        }\n    }',
                    '            catch (Exception ex)\n            {\n                Console.WriteLine($"[Config] Failed to load launcher config: {ex}");\n            }\n        }\n        _isLoadingConfig = false;\n    }')

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
