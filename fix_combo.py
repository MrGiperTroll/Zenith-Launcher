import re

with open('Views/ThemeSettingsWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# Replace ComboBoxItems with sys:String
text = text.replace('xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"', 'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"\n        xmlns:sys="clr-namespace:System;assembly=mscorlib"')

text = text.replace('<ComboBoxItem Content="None"/>', '<sys:String>None</sys:String>')
text = text.replace('<ComboBoxItem Content="Snowflakes"/>', '<sys:String>Snowflakes</sys:String>')
text = text.replace('<ComboBoxItem Content="Leaves"/>', '<sys:String>Leaves</sys:String>')

with open('Views/ThemeSettingsWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
