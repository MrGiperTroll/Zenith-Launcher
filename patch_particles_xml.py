import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

pattern = r'(<Panel Background="#080A0F">)'
replacement = r'\1\n        <controls:ParticleSystemControl ParticleType="{Binding SelectedParticle}" IsHitTestVisible="False"/>'
text = re.sub(pattern, replacement, text)

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
