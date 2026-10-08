import re

with open('Views/ThemeSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('xmlns:sys="clr-namespace:System;assembly=mscorlib"', 'xmlns:sys="clr-namespace:System;assembly=mscorlib"\n             xmlns:services="using:CustomMcLauncher.Services"')
text = text.replace('ItemsSource="{Binding ThemePresets}"', 'ItemsSource="{x:Static services:ZenithTheme.Presets}"')

with open('Views/ThemeSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
