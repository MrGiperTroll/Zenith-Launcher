import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('x:Name="DragOverlayPanel" IsHitTestVisible="False"', 'x:Name="DragOverlayPanel" IsHitTestVisible="False" ClipToBounds="False"')

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
