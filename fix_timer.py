import re

with open('Views/MainWindow.axaml.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('Interval = TimeSpan.FromMilliseconds(150)', 'Interval = TimeSpan.FromMilliseconds(200)')

with open('Views/MainWindow.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(text)
