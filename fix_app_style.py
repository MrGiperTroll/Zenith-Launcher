import re

with open('App.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('        <FluentTheme />', '        <FluentTheme />\n        <StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml" />')

with open('App.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
