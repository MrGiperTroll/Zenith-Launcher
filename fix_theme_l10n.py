import re

with open('Views/ThemeSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('Header="Simple"', 'Header="{l10n:Tr Key=theme_simple}"')
text = text.replace('Header="Advanced"', 'Header="{l10n:Tr Key=theme_advanced}"')
text = text.replace('Text="Customize"', 'Text="{l10n:Tr Key=theme_customize}"')

with open('Views/ThemeSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
