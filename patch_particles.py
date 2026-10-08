import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

prop = '''    [ObservableProperty] private string _selectedParticle = "None";

    partial void OnSelectedParticleChanged(string value) => SaveLauncherConfig();
'''

text = re.sub(r'(\[ObservableProperty\] private double _launcherScale = 1\.0;\n)', r'\1\n' + prop, text)

# save config
save_replace = r'LauncherScale = LauncherScale,\n                SelectedParticle = SelectedParticle,'
text = re.sub(r'LauncherScale = LauncherScale,', save_replace, text, count=1)
text = re.sub(r'LauncherScale = LauncherScale,', save_replace, text, count=1)

# load config
load_replace = r'if (cfg.LauncherScale > 0.4 && cfg.LauncherScale < 3.0) LauncherScale = cfg.LauncherScale;\n                    if (!string.IsNullOrEmpty(cfg.SelectedParticle)) SelectedParticle = cfg.SelectedParticle;'
text = re.sub(r'if \(cfg.LauncherScale > 0.4 && cfg.LauncherScale < 3.0\) LauncherScale = cfg.LauncherScale;', load_replace, text)

prop_replace = r'"LauncherScale" or "SelectedParticle")'
text = re.sub(r'"LauncherScale"\)', prop_replace, text)

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)

with open('Models/LauncherConfigData.cs', 'r', encoding='utf-8') as f:
    config_text = f.read()

config_text = re.sub(r'public double LauncherScale', r'public string SelectedParticle { get; set; } = "None";\n    public double LauncherScale', config_text)

with open('Models/LauncherConfigData.cs', 'w', encoding='utf-8') as f:
    f.write(config_text)
