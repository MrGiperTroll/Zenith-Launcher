import re

with open('Views/MainWindow.axaml.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('_draggedCard.Opacity = 0.01;', '_draggedCard.Opacity = 0.4;')

with open('Views/MainWindow.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(text)
