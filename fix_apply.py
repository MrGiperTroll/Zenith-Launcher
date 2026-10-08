import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('ZenithTheme.Apply(ZenithTheme.DefaultAccent);', 'ZenithTheme.Apply(new Models.LauncherConfigData { AccentColor = ZenithTheme.DefaultAccent });')

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
