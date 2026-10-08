import os
import re

vmPath = "ViewModels/MainWindowViewModel.cs"
with open(vmPath, 'r', encoding='utf-8') as f:
    vm = f.read()

props = '''
    [ObservableProperty]
    private string _sidebarBgHex = "#06080D";
    partial void OnSidebarBgHexChanged(string value) { if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }

    [ObservableProperty]
    private string _cardHex = "#0D111A";
    partial void OnCardHexChanged(string value) { if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }

    [ObservableProperty]
    private string _textPrimaryHex = "#FFFFFF";
    partial void OnTextPrimaryHexChanged(string value) { if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }

    [ObservableProperty]
    private string _textSecondaryHex = "#C5CBD6";
    partial void OnTextSecondaryHexChanged(string value) { if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }

    [ObservableProperty]
    private string _borderDarkHex = "#121622";
    partial void OnBorderDarkHexChanged(string value) { if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }
'''

vm = vm.replace('private string _panelHex = "#121722";', 'private string _panelHex = "#121722";\n' + props)

# Also update LoadLauncherConfig / GetLauncherConfigSnapshot
vm = vm.replace('if (!string.IsNullOrWhiteSpace(cfg.PanelColor)) PanelHex = cfg.PanelColor;',
    'if (!string.IsNullOrWhiteSpace(cfg.PanelColor)) PanelHex = cfg.PanelColor;\n' +
    '                    if (!string.IsNullOrWhiteSpace(cfg.SidebarBgColor)) SidebarBgHex = cfg.SidebarBgColor;\n' +
    '                    if (!string.IsNullOrWhiteSpace(cfg.CardColor)) CardHex = cfg.CardColor;\n' +
    '                    if (!string.IsNullOrWhiteSpace(cfg.TextPrimaryColor)) TextPrimaryHex = cfg.TextPrimaryColor;\n' +
    '                    if (!string.IsNullOrWhiteSpace(cfg.TextSecondaryColor)) TextSecondaryHex = cfg.TextSecondaryColor;\n' +
    '                    if (!string.IsNullOrWhiteSpace(cfg.BorderDarkColor)) BorderDarkHex = cfg.BorderDarkColor;')

vm = vm.replace('PanelColor = PanelHex,',
    'PanelColor = PanelHex,\n' +
    '            SidebarBgColor = SidebarBgHex,\n' +
    '            CardColor = CardHex,\n' +
    '            TextPrimaryColor = TextPrimaryHex,\n' +
    '            TextSecondaryColor = TextSecondaryHex,\n' +
    '            BorderDarkColor = BorderDarkHex,')

with open(vmPath, 'w', encoding='utf-8') as f:
    f.write(vm)

print("Done viewmodel")
