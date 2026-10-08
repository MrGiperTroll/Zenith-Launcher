import re

with open('Views/LauncherSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('Text="Edit Theme"', 'Text="{l10n:Tr Key=theme_edit}"')
text = text.replace('Text="Customize colors, animations, and the overall look and feel of Zenith."', 'Text="{l10n:Tr Key=theme_edit_desc}"')
text = text.replace('Content="Customize"', 'Content="{l10n:Tr Key=theme_customize}"')

with open('Views/LauncherSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
