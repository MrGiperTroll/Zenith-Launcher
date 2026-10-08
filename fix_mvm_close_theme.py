import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('''    [RelayCommand]
    private void OpenThemeEditor()
    {
        SwitchSettingsTab("Theme");
    }''', '''    [RelayCommand]
    private void OpenThemeEditor()
    {
        SwitchSettingsTab("Theme");
    }
    
    [RelayCommand]
    private void CloseThemeEditor()
    {
        SwitchSettingsTab("Appearance");
    }''')

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
