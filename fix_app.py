import re

with open('App.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"', 'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"\\n             xmlns:cp="using:Avalonia.Controls.ColorPicker"')
text = text.replace('<StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml" />', '<cp:ColorPickerTheme />')

with open('App.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
