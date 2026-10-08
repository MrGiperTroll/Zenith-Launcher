import re

with open('Services/L10n.cs', 'r', encoding='utf-8') as f:
    text = f.read()

# Add to English
en_insert = '["theme_advanced"] = "Advanced",\n        ["theme_simple"] = "Simple",\n        ["theme_customize"] = "Customize",'
text = text.replace('["theme_particles"] = "Background Particles (Leaves/Snow)",', '["theme_particles"] = "Background Particles (Leaves/Snow)",\n        ' + en_insert)

# Add to Russian
ru_insert = '["theme_advanced"] = "Продвинутые",\n        ["theme_simple"] = "Простые",\n        ["theme_customize"] = "Кастомизация",'
text = text.replace('["theme_particles"] = "Частицы на фоне (Листья/Снег)",', '["theme_particles"] = "Частицы на фоне (Листья/Снег)",\n        ' + ru_insert)

with open('Services/L10n.cs', 'w', encoding='utf-8') as f:
    f.write(text)
