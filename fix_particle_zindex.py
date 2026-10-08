import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('<controls:ParticleSystemControl ParticleType="{Binding SelectedParticle}" IsHitTestVisible="False"/>', '')
text = text.replace('          <!-- MODAL: Restart Confirmation (Theme Application) -->', '          <controls:ParticleSystemControl ParticleType="{Binding SelectedParticle}" IsHitTestVisible="False"/>\n          <!-- MODAL: Restart Confirmation (Theme Application) -->')

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
