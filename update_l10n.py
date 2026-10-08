import re

with open('Services/L10n.cs', 'r', encoding='utf-8') as f:
    text = f.read()

# English
text = re.sub(r'\[\"nav_settings\"\] = \"Settings\"', '["nav_settings"] = "Launcher Settings"', text)
text = re.sub(r'(?<=        \["nav_settings"\] = "Launcher Settings",\n)', '        ["theme_customize"] = "Customize your theme",\n        ["btn_customize"] = "Customize",\n        ["theme_advanced"] = "Advanced",\n        ["theme_simple"] = "Simple",\n        ["theme_particles"] = "Background Particles (Leaves/Snow)",\n        ["settings_scale"] = "Launcher Scale",\n', text)

# Russian
text = re.sub(r'\[\"nav_settings\"\] = \"Настройки\"', '["nav_settings"] = "Настройки лаунчера"', text)
text = re.sub(r'(?<=        \["nav_settings"\] = "Настройки лаунчера",\n)', '        ["theme_customize"] = "Настрой свою тему",\n        ["btn_customize"] = "Настроить",\n        ["theme_advanced"] = "Продвинутый",\n        ["theme_simple"] = "Простой",\n        ["theme_particles"] = "Частицы на фоне (Листья/Снег)",\n        ["settings_scale"] = "Масштаб лаунчера",\n', text)

# Ukrainian
text = re.sub(r'\[\"nav_settings\"\] = \"Налаштування\"', '["nav_settings"] = "Налаштування лаунчера"', text)
text = re.sub(r'(?<=        \["nav_settings"\] = "Налаштування лаунчера",\n)', '        ["theme_customize"] = "Налаштуй свою тему",\n        ["btn_customize"] = "Налаштувати",\n        ["theme_advanced"] = "Розширений",\n        ["theme_simple"] = "Простий",\n        ["theme_particles"] = "Частинки на тлі (Листя/Сніг)",\n        ["settings_scale"] = "Масштаб лаунчера",\n', text)

# Turkish
text = re.sub(r'\[\"nav_settings\"\] = \"Ayarlar\"', '["nav_settings"] = "Başlatıcı Ayarları"', text)
text = re.sub(r'(?<=        \["nav_settings"\] = "Başlatıcı Ayarları",\n)', '        ["theme_customize"] = "Temanızı özelleştirin",\n        ["btn_customize"] = "Özelleştir",\n        ["theme_advanced"] = "Gelişmiş",\n        ["theme_simple"] = "Basit",\n        ["theme_particles"] = "Arka Plan Parçacıkları",\n        ["settings_scale"] = "Başlatıcı Ölçeği",\n', text)

# German
text = re.sub(r'\[\"nav_settings\"\] = \"Einstellungen\"', '["nav_settings"] = "Launcher-Einstellungen"', text)
text = re.sub(r'(?<=        \["nav_settings"\] = "Launcher-Einstellungen",\n)', '        ["theme_customize"] = "Passen Sie Ihr Design an",\n        ["btn_customize"] = "Anpassen",\n        ["theme_advanced"] = "Erweitert",\n        ["theme_simple"] = "Einfach",\n        ["theme_particles"] = "Hintergrundpartikel",\n        ["settings_scale"] = "Launcher-Skalierung",\n', text)

# French
text = re.sub(r'\[\"nav_settings\"\] = \"Paramètres\"', '["nav_settings"] = "Paramètres du lanceur"', text)
text = re.sub(r'(?<=        \["nav_settings"\] = "Paramètres du lanceur",\n)', '        ["theme_customize"] = "Personnalisez votre thème",\n        ["btn_customize"] = "Personnaliser",\n        ["theme_advanced"] = "Avancé",\n        ["theme_simple"] = "Simple",\n        ["theme_particles"] = "Particules d\'arrière-plan",\n        ["settings_scale"] = "Échelle du lanceur",\n', text)


with open('Services/L10n.cs', 'w', encoding='utf-8') as f:
    f.write(text)
