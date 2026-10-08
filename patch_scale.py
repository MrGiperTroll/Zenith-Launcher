import re

# Update Models/LauncherConfigData.cs
with open('Models/LauncherConfigData.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = re.sub(r'public bool IsSidebarExpanded', r'public double LauncherScale { get; set; } = 1.0;\n    public bool IsSidebarExpanded', text)

with open('Models/LauncherConfigData.cs', 'w', encoding='utf-8') as f:
    f.write(text)

# Update ViewModels/MainWindowViewModel.cs
with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

scale_prop = '''    [ObservableProperty] private double _launcherScale = 1.0;

    partial void OnLauncherScaleChanged(double value) => SaveLauncherConfig();
'''
text = re.sub(r'(\[ObservableProperty\] private bool _discordRpcEnabled = true;\n)', r'\1\n' + scale_prop, text)

# Add it to SaveLauncherConfig
save_replace = r'DiscordRpcEnabled = DiscordRpcEnabled,\n                LauncherScale = LauncherScale,'
text = re.sub(r'DiscordRpcEnabled = DiscordRpcEnabled,', save_replace, text, count=1)

save_replace_2 = r'DiscordRpcEnabled = DiscordRpcEnabled,\n        LauncherScale = LauncherScale,'
text = re.sub(r'DiscordRpcEnabled = DiscordRpcEnabled,', save_replace_2, text, count=1)

# Add it to LoadLauncherConfig
load_replace = r'DiscordRpcEnabled = cfg.DiscordRpcEnabled;\n                    if (cfg.LauncherScale > 0.4 && cfg.LauncherScale < 3.0) LauncherScale = cfg.LauncherScale;'
text = re.sub(r'DiscordRpcEnabled = cfg\.DiscordRpcEnabled;', load_replace, text)

# Add to PropertyChanged check
prop_replace = r'"AutoManageJava" or "DiscordRpcEnabled" or "SelectedLanguage" or "LauncherScale")'
text = re.sub(r'"AutoManageJava" or "DiscordRpcEnabled" or "SelectedLanguage"\)', prop_replace, text)

# Add OpenThemeSettingsCommand
cmd = '''
    [RelayCommand]
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
    }
'''
text = re.sub(r'public void LoadLauncherConfig', cmd + r'\n    public void LoadLauncherConfig', text)

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
