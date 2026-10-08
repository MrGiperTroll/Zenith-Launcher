import re

with open('Services/L10n.cs', 'r', encoding='utf-8') as f:
    text = f.read()

# Add to English
en_insert = '["theme_edit"] = "Edit Theme",\n        ["theme_edit_desc"] = "Customize colors, animations, and the overall look and feel of Zenith.",'
text = text.replace('["theme_advanced"] = "Advanced",', '["theme_advanced"] = "Advanced",\n        ' + en_insert)

# Add to Russian
ru_insert = '["theme_edit"] = "Изменить тему",\n        ["theme_edit_desc"] = "Настройте цвета, анимации и внешний вид Zenith.",'
text = text.replace('["theme_advanced"] = "Продвинутые",', '["theme_advanced"] = "Продвинутые",\n        ' + ru_insert)

with open('Services/L10n.cs', 'w', encoding='utf-8') as f:
    f.write(text)
