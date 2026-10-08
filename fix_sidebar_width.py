import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('public double SidebarWidth => IsSidebarExpanded ? 200 : 64;', 'public double SidebarWidth => IsSidebarExpanded ? 240 : 64;')

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
