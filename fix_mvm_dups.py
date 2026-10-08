import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('    partial void OnBgHexChanged(string value) => SaveLauncherConfig();', '')
text = text.replace('    partial void OnSurfaceHexChanged(string value) => SaveLauncherConfig();', '')
text = text.replace('    partial void OnPanelHexChanged(string value) => SaveLauncherConfig();', '')

text = text.replace('    partial void OnBgHexChanged(string value) { OnPropertyChanged(nameof(BgColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); }',
                    '    partial void OnBgHexChanged(string value) { OnPropertyChanged(nameof(BgColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); SaveLauncherConfig(); }')

text = text.replace('    partial void OnSurfaceHexChanged(string value) { OnPropertyChanged(nameof(SurfaceColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); }',
                    '    partial void OnSurfaceHexChanged(string value) { OnPropertyChanged(nameof(SurfaceColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); SaveLauncherConfig(); }')

text = text.replace('    partial void OnPanelHexChanged(string value) { OnPropertyChanged(nameof(PanelColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); }',
                    '    partial void OnPanelHexChanged(string value) { OnPropertyChanged(nameof(PanelColorValue)); ZenithTheme.Apply(GetLauncherConfigSnapshot()); SaveLauncherConfig(); }')

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
