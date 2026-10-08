import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('<views:LauncherSettingsView IsVisible="{Binding IsSettingsTabAppearance}"/>', '<views:LauncherSettingsView IsVisible="{Binding IsSettingsTabAppearance}"/>\n                <views:ThemeSettingsView IsVisible="{Binding IsSettingsTabTheme}"/>')

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
