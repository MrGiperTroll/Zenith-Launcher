import re

with open('Views/ThemeSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('Color="{Binding BgHex, Mode=TwoWay}"', 'Color="{Binding BgColorValue, Mode=TwoWay}"')
text = text.replace('Color="{Binding SurfaceHex, Mode=TwoWay}"', 'Color="{Binding SurfaceColorValue, Mode=TwoWay}"')
text = text.replace('Color="{Binding PanelHex, Mode=TwoWay}"', 'Color="{Binding PanelColorValue, Mode=TwoWay}"')
text = text.replace('Color="{Binding CustomThemeHex, Mode=TwoWay}"', 'Color="{Binding CustomThemeColorValue, Mode=TwoWay}"')

with open('Views/ThemeSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
