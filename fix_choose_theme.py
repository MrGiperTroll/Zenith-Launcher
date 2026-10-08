import re

with open('Views/LauncherSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('DataContext.ChooseThemeCommand', 'DataContext.RequestApplyThemeCommand')

with open('Views/LauncherSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
