import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

props = '''    [ObservableProperty] private bool _isSettingsTabTheme;
'''

text = text.replace('private bool _isSettingsTabAppearance;', 'private bool _isSettingsTabAppearance;\n' + props)

cmd = '''
    [RelayCommand]
    public void OpenThemeEditor()
    {
        IsSettingsTabGame = false;
        IsSettingsTabAppearance = false;
        IsSettingsTabTheme = true;
    }
'''

text = text.replace('public void OpenSettingsDialog()', cmd + '\n    [RelayCommand]\n    public void OpenSettingsDialog()')

text = text.replace('IsSettingsTabGame = false;\n        IsSettingsTabAppearance = true;', 'IsSettingsTabGame = false;\n        IsSettingsTabAppearance = true;\n        IsSettingsTabTheme = false;')

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
