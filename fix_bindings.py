import re

with open('Views/ThemeSettingsWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('<DataTemplate x:DataType="services:ThemePreset">', '<DataTemplate x:DataType="services:ThemePreset" x:CompileBindings="False">')

with open('Views/ThemeSettingsWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
