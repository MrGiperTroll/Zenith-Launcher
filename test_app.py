import re

with open('App.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('        <FluentTheme />\n        <StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml" />',
                    '        <FluentTheme />\n        <StyleInclude Source="avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml" />')

print(text.find('avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml'))
