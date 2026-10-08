import re

with open('Services/L10n.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('["nav_settings"] = "Launcher Settings",', '["nav_settings"] = "Settings",')

with open('Services/L10n.cs', 'w', encoding='utf-8') as f:
    f.write(text)
