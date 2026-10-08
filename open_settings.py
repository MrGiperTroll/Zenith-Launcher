import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('CurrentViewType = ViewType.Home;', 'CurrentViewType = ViewType.Settings;')

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
