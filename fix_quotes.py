with open('Views/ThemeSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('""', '"')

with open('Views/ThemeSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
