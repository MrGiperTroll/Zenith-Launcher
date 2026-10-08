with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

# 1. Add IsSettingsTabTheme
text = text.replace('public bool IsSettingsTabAppearance => CurrentSettingsTab is "Appearance" or "Launcher";', 
                    'public bool IsSettingsTabAppearance => CurrentSettingsTab is "Appearance" or "Launcher";\n    public bool IsSettingsTabTheme => CurrentSettingsTab == "Theme";')

# 2. Add OnPropertyChanged for IsSettingsTabTheme
text = text.replace('OnPropertyChanged(nameof(IsSettingsTabAppearance));', 
                    'OnPropertyChanged(nameof(IsSettingsTabAppearance));\n        OnPropertyChanged(nameof(IsSettingsTabTheme));')

# 3. Replace OpenThemeSettings exactly
original_method = '''    [RelayCommand]
    private void OpenThemeSettings()
    {
        // For now, we will open a new Theme Window instance.
        // We will create ThemeSettingsWindow soon.
        var window = new ThemeSettingsWindow
        {
            DataContext = this
        };
        // Needs a valid lifetime reference, usually we can just Show()
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            window.ShowDialog(desktop.MainWindow);
        }
    }'''

new_method = '''    [RelayCommand]
    private void OpenThemeEditor()
    {
        SwitchSettingsTab("Theme");
    }'''

text = text.replace(original_method, new_method)

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
