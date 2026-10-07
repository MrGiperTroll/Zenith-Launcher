import re
with open('Services/L10n.cs', 'r', encoding='utf-8-sig') as f:
    content = f.read()
content = content.replace('[\"cp_upload_icon\"] = \"Загрузить иконку\"', '[\"cp_upload_icon\"] = \"Загрузить\"')
content = content.replace('[\"cp_upload_icon\"] = \"Завантажити іконку\"', '[\"cp_upload_icon\"] = \"Завантажити\"')
with open('Services/L10n.cs', 'w', encoding='utf-8-sig') as f:
    f.write(content)
