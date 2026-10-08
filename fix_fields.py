import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

props = '''
    [ObservableProperty] private string _bgHex = "#080A0F";
    [ObservableProperty] private string _surfaceHex = "#0D111A";
    [ObservableProperty] private string _panelHex = "#121722";

    partial void OnBgHexChanged(string value) => SaveLauncherConfig();
    partial void OnSurfaceHexChanged(string value) => SaveLauncherConfig();
    partial void OnPanelHexChanged(string value) => SaveLauncherConfig();
'''

text = text.replace('private string _customThemeHex = ZenithTheme.DefaultAccent;', 'private string _customThemeHex = ZenithTheme.DefaultAccent;' + props)

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
