import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('</Panel>\n</Window>', '<controls:ParticleSystemControl ParticleType="{Binding SelectedParticle}" IsHitTestVisible="False"/>\n    </Panel>\n</Window>')

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
