import re

with open('Views/ThemeSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('Command="{Binding [UserControl].((vm:MainWindowViewModel)DataContext).ApplyThemePresetCommand}"', 'Command="{ReflectionBinding [UserControl].DataContext.ApplyThemePresetCommand}"')

with open('Views/ThemeSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
