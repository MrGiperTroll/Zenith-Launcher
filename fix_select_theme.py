import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

# Fix SelectTheme
text = text.replace('ZenithTheme.Apply(hex);', 'ZenithTheme.Apply(GetLauncherConfigSnapshot());')

# Fix RequestApplyTheme
pattern = r'PendingRestartThemeHex = targetHex;\s*IsRestartConfirmOpen = true;'
replacement = r'SelectTheme(targetHex);'
text = re.sub(pattern, replacement, text)

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
