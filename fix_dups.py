import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('''        LauncherScale = LauncherScale,
                SelectedParticle = SelectedParticle,
                SelectedParticle = SelectedParticle,
                LauncherScale = LauncherScale,''', '''        LauncherScale = LauncherScale,
                SelectedParticle = SelectedParticle,''')

# Let's also check if there's another occurrence (e.g. in GetLauncherConfigSnapshot).
text = text.replace('''LauncherScale = LauncherScale,
                SelectedParticle = SelectedParticle,
        LauncherScale = LauncherScale,''', '''LauncherScale = LauncherScale,
        SelectedParticle = SelectedParticle,''')

# Just use regex to remove exact duplicates of those lines.
text = re.sub(r'(SelectedParticle = SelectedParticle,\s*)+', r'SelectedParticle = SelectedParticle,\n                ', text)
text = re.sub(r'(LauncherScale = LauncherScale,\s*)+', r'LauncherScale = LauncherScale,\n                ', text)

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
