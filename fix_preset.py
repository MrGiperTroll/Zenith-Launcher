import re

with open('Views/ThemeSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('{Binding Name}', '{Binding NameEn}')
text = text.replace('Background="{Binding BgColor}"', 'Background="{StaticResource SurfaceBrush}"')
text = text.replace('Background="{Binding AccentColor}"', 'Background="{Binding HexColor}"')
text = text.replace('Command="{Binding #RootSettingsControl.((vm:MainWindowViewModel)DataContext).ApplyThemePresetCommand}"', 'Command="{Binding [UserControl].((vm:MainWindowViewModel)DataContext).ApplyThemePresetCommand}"')

with open('Views/ThemeSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
