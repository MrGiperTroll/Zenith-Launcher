import re

with open('App.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('\\n', '\n')

with open('App.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
