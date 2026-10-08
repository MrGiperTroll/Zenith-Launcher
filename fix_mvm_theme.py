import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

# 1. Add IsSettingsTabTheme
text = text.replace('public bool IsSettingsTabAppearance => CurrentSettingsTab is "Appearance" or "Launcher";', 
                    'public bool IsSettingsTabAppearance => CurrentSettingsTab is "Appearance" or "Launcher";\n    public bool IsSettingsTabTheme => CurrentSettingsTab == "Theme";')

# 2. Add OnPropertyChanged for IsSettingsTabTheme
text = text.replace('OnPropertyChanged(nameof(IsSettingsTabAppearance));', 
                    'OnPropertyChanged(nameof(IsSettingsTabAppearance));\n        OnPropertyChanged(nameof(IsSettingsTabTheme));')

# 3. Rename and fix OpenThemeSettings -> OpenThemeEditorCommand
pattern = r'\[RelayCommand\]\s*private void OpenThemeSettings\(\)\s*\{.*?\}'
replacement = r'''[RelayCommand]
    private void OpenThemeEditor()
    {
        SwitchSettingsTab("Theme");
    }'''

text = re.sub(pattern, replacement, text, flags=re.DOTALL)

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
