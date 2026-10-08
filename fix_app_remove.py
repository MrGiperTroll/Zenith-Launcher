import re

with open('App.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('        <StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml" />\n', '')

with open('App.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
