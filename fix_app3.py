import re

with open('App.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('<cp:ColorPickerTheme />', '<StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml" />')
text = text.replace('xmlns:cp="using:Avalonia.Controls.ColorPicker"', '')

with open('App.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
