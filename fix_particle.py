import re
with open('Controls/ParticleSystemControl.cs', 'r', encoding='utf-8') as f:
    text = f.read()
text = text.replace('context.PushPreTransform', 'context.PushTransform')
with open('Controls/ParticleSystemControl.cs', 'w', encoding='utf-8') as f:
    f.write(text)
