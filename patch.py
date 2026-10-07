import re

with open('Services/L10n.cs', 'r', encoding='utf-8') as f:
    code = f.read()

code = re.sub(r'\["cp_upload_icon"\]\s*=\s*".*?"', '["cp_upload_icon"] = "Upload"', code)

with open('Services/L10n.cs', 'w', encoding='utf-8') as f:
    f.write(code)

